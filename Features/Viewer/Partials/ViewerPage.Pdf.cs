using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using DocGlance.UI.Controls;
using DocGlance.Infrastructure.Logging;
using DocGlance.Core.Models;
using DocGlance.Core.Enums;
using DocGlance.Rendering;
using DocGlance.Rendering.Pdf;
using DocGlance.Rendering.Docx;
using DocGlance.Rendering.Pptx;
using DocGlance.Rendering.Xlsx;
using DocGlance.Rendering.Shared;
using DocGlance.Rendering.Pdf.Models;
using DocGlance.Interop.PdfCraft;
using DocGlance.Interop.GridCraft;
using DocGlance.Features.Viewer.Components;
using DocGlance.Features.Viewer.Formats.Text;
using DocGlance.Features.Viewer.Formats.Fallback;
using DocGlance.Features.Viewer.Formats.Pdf;
using DocGlance.Features.Viewer.Formats.Docx;
using DocGlance.Features.Viewer.Formats.Pptx;
using DocGlance.Features.Viewer.Formats.Spreadsheet;
using DocGlance.Features.Viewer.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DocGlance.Features.Viewer;

public sealed partial class ViewerPage
{
    private float DisplayScale => (float)(XamlRoot?.RasterizationScale ?? 1.0);
    
    // Natural zoom is 1.0 now since we render at screen DPI × zoom dynamically
    private float NaturalZoom => 1.0f;




    private async Task LoadDocumentAsync(DocumentItem doc)
    {
        // Top-level boundary: every caller is fire-and-forget, so an exception escaping
        // this method would be lost. Log it and show the error card instead.
        try
        {
            await LoadDocumentCoreAsync(doc);
        }
        catch (OperationCanceledException)
        {
            // Expected when switching documents or closing a tab mid-load.
        }
        catch (Exception ex)
        {
            Infrastructure.Logging.Logger.Error($"[Viewer] LoadDocumentAsync failed for {doc.FilePath}", ex);
            ShowErrorCard($"Cannot open document: {ex.Message}");
        }
    }

    private async Task LoadDocumentCoreAsync(DocumentItem doc)
    {
        if (!await FlushPendingPdfAnnotationsAsync())
            throw new InvalidOperationException(
                "The previous PDF still has unsaved annotation changes. Save them before opening another document.");

        // Correlated trace for this open attempt: every log entry inside can be tied
        // back via OperationId; duration and outcome are recorded on dispose.
        using var openOp = Logger.BeginOperation("OpenDocument", ("FilePath", doc.FilePath));
        Infrastructure.Diagnostics.DiagnosticsService.SetCurrentOperation(
            $"OpenDocument {System.IO.Path.GetExtension(doc.FilePath ?? string.Empty)}");

        _doc = doc;
        ClearPdfAnnotationHistory();
        _cts?.Cancel();

        _searchCts?.Cancel();
        _searchDebounceCts?.Cancel();
        ClearSearchHighlights();
        ClearOfficeSelection();
        ClearOfficeHighlights();
        _activeViewer = null;
        _selectedText = null;
        _officeSelectionAnchor = null;
        _officeSelectionFocus = null;
        _pptxTextMaps.Clear();
        await SpreadsheetViewerControl.CleanupAsync();
        
        await PdfViewerControl.CleanupAsync();
        await DocxViewerControl.CleanupAsync();
        await PptxViewerControl.CleanupAsync();
        
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        Thumbnails.Clear();
        ThumbnailList.ItemsSource = Thumbnails;
        _outline.Clear();
        OutlineTree.RootNodes.Clear();
        SetPdfAnnotations(Array.Empty<PdfTextAnnotation>());
        PdfAnnotationCanvas.Children.Clear();
        Toolbar.PdfMarkupToolsVisibility = Visibility.Collapsed;
        Toolbar.SetMarkupToolActive(false);

        _rotation = 0;
        _fitMode  = FitMode.None;
        Toolbar.IsFitWidthChecked = false;
        Toolbar.IsFitPageChecked = false;

        _page = 1; _total = 1;
        Toolbar.UpdatePageInfo(1, 1);

        // Sync reset state to ViewModel
        ViewModel.Reset();
        ViewModel.Document = doc;

        ShowPdfViewer();

        string ext = System.IO.Path.GetExtension(doc.FilePath ?? "").ToLowerInvariant();
        IDocumentViewerFactory factory = _viewerRegistry.Resolve(ext);

        if (factory is PdfViewerFactory)
            await LoadPdfAsync(doc.FilePath ?? string.Empty, ct);
        else if (factory is DocxViewerFactory)
            await LoadDocxAsync(doc.FilePath ?? string.Empty, ct);
        else if (factory is PptxViewerFactory)
            await LoadPptxAsync(doc.FilePath ?? string.Empty, ct);
        else
        {
            if (factory is SpreadsheetViewerFactory)
                await LoadXlsxAsync(doc.FilePath ?? string.Empty, ct);
            else if (factory is TextViewerFactory)
                await LoadTextFileAsync(doc, ct);
            else
                await ShowFallbackViewerAsync(doc);
        }

        Infrastructure.Diagnostics.DiagnosticsService.SetCurrentOperation(null);
        Infrastructure.Diagnostics.DiagnosticsService.MemoryCheckpoint("DocumentOpened");
    }

    private async Task LoadPdfAsync(string filePath, CancellationToken ct)
    {
        ShowLoading(true);
        try
        {
            Infrastructure.Logging.Logger.Log($"[PDF] Starting LoadPdfAsync for: {filePath}");
            
            Infrastructure.Logging.Logger.Log("[PDF] Calling PdfViewerService.LoadDocumentAsync");
            
            // Delegate PDF loading to service (Phase 2: LoadDocument extraction)
            await PdfViewerControl.LoadAsync(
                _doc ?? throw new InvalidOperationException("No document is active."),
                ct);

            // Service owns the document lifecycle — ViewerPage just holds a reference
            // Do NOT dispose here (service handles it in CloseDocument)
            _pdfState = PdfViewerControl.DocumentState;
            _total = _pdfState?.PageCount ?? 0;
            
            if (_pdfState == null)
            {
                Infrastructure.Logging.Logger.Log("[PDF] ERROR: _pdfState is null after successful load");
                ShowErrorCard("Failed to load PDF document");
                return;
            }

            Infrastructure.Logging.Logger.Log($"[PDF] Document loaded: {_total} pages");
            Toolbar.PdfMarkupToolsVisibility = _pdfState.ManagedPdfDocument == null
                ? Visibility.Visible
                : Visibility.Collapsed;
            await LoadPdfNavigationDataAsync(_pdfState, ct);

            Toolbar.UpdatePageInfo(_page, _total);
            _page = 1;

            // Sync pagination to ViewModel
            ViewModel.TotalPages  = _total;
            ViewModel.CurrentPage = _page;

            Infrastructure.Logging.Logger.Log("[PDF] Applying viewer settings");
            
            // Apply default zoom and UI settings from configuration
            double targetZoom = Infrastructure.Configuration.AppSettings.DefaultZoomPercent / 100.0;
            _zoom = targetZoom;
            DocumentScrollViewer.ChangeView(null, null, (float)targetZoom, disableAnimation: true);
            ZoomLabel.Text = $"{Infrastructure.Configuration.AppSettings.DefaultZoomPercent}%";
            ViewModel.NaturalZoom = NaturalZoom;
            ViewModel.Zoom        = _zoom;
            
            // Apply default zoom mode from settings
            string zoomMode = Infrastructure.Configuration.AppSettings.DefaultZoomMode;
            if (zoomMode == "FitWidth")
            {
                SetFitMode(FitMode.FitWidth);
                ApplyFitWidth();
            }
            else if (zoomMode == "FitPage")
            {
                SetFitMode(FitMode.FitPage);
                ApplyFitPage();
            }
            else if (zoomMode == "ActualSize")
            {
                SetFitMode(FitMode.None);
                SetZoom(1.0);
            }

            // Auto-hide the sidebar for single-page documents
            if (_total == 1)
            {
                Toolbar.SidebarIsChecked = false;
                SidebarCard.Visibility   = Visibility.Collapsed;
                SidebarColumn.Width      = new Microsoft.UI.Xaml.GridLength(0);
                ViewModel.SidebarOpen    = false;
            }
            else
            {
                // For multi-page documents, use settings
                bool showSidebar = Infrastructure.Configuration.AppSettings.ShowSidebar;
                Toolbar.SidebarIsChecked = showSidebar;
                SidebarCard.Visibility   = showSidebar ? Visibility.Visible : Visibility.Collapsed;
                SidebarColumn.Width      = showSidebar ? new Microsoft.UI.Xaml.GridLength(240) : new Microsoft.UI.Xaml.GridLength(0);
                ViewModel.SidebarOpen    = showSidebar;
            }

            Infrastructure.Logging.Logger.Log("[PDF] Creating thumbnail placeholders");
            foreach (PageThumbnailItem thumbnail in PdfViewerControl.Thumbnails)
                Thumbnails.Add(thumbnail);

            Infrastructure.Logging.Logger.Log("[PDF] Initializing viewport rendering");
            
            // VIEWPORT RENDERING: Initialize viewport system
            InitializeViewportRendering();
            ScheduleFitModeReapply();

            Infrastructure.Logging.Logger.Log("[PDF] Hiding loading indicator");
            
            ShowLoading(false);
            DocumentScrollViewer.UpdateLayout();

            Infrastructure.Logging.Logger.Log("[PDF] Starting thumbnail prefetch");
            
            PdfViewerControl.PrefetchThumbnailsAroundAsync(
                0, SidebarCard.Visibility == Visibility.Visible, ct)
                .SafeFireAndForget("PdfThumbnailPrefetch");
            
            Infrastructure.Logging.Logger.Log("[PDF] LoadPdfAsync completed successfully");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Infrastructure.Logging.Logger.Error($"[PDF] LoadPdfAsync failed: {ex.Message}", ex);
            ShowErrorCard($"Cannot open PDF\n{ex.Message}");
        }
        finally { 
            ShowLoading(false); 
            Infrastructure.Logging.Logger.Log("[PDF] LoadPdfAsync finally block executed");
        }
    }

    // ── Non-PDF Viewers ───────────────────────────────────────────────────────

    private async Task ShowFallbackViewerAsync(DocumentItem doc)
    {
        if (_fallbackViewer != null)
            await _fallbackViewer.CleanupAsync();

        _fallbackViewer = (FallbackViewer)_viewerRegistry.Resolve(doc.FilePath ?? string.Empty).Create(ViewModel);
        _fallbackViewer.OpenRequested += (_, _) => OpenExternalButton_Click(this, EventArgs.Empty);
        await _fallbackViewer.LoadAsync(doc, CancellationToken.None);
        _activeViewer = _fallbackViewer;
        UnsupportedFileMessage.Content = _fallbackViewer.Control;
        _total = 1;
        Toolbar.UpdatePageInfo(1, 1);
        UnsupportedFileMessage.Visibility = Visibility.Visible;
        
        // Hide other viewers and sidebar
        PdfViewerHost.Visibility = Visibility.Collapsed;
        DocxViewerHost.Visibility = Visibility.Collapsed;
        PptxViewerHost.Visibility = Visibility.Collapsed;
        DocumentScrollViewer.Visibility = Visibility.Collapsed;
        TextViewerHost.Visibility = Visibility.Collapsed;
        SpreadsheetViewerHost.Visibility = Visibility.Collapsed;
        ErrorMessage.Visibility = Visibility.Collapsed;
        
        SidebarCard.Visibility = Visibility.Collapsed;
        SidebarColumn.Width = new GridLength(0);
        Toolbar.SidebarButtonVisibility = Visibility.Collapsed;
        Toolbar.RotationButtonsVisibility = Visibility.Collapsed;
    }

    private void ShowErrorCard(string message)
    {
        ViewModel.ErrorMessage = message;
        _activeViewer = null;
        
        // Populate the error message UI (defined in XAML)
        ErrorMessageText.Text = message;
        ErrorMessage.Visibility = Visibility.Visible;
        
        // Hide other viewers and sidebar
        PdfViewerHost.Visibility = Visibility.Collapsed;
        DocxViewerHost.Visibility = Visibility.Collapsed;
        PptxViewerHost.Visibility = Visibility.Collapsed;
        DocumentScrollViewer.Visibility = Visibility.Collapsed;
        TextViewerHost.Visibility = Visibility.Collapsed;
        SpreadsheetViewerHost.Visibility = Visibility.Collapsed;
        UnsupportedFileMessage.Visibility = Visibility.Collapsed;
        
        SidebarCard.Visibility = Visibility.Collapsed;
        SidebarColumn.Width = new GridLength(0);
        Toolbar.SidebarButtonVisibility = Visibility.Collapsed;
        Toolbar.RotationButtonsVisibility = Visibility.Collapsed;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PPTX loading orchestration
    // ══════════════════════════════════════════════════════════════════════════

    private async Task LoadPptxAsync(string filePath, CancellationToken cancellationToken)
    {
        ShowLoading(true);
        try
        {
            ShowPptxViewer();
            _pptxTextMaps.Clear();
            await PptxViewerControl.LoadAsync(
                _doc ?? throw new InvalidOperationException("No document is active."),
                cancellationToken);
            PptxDocument document = _pptxDocument
                ?? throw new InvalidOperationException("PPTX viewer did not retain the opened presentation.");

            _total = document.SlideCount;
            SetOutlineItems(PptxViewerControl.Outline);
            _page = 1;
            Toolbar.UpdatePageInfo(_page, _total);
            ViewModel.TotalPages = _total;
            ViewModel.CurrentPage = _page;

            Toolbar.SidebarIsChecked = true;
            SidebarCard.Visibility = Visibility.Visible;
            SidebarColumn.Width = new Microsoft.UI.Xaml.GridLength(240);
            ViewModel.SidebarOpen = true;
            Thumbnails.Clear();
            foreach (PageThumbnailItem thumbnail in PptxViewerControl.Thumbnails)
                Thumbnails.Add(thumbnail);
            ThumbnailList.ItemsSource = Thumbnails;

            _zoom = NaturalZoom;
            ZoomLabel.Text = "100%";
            ViewModel.NaturalZoom = NaturalZoom;
            ViewModel.Zoom = _zoom;
            SetFitMode(FitMode.FitWidth);

            await Task.Delay(100, cancellationToken);
            PptxViewerControl.ScrollViewer.UpdateLayout();
            await PptxViewerControl.RenderViewportAsync(cancellationToken);
            PptxViewerControl.StartThumbnailLoading();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error($"LoadPptxAsync: {filePath}", ex);
            ShowErrorCard($"Cannot open presentation: {ex.Message}");
        }
        finally
        {
            ShowLoading(false);
        }
    }
    // ══════════════════════════════════════════════════════════════════════════
    // SPREADSHEET (GridCraft XLSX — native Win2D grid viewer)
    // ══════════════════════════════════════════════════════════════════════════

    private async Task LoadXlsxAsync(string filePath, CancellationToken ct)
    {
        ShowLoading(true);

        // Spreadsheets have no sidebar. Hide it up-front so it doesn't flash open (the XAML
        // default is 240 px) while the workbook is still loading.
        SidebarCard.Visibility          = Visibility.Collapsed;
        SidebarColumn.Width             = new GridLength(0);
        Toolbar.SidebarButtonVisibility = Visibility.Collapsed;
        Toolbar.SidebarIsChecked        = false;
        ViewModel.SidebarOpen           = false;

        try
        {
            // Clean up any previous state (PDF doc, etc.)
            _pdfState = null;
            Thumbnails.Clear();
            _searchCts?.Cancel();
            SpreadsheetViewerControl.ClearHighlight();

            if (!GridCraftNative.IsAvailable)
            {
                ShowErrorCard("GridCraft XLSX renderer not available.\nPlease ensure gridcraft_ffi.dll is present.");
                return;
            }

            // Spreadsheet loads start at actual size; don't inherit a fit mode
            // from the previously viewed document type.
            SetFitMode(FitMode.None);

            // Switch the viewer area to the native spreadsheet grid
            ShowSpreadsheetViewer();

            await SpreadsheetViewerControl.LoadAsync(_doc!, ct);
            SpreadsheetDocument spreadsheetDocument = _spreadsheetDocument
                ?? throw new InvalidOperationException("Spreadsheet viewer did not retain the opened document.");
            _activeViewer = SpreadsheetViewerControl;

            // Toolbar: show page info as sheet count
            int sheetCount = spreadsheetDocument.SheetCount;
            _total = sheetCount;
            _page  = 1;
            Toolbar.UpdatePageInfo(1, sheetCount);
            ViewModel.TotalPages   = sheetCount;
            ViewModel.CurrentPage  = 1;

            SetZoom(1.0);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Error($"LoadXlsxAsync: {filePath}", ex);
            ShowErrorCard($"Cannot open spreadsheet: {ex.Message}");
        }
        finally
        {
            ShowLoading(false);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // DOCX (WordCraft — native DOCX parser and renderer)
    // ══════════════════════════════════════════════════════════════════════════

    private async Task LoadDocxAsync(string filePath, CancellationToken ct)
    {
        ShowLoading(true);
        try
        {
            ShowDocxViewer();
            await DocxViewerControl.LoadAsync(_doc!, ct);
            DocxDocument document = _docxDocument
                ?? throw new InvalidOperationException("DOCX viewer did not retain the opened document.");

            _total = document.PageCount;
            SetOutlineItems(DocxViewerControl.Outline);
            _page = 1;
            Toolbar.UpdatePageInfo(_page, _total);
            ViewModel.TotalPages = _total;
            ViewModel.CurrentPage = _page;

            Toolbar.SidebarIsChecked = true;
            SidebarCard.Visibility = Visibility.Visible;
            SidebarColumn.Width = new Microsoft.UI.Xaml.GridLength(240);
            ViewModel.SidebarOpen = true;

            Thumbnails.Clear();
            foreach (PageThumbnailItem thumbnail in DocxViewerControl.Thumbnails)
                Thumbnails.Add(thumbnail);
            ThumbnailList.ItemsSource = Thumbnails;

            _zoom = NaturalZoom;
            ZoomLabel.Text = "100%";
            ViewModel.NaturalZoom = NaturalZoom;
            ViewModel.Zoom = _zoom;
            SetFitMode(FitMode.FitWidth);
            ApplyFitWidth();

            await Task.Delay(100, ct);
            DocxViewerControl.ScrollViewer.UpdateLayout();
            await DocxViewerControl.RenderViewportAsync(ct);
            DocxViewerControl.StartThumbnailLoading();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error($"LoadDocxAsync: {filePath}", ex);
            ShowErrorCard($"Cannot open document: {ex.Message}");
        }
        finally
        {
            ShowLoading(false);
        }
    }


}
