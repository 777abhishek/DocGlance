// ViewerPage.xaml.cs — shell, state, lifecycle, public API
// Split into partial files:
//   ViewerPage.Toolbar.cs — toolbar button handlers
//   ViewerPage.Sidebar.cs — sidebar tab switching + thumbnail click
//   ViewerPage.Zoom.cs    — zoom pill + fit-width / fit-page

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using DocGlance.Services;
using DocGlance.Core.Models;
using DocGlance.Core.Enums;
using DocGlance.Rendering.Pdf;
using DocGlance.Rendering.Pdf.Models;
using DocGlance.Rendering.Shared;
using DocGlance.Rendering.Xlsx;
using DocGlance.Rendering.Docx;
using DocGlance.Rendering.Pptx;
using DocGlance.Features.Viewer.Formats.Text;
using DocGlance.Infrastructure.Logging;
using DocGlance.Features.Viewer.Contracts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using DocGlance.Features.Viewer.Formats.Fallback;
using DocGlance.Features.Viewer.Formats.Pdf;
using DocGlance.Features.Viewer.Formats.Docx;
using DocGlance.Features.Viewer.Formats.Pptx;
using DocGlance.Features.Viewer.Formats.Spreadsheet;
using DocGlance.Features.Viewer.Formats.Images;
using DocGlance.Features.Viewer.Formats.Markdown;
using DocGlance.Features.Viewer.Formats.Rtf;

namespace DocGlance.Features.Viewer;

public sealed partial class ViewerPage : Page
{
    private readonly DocumentViewerRegistry _viewerRegistry = new(
        new IDocumentViewerFactory[]
        {
            new PdfViewerFactory(),
            new DocxViewerFactory(),
            new PptxViewerFactory(),
            new RtfViewerFactory(),
            new TextViewerFactory(),
            new MarkdownViewerFactory(),
            new SpreadsheetViewerFactory(),
            new ImageViewerFactory(),
        },
        new FallbackViewerFactory());
    private PdfViewer? _pdfViewerControl;
    private PdfViewer PdfViewerControl =>
        _pdfViewerControl ?? throw new InvalidOperationException("The PDF viewer has not been initialized.");
    private DocxViewer? _docxViewerControl;
    private DocxViewer DocxViewerControl =>
        _docxViewerControl ?? throw new InvalidOperationException("The DOCX viewer has not been initialized.");
    private DocxDocument? _docxDocument => _docxViewerControl?.Document;
    private PptxViewer? _pptxViewerControl;
    private PptxViewer PptxViewerControl =>
        _pptxViewerControl ?? throw new InvalidOperationException("The PPTX viewer has not been initialized.");
    private PptxDocument? _pptxDocument => _pptxViewerControl?.Document;
    private TextViewer TextViewerControl { get; set; } = null!;
    private MarkdownViewer MarkdownViewerControl { get; set; } = null!;
    private SpreadsheetViewer SpreadsheetViewerControl { get; set; } = null!;
    private SpreadsheetDocument? _spreadsheetDocument => SpreadsheetViewerControl?.Document;
    private ImageViewer ImageViewerControl { get; set; } = null!;
    private FallbackViewer? _fallbackViewer;
    private IDocumentViewer? _activeViewer;
    private ScrollViewer DocumentScrollViewer =>
        _activeViewer is DocxViewer docxViewer
            ? docxViewer.ScrollViewer
            : _activeViewer is PptxViewer pptxViewer
                ? pptxViewer.ScrollViewer
            : _activeViewer is ImageViewer imageViewer
                ? imageViewer.ScrollViewer
            : PdfViewerControl.DocumentScrollViewerControl;
    private Canvas PdfViewportCanvas =>
        _activeViewer is DocxViewer docxViewer
            ? docxViewer.Canvas
            : _activeViewer is PptxViewer pptxViewer
                ? pptxViewer.Canvas
            : PdfViewerControl.PdfViewportCanvasControl;
    private Image ViewportImage => PdfViewerControl.ViewportImageControl;
    private Canvas PdfAnnotationCanvas => PdfViewerControl.PdfAnnotationCanvasControl;
    private Canvas PdfSelectionCanvas => PdfViewerControl.PdfSelectionCanvasControl;

    // ── ViewModel ─────────────────────────────────────────────────────────────
    public ViewerViewModel ViewModel { get; } = new();

    // ── Shared state (accessed by all partial files) ──────────────────────────
    // ViewModel is the sole owner of document-viewer state (page, total, zoom,
    // rotation, document, sidebar). These properties forward directly to ViewModel.
    internal DocumentItem? _doc
    {
        get => ViewModel.Document;
        set => ViewModel.Document = value;
    }
    internal PdfDocumentState? _pdfState
    {
        get => PdfViewerControl.DocumentState;
        set => PdfViewerControl.DocumentState = value;
    }
    internal double _zoom
    {
        get => ViewModel.Zoom;
        set => ViewModel.Zoom = value;
    }
    internal int _page
    {
        get => ViewModel.CurrentPage;
        set => ViewModel.CurrentPage = value;
    }
    internal int _total
    {
        get => ViewModel.TotalPages;
        set => ViewModel.TotalPages = value;
    }
    internal double _rotation
    {
        get => ViewModel.Rotation;
        set => ViewModel.Rotation = value;
    }
    internal CancellationTokenSource? _cts;
    internal List<OutlineItem>        _outline  = new();
    internal PdfViewportState? _viewportState
    {
        get => PdfViewerControl.ViewportState;
        set => PdfViewerControl.ViewportState = value;
    }
    internal const double PtToLp = 96.0 / 72.0;
    internal const double LpToPt = 72.0 / 96.0;

    private void InitializeViewportRendering() => PdfViewerControl.InitializeViewportRendering();
    private void UpdateCanvasSize() => PdfViewerControl.UpdateCanvasSize();
    private Task RenderPagesAsync() => PdfViewerControl.RenderPagesAsync();
    private (double W, double H) GetRotatedPageSize(int pageIndex) =>
        PdfViewerControl.GetRotatedPageSize(pageIndex);
    private double GetPageTopLp(int pageIndex) => PdfViewerControl.GetPageTopLp(pageIndex);
    private double GetDocxPageTopLp(int pageIndex) => DocxViewerControl.GetPageTopLp(pageIndex);
    private double GetPptxPageTopLp(int pageIndex) => PptxViewerControl.GetSlideTopLp(pageIndex);

    public ObservableCollection<PageThumbnailItem> Thumbnails { get; } = new();

    // ── UI-10: Resize debounce ───────────────────────────────────────────────
    private DispatcherTimer? _resizeDebounceTimer;

    // ── Construction ─────────────────────────────────────────────────────────
    public ViewerPage()
    {
        InitializeComponent();
        _pdfViewerControl = (PdfViewer)_viewerRegistry.Resolve(".pdf").Create(ViewModel);
        PdfViewerHost.Content = _pdfViewerControl;
        _docxViewerControl = (DocxViewer)_viewerRegistry.Resolve(".docx").Create(ViewModel);
        DocxViewerHost.Content = _docxViewerControl;
        _pptxViewerControl = (PptxViewer)_viewerRegistry.Resolve(".pptx").Create(ViewModel);
        PptxViewerHost.Content = _pptxViewerControl;
        ImageViewerControl = (ImageViewer)_viewerRegistry.Resolve(".png").Create(ViewModel);
        ImageViewerHost.Content = ImageViewerControl;
        ImageViewerControl.ZoomChanged += (_, zoom) =>
        {
            _zoom = zoom;
            ZoomLabel.Text = $"{Math.Round(_zoom / NaturalZoom * 100.0)}%";
            ViewModel.Zoom = _zoom;
        };
        ImageViewerControl.ScrollViewer.PointerWheelChanged += OnViewerPointerWheelChanged;
        PdfViewerControl.ZoomChanged += (_, zoom) =>
        {
            _zoom = zoom;
            ZoomLabel.Text = $"{Math.Round(_zoom / NaturalZoom * 100.0)}%";
            ViewModel.Zoom = _zoom;
        };
        PdfViewerControl.PageNavigationRequested += (_, page) => UpdateCurrentPage(page);
        PdfViewerControl.OutlineNavigationRequested += (_, page) => GoToPage(page);
        PdfViewerControl.AnnotationsChanged += (_, _) => UpdatePdfNoteList();
        PdfViewerControl.PageChanged += (_, page) => UpdateCurrentPage(page);
        PdfViewerControl.TextPageMapLoaded += (_, pageIndex) =>
        {
            TryStartPendingMouseSelection(pageIndex);
            TryStartTouchSelection();
        };
        PdfViewerControl.ClearTextSelectionRequested += (_, _) => ClearPdfTextSelectionState();
        DocxViewerControl.PageChanged += (_, page) => UpdateCurrentPage(page);
        DocxViewerControl.ZoomChanged += (_, zoom) =>
        {
            _zoom = zoom;
            ZoomLabel.Text = $"{Math.Round(_zoom / NaturalZoom * 100.0)}%";
        };
        PptxViewerControl.PageChanged += (_, page) => UpdateCurrentPage(page);
        PptxViewerControl.ZoomChanged += (_, zoom) =>
        {
            _zoom = zoom;
            ZoomLabel.Text = $"{Math.Round(_zoom / NaturalZoom * 100.0)}%";
        };
        TextViewerControl = (TextViewer)_viewerRegistry.Resolve(".txt").Create(ViewModel);
        TextViewerHost.Content = TextViewerControl;
        MarkdownViewerControl = (MarkdownViewer)_viewerRegistry.Resolve(".md").Create(ViewModel);
        MarkdownViewerHost.Content = MarkdownViewerControl;
        SpreadsheetViewerControl = (SpreadsheetViewer)_viewerRegistry.Resolve(".xlsx").Create(ViewModel);
        SpreadsheetViewerHost.Content = SpreadsheetViewerControl;
        InitializeViewerShortcuts();
        Toolbar.PdfToolSelected += Toolbar_PdfToolSelected;

        InitializePdfTextSelection();

        // Initialize Office text selection (drag-to-select and copy)
        InitializeOfficeTextSelection();
        TextViewerControl.TextControl.RightTapped += PlainTextBlock_RightTapped;
        SpreadsheetViewerControl.TranslateRequested += text => _ = ShowTranslationDialogAsync(text);

        // Wire viewport-resize → reapply active fit mode + re-render (the
        // viewport may have grown — re-render pages.
        // UI-10 fix: debounce to avoid render storm during drag-resize
        DocumentScrollViewer.SizeChanged += (_, _) =>
        {
            ScheduleFitModeReapply();
        };
        DocxViewerControl.SizeChanged += (_, _) => ScheduleFitModeReapply();
        DocxViewerControl.ScrollViewer.PointerWheelChanged += OnViewerPointerWheelChanged;
        PptxViewerControl.SizeChanged += (_, _) => ScheduleFitModeReapply();
        PptxViewerControl.ScrollViewer.PointerWheelChanged += OnViewerPointerWheelChanged;
        PdfViewerControl.SizeChanged += (_, _) => ScheduleFitModeReapply();
        SpreadsheetViewerControl.SizeChanged += (_, _) => ScheduleFitModeReapply();
        SpreadsheetViewerControl.ActiveSheetChanged += (_, sheetIndex) =>
        {
            if (_spreadsheetDocument == null) return;
            UpdateCurrentPage(sheetIndex + 1);
            ReapplyFitMode();
        };
        SpreadsheetViewerControl.ZoomChanged += (_, zoom) =>
        {
            _zoom = zoom;
            ZoomLabel.Text = $"{Math.Round(_zoom * 100)}%";
        };
        SpreadsheetViewerControl.SelectionError += (_, message) =>
            ShowInfoBar($"Could not copy selected cells: {message}", InfoBarSeverity.Error);
        SpreadsheetViewerControl.ManualZoomRequested += (_, _) => SetFitMode(FitMode.None);

        // Ctrl+Wheel zoom anchored at the cursor.
        DocumentScrollViewer.PointerWheelChanged += OnViewerPointerWheelChanged;

        // ViewModel commands → ViewerPage actions
        ViewModel.PageChangeRequested  += (_, page)  => GoToPage(page);
        ViewModel.ZoomInRequested      += (_, _)     => { SetFitMode(FitMode.None); SetZoom(_zoom * 1.25); };
        ViewModel.ZoomOutRequested     += (_, _)     => { SetFitMode(FitMode.None); SetZoom(_zoom / 1.25); };
        ViewModel.ZoomResetRequested   += (_, _)     => { SetFitMode(FitMode.None); SetZoom(NaturalZoom); };

        // Subscribe to settings changes for live updates
        Infrastructure.Configuration.AppSettings.SettingsChanged += OnSettingsChanged;
        
        // Unsubscribe when page unloads
        Unloaded += (_, _) => Infrastructure.Configuration.AppSettings.SettingsChanged -= OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        // Apply settings changes immediately to the current viewer
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyViewerSettings();
        });
    }

    private void ApplyViewerSettings()
    {
        // Apply show thumbnails setting via the ThumbnailList
        if (ThumbnailList != null)
        {
            ThumbnailList.Visibility = Infrastructure.Configuration.AppSettings.ShowThumbnails 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }
        
        if (SidebarCard == null)
            return;

        // This runs for EVERY AppSettings change (including ones raised just by opening the
        // Settings page) and on every open viewer tab. Only documents that have a sidebar
        // (multi-page PDF, DOCX, PPTX) may have it opened; spreadsheets, text, Markdown and
        // error/unsupported views must never get it back.
        bool supportsSidebar =
            Toolbar.SidebarButtonVisibility == Visibility.Visible &&
            !IsSpreadsheetViewerActive() &&
            _total > 1;

        bool shouldOpenSidebar =
            Infrastructure.Configuration.AppSettings.ShowSidebar && supportsSidebar;
        double targetWidth = shouldOpenSidebar ? 240 : 0;
        if (Toolbar.IsSidebarOpen != shouldOpenSidebar ||
            Math.Abs(SidebarColumn.Width.Value - targetWidth) >= 0.5)
        {
            SetSidebarOpen(shouldOpenSidebar);
        }
    }

    private void ScheduleFitModeReapply()
    {
        if (_isSidebarAnimating)
            return;

        if (_resizeDebounceTimer == null)
        {
            _resizeDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _resizeDebounceTimer.Tick += (_, _) =>
            {
                _resizeDebounceTimer?.Stop();
                PdfViewerControl.UpdateLayout();
                DocumentScrollViewer.UpdateLayout();
                ReapplyFitMode();
                if (_pdfState != null) RenderPagesAsync().SafeFireAndForget("PdfViewportRender");
            };
        }

        _resizeDebounceTimer.Stop();
        _resizeDebounceTimer.Start();
    }


    // ── Navigation ───────────────────────────────────────────────────────────
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is DocumentItem doc)
            LoadDocumentAsync(doc).SafeFireAndForget("ViewerOnNavigatedTo");
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        CancelLoad();
    }

    // Called by TitleBarWindow when the tab is closed
    public void CancelLoad()
    {
        _cts?.Cancel();

        _searchCts?.Cancel();
        _searchDebounceCts?.Cancel();

        // Release every native document this page owns. Without this, closing a tab
        // leaked the Rust-side document (whole file buffer + renderer + font caches)
        // until GC — the reported "memory grows when opening documents" symptom.
        _ = TextViewerControl.CleanupAsync();
        _ = MarkdownViewerControl?.CleanupAsync();
        _ = PptxViewerControl.CleanupAsync();
        _ = DocxViewerControl.CleanupAsync();
        _pptxTextMaps.Clear();
        ClearOfficeSelection();
        _ = SpreadsheetViewerControl?.CleanupAsync();
        _ = ImageViewerControl?.CleanupAsync();
        if (_fallbackViewer != null)
        {
            _ = _fallbackViewer.CleanupAsync();
            _fallbackViewer = null;
        }

        // The per-page service owns the PDF document — closing it frees the native handle.
        // A fresh service keeps the page usable if it is reattached later.
        _ = PdfViewerControl.CleanupAsync();
        ViewModel.Reset();
    }

    // Public entry point called by TitleBarWindow.OpenDocument
    public void LoadDocument(DocumentItem doc) => LoadDocumentAsync(doc).SafeFireAndForget("ViewerLoadDocument");

    // ── Shared helpers ────────────────────────────────────────────────────────

    // UI-04 fix: animate Opacity instead of toggling Visibility for smooth fade
    internal void ShowLoading(bool on)
    {
        LoadingOverlay.Opacity = on ? 1.0 : 0.0;
        LoadingOverlay.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ViewModel.IsLoading = on;
    }

    // Natural zoom accounts for display DPI scaling (PerMonitorV2).
    // On a 150%-scaled monitor RasterizationScale = 1.5, so "100%" zoom
    // must render at 1.5× device pixels to remain pixel-perfect.
    private float NaturalZoom => (float)(XamlRoot?.RasterizationScale ?? 1.0);

    // ── Top-level load entry point ────────────────────────────────────────────

    private async Task LoadDocumentAsync(DocumentItem doc)
    {
        // Top-level fire-and-forget boundary: exceptions must be caught here so
        // they are surfaced as error cards instead of silently lost.
        try
        {
            await LoadDocumentCoreAsync(doc);
        }
        catch (OperationCanceledException)
        {
            Infrastructure.Diagnostics.DocumentOpenTrace.For(doc)?.Mark("OpenCanceled");
            // Expected when switching documents or closing a tab mid-load.
        }
        catch (Exception ex)
        {
            Infrastructure.Diagnostics.DocumentOpenTrace.For(doc)?.Mark(
                "OpenFailed", $"ErrorType={ex.GetType().Name}");
            Logger.Error($"[Viewer] LoadDocumentAsync failed for {doc.FilePath}", ex);
            ShowErrorCard($"Cannot open document: {ex.Message}");
        }
    }

    // ── Core load — generic registry path ────────────────────────────────────

    private async Task LoadDocumentCoreAsync(DocumentItem doc)
    {
        Infrastructure.Diagnostics.DocumentOpenTrace? openTrace =
            Infrastructure.Diagnostics.DocumentOpenTrace.For(doc);
        using IDisposable? traceScope = openTrace?.Activate();
        openTrace?.Mark("ViewerLoadStarted");

        if (!await FlushPendingPdfAnnotationsAsync())
            throw new InvalidOperationException(
                "The previous PDF still has unsaved annotation changes. " +
                "Save them before opening another document.");

        using var openOp = Logger.BeginOperation("OpenDocument", ("FilePath", doc.FilePath));
        Infrastructure.Diagnostics.DiagnosticsService.SetCurrentOperation(
            $"OpenDocument {System.IO.Path.GetExtension(doc.FilePath ?? string.Empty)}");

        // ── Reset shared state ────────────────────────────────────────────────
        _doc = doc;
        ClearPdfAnnotationHistory();
        _cts?.Cancel();
        _searchCts?.Cancel();
        _searchDebounceCts?.Cancel();
        ClearSearchHighlights();
        ClearOfficeSelection();
        ClearOfficeHighlights();
        _activeViewer           = null;
        _selectedText           = null;
        _officeSelectionAnchor  = null;
        _officeSelectionFocus   = null;
        _pptxTextMaps.Clear();

        // Parallelise all viewer cleanups: they own independent native handles
        // and cancel their own CTS tokens, so running concurrently is safe and
        // avoids the previous ~6× sequential wait time before each file load.
        await Task.WhenAll(
            SpreadsheetViewerControl.CleanupAsync(),
            ImageViewerControl.CleanupAsync(),
            MarkdownViewerControl.CleanupAsync(),
            PdfViewerControl.CleanupAsync(),
            DocxViewerControl.CleanupAsync(),
            PptxViewerControl.CleanupAsync());

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
        Toolbar.IsFitPageChecked  = false;
        _page = 1; _total = 1;
        Toolbar.UpdatePageInfo(1, 1);

        ViewModel.Reset();
        ViewModel.Document = doc;

        // ── Generic registry path ─────────────────────────────────────────────
        // All formats — including XLSX — resolve through the registry and load
        // via viewer.LoadAsync. No factory type-matching occurs here.

        string ext = System.IO.Path.GetExtension(doc.FilePath ?? "").ToLowerInvariant();
        IDocumentViewerFactory factory = _viewerRegistry.Resolve(ext);

        ShowLoading(true);
        try
        {
            IDocumentViewer viewer = ActivateHostedViewer(factory, doc);
            _activeViewer = viewer;
            openTrace?.Mark("ViewerActivated", $"Viewer={viewer.GetType().Name}");

            openTrace?.Mark("FormatLoadStarted", $"Viewer={viewer.GetType().Name}");
            await viewer.LoadAsync(doc, ct);
            ct.ThrowIfCancellationRequested();
            openTrace?.Mark("FormatParseCompleted", $"Viewer={viewer.GetType().Name}");

            await ApplyPostLoadConfigAsync(viewer, ct);
            openTrace?.Mark("NavigationReady");
        }
        catch (OperationCanceledException)
        {
            openTrace?.Mark("OpenCanceled");
            throw;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            openTrace?.Mark("FormatLoadFailed", $"ErrorType={ex.GetType().Name}");
            Logger.Error($"[Viewer] LoadDocumentCoreAsync: {ex.Message}", ex);
            ShowErrorCard($"Cannot open document: {ex.Message}");
        }
        finally
        {
            ShowLoading(false);
            Infrastructure.Diagnostics.DiagnosticsService.SetCurrentOperation(null);
            Infrastructure.Diagnostics.DiagnosticsService.MemoryCheckpoint("DocumentOpened");
        }
    }

    // ── Viewer activation ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the pre-hosted viewer instance for the given factory and activates
    /// the appropriate host panel. FallbackViewer is constructed fresh per load
    /// and placed in <c>UnsupportedFileMessage</c>.
    /// </summary>
    private IDocumentViewer ActivateHostedViewer(IDocumentViewerFactory factory, DocumentItem doc)
    {
        switch (factory)
        {
            case PdfViewerFactory:
                ShowPdfViewer();
                return PdfViewerControl;

            case DocxViewerFactory:
                ShowDocxViewer();
                return DocxViewerControl;

            case PptxViewerFactory:
                ShowPptxViewer();
                return PptxViewerControl;

            case SpreadsheetViewerFactory:
                // XLSX routed through the shared viewer path (Phase 2 completion).
                ShowSpreadsheetViewer();
                return SpreadsheetViewerControl;

            case TextViewerFactory:
                ShowTextViewer();
                return TextViewerControl;

            case MarkdownViewerFactory:
                ShowMarkdownViewer();
                return MarkdownViewerControl;

            case ImageViewerFactory:
                ShowImageViewer();
                return ImageViewerControl;

            case RtfViewerFactory:
                // RTF viewer shows its own message if loading fails
                return ActivateFallbackViewer(factory, doc);

            default:
                return ActivateFallbackViewer(factory, doc);
        }
    }

    private IDocumentViewer ActivateFallbackViewer(IDocumentViewerFactory factory, DocumentItem doc)
    {
        // Cleanup previous fallback viewer
        if (_fallbackViewer != null)
            _ = _fallbackViewer.CleanupAsync();

        // Create the viewer
        var viewer = factory.Create(ViewModel);
        
        // Only try to cast to FallbackViewer if it's actually a FallbackViewerFactory
        if (factory is FallbackViewerFactory)
        {
            _fallbackViewer = (FallbackViewer)viewer;
            _fallbackViewer.OpenRequested += (_, _) => OpenExternalButton_Click(this, EventArgs.Empty);
        }
        else
        {
            _fallbackViewer = null;
        }

        PdfViewerHost.Visibility          = Visibility.Collapsed;
        DocxViewerHost.Visibility         = Visibility.Collapsed;
        PptxViewerHost.Visibility         = Visibility.Collapsed;
        DocumentScrollViewer.Visibility   = Visibility.Collapsed;
        TextViewerHost.Visibility         = Visibility.Collapsed;
        MarkdownViewerHost.Visibility     = Visibility.Collapsed;
        SpreadsheetViewerHost.Visibility  = Visibility.Collapsed;
        ImageViewerHost.Visibility        = Visibility.Collapsed;
        ErrorMessage.Visibility           = Visibility.Collapsed;
        UnsupportedFileMessage.Content    = viewer.Control;
        UnsupportedFileMessage.Visibility = Visibility.Visible;

        SidebarCard.Visibility              = Visibility.Collapsed;
        SidebarColumn.Width                 = new GridLength(0);
        Toolbar.SidebarButtonVisibility     = Visibility.Collapsed;
        Toolbar.RotationButtonsVisibility   = Visibility.Collapsed;

        return viewer;
    }

    // ── Post-load configuration ───────────────────────────────────────────────

    /// <summary>
    /// After <c>viewer.LoadAsync</c> succeeds, apply shell-side wiring that
    /// cannot live inside the viewer: page counts, thumbnails, outline tree,
    /// zoom defaults, and sidebar visibility. Uses ISupports* and viewer-concrete
    /// checks — no factory type-matching.
    /// </summary>
    private async Task ApplyPostLoadConfigAsync(IDocumentViewer viewer, CancellationToken ct)
    {
        // ── PDF ───────────────────────────────────────────────────────────────
        if (viewer is PdfViewer pdfViewer)
        {
            _pdfState = pdfViewer.DocumentState;
            _total    = _pdfState?.PageCount ?? 0;

            if (_pdfState == null)
            {
                ShowErrorCard("Failed to load PDF document");
                return;
            }

            Toolbar.PdfMarkupToolsVisibility = _pdfState.ManagedPdfDocument == null
                ? Visibility.Visible
                : Visibility.Collapsed;

            await LoadPdfNavigationDataAsync(_pdfState, ct);

            _page = 1;
            Toolbar.UpdatePageInfo(_page, _total);
            ViewModel.TotalPages  = _total;
            ViewModel.CurrentPage = _page;

            double targetZoom = Infrastructure.Configuration.AppSettings.DefaultZoomPercent / 100.0;
            _zoom = targetZoom;
            DocumentScrollViewer.ChangeView(null, null, (float)targetZoom, disableAnimation: true);
            ZoomLabel.Text        = $"{Infrastructure.Configuration.AppSettings.DefaultZoomPercent}%";
            ViewModel.NaturalZoom = NaturalZoom;
            ViewModel.Zoom        = _zoom;

            string zoomMode = Infrastructure.Configuration.AppSettings.DefaultZoomMode;
            if      (zoomMode == "FitWidth")   { SetFitMode(FitMode.FitWidth); ApplyFitWidth(); }
            else if (zoomMode == "FitPage")    { SetFitMode(FitMode.FitPage);  ApplyFitPage();  }
            else if (zoomMode == "ActualSize") { SetFitMode(FitMode.None);     SetZoom(1.0);    }

            if (_total == 1)
                ConfigureSidebar(false, singlePage: true);
            else
                ConfigureSidebar(Infrastructure.Configuration.AppSettings.ShowSidebar);

            foreach (PageThumbnailItem thumbnail in pdfViewer.Thumbnails)
                Thumbnails.Add(thumbnail);

            ShowLoading(false);
            DocumentScrollViewer.UpdateLayout();
            InitializeViewportRendering();
            ScheduleFitModeReapply();

            pdfViewer.PrefetchThumbnailsAroundAsync(
                0, SidebarCard.Visibility == Visibility.Visible, ct)
                .SafeFireAndForget("PdfThumbnailPrefetch");
            return;
        }

        // ── DOCX ──────────────────────────────────────────────────────────────
        if (viewer is DocxViewer docxViewer)
        {
            DocxDocument document = docxViewer.Document
                ?? throw new InvalidOperationException(
                    "DOCX viewer did not retain the opened document.");
            _total = document.PageCount;
            SetOutlineItems(docxViewer.Outline);
            _page = 1;
            Toolbar.UpdatePageInfo(_page, _total);
            ViewModel.TotalPages  = _total;
            ViewModel.CurrentPage = _page;

            ConfigureSidebar(true);

            foreach (PageThumbnailItem thumbnail in docxViewer.Thumbnails)
                Thumbnails.Add(thumbnail);
            ThumbnailList.ItemsSource = Thumbnails;

            _zoom = NaturalZoom;
            ZoomLabel.Text        = "100%";
            ViewModel.NaturalZoom = NaturalZoom;
            ViewModel.Zoom        = _zoom;
            SetFitMode(FitMode.FitWidth);
            ApplyFitWidth();

            await Task.Delay(100, ct);
            docxViewer.ScrollViewer.UpdateLayout();
            await docxViewer.RenderViewportAsync(ct);
            docxViewer.StartThumbnailLoading();
            return;
        }

        // ── PPTX ──────────────────────────────────────────────────────────────
        if (viewer is PptxViewer pptxViewer)
        {
            PptxDocument document = pptxViewer.Document
                ?? throw new InvalidOperationException(
                    "PPTX viewer did not retain the opened presentation.");
            _pptxTextMaps.Clear();
            _total = document.SlideCount;
            SetOutlineItems(pptxViewer.Outline);
            _page = 1;
            Toolbar.UpdatePageInfo(_page, _total);
            ViewModel.TotalPages  = _total;
            ViewModel.CurrentPage = _page;

            ConfigureSidebar(true);

            Thumbnails.Clear();
            foreach (PageThumbnailItem thumbnail in pptxViewer.Thumbnails)
                Thumbnails.Add(thumbnail);
            ThumbnailList.ItemsSource = Thumbnails;

            _zoom = NaturalZoom;
            ZoomLabel.Text        = "100%";
            ViewModel.NaturalZoom = NaturalZoom;
            ViewModel.Zoom        = _zoom;
            SetFitMode(FitMode.FitWidth);

            await Task.Delay(100, ct);
            pptxViewer.ScrollViewer.UpdateLayout();
            await pptxViewer.RenderViewportAsync(ct);
            pptxViewer.StartThumbnailLoading();
            return;
        }

        // ── XLSX (Spreadsheet) ────────────────────────────────────────────────
        if (viewer is SpreadsheetViewer spreadsheetViewer)
        {
            // GridCraft availability is enforced inside SpreadsheetViewer.LoadAsync.
            // If it threw, we never reach here.
            SpreadsheetDocument spreadsheetDocument = spreadsheetViewer.Document
                ?? throw new InvalidOperationException(
                    "Spreadsheet viewer did not retain the opened document.");

            _activeViewer = spreadsheetViewer;
            int sheetCount = spreadsheetDocument.SheetCount;
            _total = sheetCount;
            _page  = 1;
            Toolbar.UpdatePageInfo(1, sheetCount);
            ViewModel.TotalPages  = sheetCount;
            ViewModel.CurrentPage = 1;
            SetFitMode(FitMode.None);
            SetZoom(1.0);
            return;
        }

        if (viewer is ImageViewer imageViewer)
        {
            _total = 1;
            _page = 1;
            Toolbar.UpdatePageInfo(1, 1);
            ViewModel.TotalPages = 1;
            ViewModel.CurrentPage = 1;
            ViewModel.NaturalZoom = NaturalZoom;
            ConfigureSidebar(false, singlePage: true);
            SetFitMode(FitMode.FitPage);
            ZoomLabel.Text = $"{Math.Round(imageViewer.CurrentZoom / NaturalZoom * 100.0)}%";
            ViewModel.Zoom = imageViewer.CurrentZoom;
            return;
        }

        // ── Text / Markdown / Code ────────────────────────────────────────────
        if (viewer is TextViewer or MarkdownViewer)
        {
            _activeViewer = viewer;
            _total = 1;
            _page  = 1;
            Toolbar.UpdatePageInfo(1, 1);
            return;
        }

        // ── Fallback (unsupported / .xls) ─────────────────────────────────────
        _total = 1;
        Toolbar.UpdatePageInfo(1, 1);
    }

    // ── Sidebar helper ────────────────────────────────────────────────────────

    /// <summary>
    /// Sets sidebar open/collapsed. When <paramref name="singlePage"/> is true,
    /// the sidebar is always collapsed regardless of <paramref name="showSidebar"/>.
    /// </summary>
    private void ConfigureSidebar(bool showSidebar, bool singlePage = false)
    {
        bool open = !singlePage && showSidebar;
        Toolbar.SidebarIsChecked = open;
        SidebarCard.Visibility   = open ? Visibility.Visible : Visibility.Collapsed;
        SidebarColumn.Width      = open
            ? new Microsoft.UI.Xaml.GridLength(240)
            : new Microsoft.UI.Xaml.GridLength(0);
        ViewModel.SidebarOpen    = open;
    }

    // ── Error card ────────────────────────────────────────────────────────────

    private void ShowErrorCard(string message)
    {
        ViewModel.ErrorMessage = message;
        _activeViewer = null;

        ErrorMessageText.Text   = message;
        ErrorMessage.Visibility = Visibility.Visible;

        PdfViewerHost.Visibility          = Visibility.Collapsed;
        DocxViewerHost.Visibility         = Visibility.Collapsed;
        PptxViewerHost.Visibility         = Visibility.Collapsed;
        DocumentScrollViewer.Visibility   = Visibility.Collapsed;
        TextViewerHost.Visibility         = Visibility.Collapsed;
        MarkdownViewerHost.Visibility     = Visibility.Collapsed;
        SpreadsheetViewerHost.Visibility  = Visibility.Collapsed;
        ImageViewerHost.Visibility        = Visibility.Collapsed;
        UnsupportedFileMessage.Visibility = Visibility.Collapsed;

        SidebarCard.Visibility              = Visibility.Collapsed;
        SidebarColumn.Width                 = new GridLength(0);
        Toolbar.SidebarButtonVisibility     = Visibility.Collapsed;
        Toolbar.RotationButtonsVisibility   = Visibility.Collapsed;
    }

    // ── Resource helpers (absorbed from ViewerPage.Helpers.cs) ────────────────
    internal static Brush ResBrush(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var v) && v is Brush b)
            return b;
        return new SolidColorBrush(Colors.Transparent);
    }

    internal static Style? ResStyle(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var v) && v is Style s)
            return s;
        return null;
    }

    internal void ShowPdfViewer()
    {
        _activeViewer = PdfViewerControl;
        PdfViewerHost.Visibility = Visibility.Visible;
        DocxViewerHost.Visibility = Visibility.Collapsed;
        PptxViewerHost.Visibility = Visibility.Collapsed;
        TextViewerHost.Visibility = Visibility.Collapsed;
        MarkdownViewerHost.Visibility = Visibility.Collapsed;
        DocumentScrollViewer.Visibility = Visibility.Visible;
        SpreadsheetViewerHost.Visibility = Visibility.Collapsed;
        ImageViewerHost.Visibility = Visibility.Collapsed;
        UnsupportedFileMessage.Visibility = Visibility.Collapsed;
        ErrorMessage.Visibility = Visibility.Collapsed;
        Toolbar.WordWrapVisibility = Visibility.Collapsed;
        Toolbar.RotationButtonsVisibility = Visibility.Visible;
        Toolbar.SidebarButtonVisibility = Visibility.Visible;
        Toolbar.FitWidthButtonVisibility = Visibility.Visible;
        Toolbar.FitPageButtonVisibility = Visibility.Visible;
    }

    internal void ShowDocxViewer()
    {
        _activeViewer = DocxViewerControl;
        PdfViewerHost.Visibility = Visibility.Collapsed;
        DocxViewerHost.Visibility = Visibility.Visible;
        PptxViewerHost.Visibility = Visibility.Collapsed;
        TextViewerHost.Visibility = Visibility.Collapsed;
        MarkdownViewerHost.Visibility = Visibility.Collapsed;
        SpreadsheetViewerHost.Visibility = Visibility.Collapsed;
        ImageViewerHost.Visibility = Visibility.Collapsed;
        UnsupportedFileMessage.Visibility = Visibility.Collapsed;
        ErrorMessage.Visibility = Visibility.Collapsed;
        Toolbar.WordWrapVisibility = Visibility.Collapsed;
        Toolbar.RotationButtonsVisibility = Visibility.Visible;
        Toolbar.SidebarButtonVisibility = Visibility.Visible;
        Toolbar.FitWidthButtonVisibility = Visibility.Visible;
        Toolbar.FitPageButtonVisibility = Visibility.Visible;
    }

    internal void ShowPptxViewer()
    {
        _activeViewer = PptxViewerControl;
        PdfViewerHost.Visibility = Visibility.Collapsed;
        DocxViewerHost.Visibility = Visibility.Collapsed;
        PptxViewerHost.Visibility = Visibility.Visible;
        TextViewerHost.Visibility = Visibility.Collapsed;
        MarkdownViewerHost.Visibility = Visibility.Collapsed;
        SpreadsheetViewerHost.Visibility = Visibility.Collapsed;
        ImageViewerHost.Visibility = Visibility.Collapsed;
        UnsupportedFileMessage.Visibility = Visibility.Collapsed;
        ErrorMessage.Visibility = Visibility.Collapsed;
        Toolbar.WordWrapVisibility = Visibility.Collapsed;
        Toolbar.RotationButtonsVisibility = Visibility.Visible;
        Toolbar.SidebarButtonVisibility = Visibility.Visible;
        Toolbar.FitWidthButtonVisibility = Visibility.Visible;
        Toolbar.FitPageButtonVisibility = Visibility.Visible;
    }

    internal void ShowSpreadsheetViewer()
    {
        _activeViewer = SpreadsheetViewerControl;
        PdfViewerHost.Visibility = Visibility.Collapsed;
        DocxViewerHost.Visibility = Visibility.Collapsed;
        PptxViewerHost.Visibility = Visibility.Collapsed;
        TextViewerHost.Visibility = Visibility.Collapsed;
        MarkdownViewerHost.Visibility = Visibility.Collapsed;
        DocumentScrollViewer.Visibility = Visibility.Collapsed;
        SpreadsheetViewerHost.Visibility = Visibility.Visible;
        ImageViewerHost.Visibility = Visibility.Collapsed;
        UnsupportedFileMessage.Visibility = Visibility.Collapsed;
        ErrorMessage.Visibility = Visibility.Collapsed;
        Toolbar.WordWrapVisibility = Visibility.Collapsed;
        Toolbar.RotationButtonsVisibility = Visibility.Collapsed;
        Toolbar.SidebarButtonVisibility = Visibility.Collapsed;
        Toolbar.FitWidthButtonVisibility = Visibility.Visible;
        Toolbar.FitPageButtonVisibility = Visibility.Visible;
        SidebarCard.Visibility = Visibility.Collapsed;
        SidebarColumn.Width = new GridLength(0);
        ViewModel.SidebarOpen = false;
    }

    internal void ShowTextViewer()
    {
        _activeViewer                   = TextViewerControl;
        TextViewerHost.Visibility       = Visibility.Visible;
        MarkdownViewerHost.Visibility   = Visibility.Collapsed;
        PdfViewerHost.Visibility        = Visibility.Collapsed;
        DocxViewerHost.Visibility       = Visibility.Collapsed;
        PptxViewerHost.Visibility       = Visibility.Collapsed;
        DocumentScrollViewer.Visibility = Visibility.Collapsed;
        SpreadsheetViewerHost.Visibility = Visibility.Collapsed;
        ImageViewerHost.Visibility = Visibility.Collapsed;
        UnsupportedFileMessage.Visibility = Visibility.Collapsed;
        ErrorMessage.Visibility         = Visibility.Collapsed;

        Toolbar.WordWrapVisibility        = Visibility.Visible;
        Toolbar.RotationButtonsVisibility = Visibility.Collapsed;
        Toolbar.SidebarButtonVisibility   = Visibility.Collapsed;
        Toolbar.FitWidthButtonVisibility  = Visibility.Collapsed;
        Toolbar.FitPageButtonVisibility   = Visibility.Collapsed;
        Toolbar.IsWordWrapChecked         = false;

        SidebarCard.Visibility = Visibility.Collapsed;
        SidebarColumn.Width    = new GridLength(0);
        ViewModel.SidebarOpen  = false;
    }

    internal void ShowMarkdownViewer()
    {
        _activeViewer = MarkdownViewerControl;
        MarkdownViewerHost.Visibility = Visibility.Visible;
        TextViewerHost.Visibility = Visibility.Collapsed;
        PdfViewerHost.Visibility = Visibility.Collapsed;
        DocxViewerHost.Visibility = Visibility.Collapsed;
        PptxViewerHost.Visibility = Visibility.Collapsed;
        DocumentScrollViewer.Visibility = Visibility.Collapsed;
        SpreadsheetViewerHost.Visibility = Visibility.Collapsed;
        ImageViewerHost.Visibility = Visibility.Collapsed;
        UnsupportedFileMessage.Visibility = Visibility.Collapsed;
        ErrorMessage.Visibility = Visibility.Collapsed;
        Toolbar.WordWrapVisibility = Visibility.Collapsed;
        Toolbar.RotationButtonsVisibility = Visibility.Collapsed;
        Toolbar.SidebarButtonVisibility = Visibility.Collapsed;
        Toolbar.FitWidthButtonVisibility = Visibility.Collapsed;
        Toolbar.FitPageButtonVisibility = Visibility.Collapsed;
        SidebarCard.Visibility = Visibility.Collapsed;
        SidebarColumn.Width = new GridLength(0);
        ViewModel.SidebarOpen = false;
    }

    internal void ShowImageViewer()
    {
        _activeViewer = ImageViewerControl;
        ImageViewerHost.Visibility = Visibility.Visible;
        PdfViewerHost.Visibility = Visibility.Collapsed;
        DocxViewerHost.Visibility = Visibility.Collapsed;
        PptxViewerHost.Visibility = Visibility.Collapsed;
        TextViewerHost.Visibility = Visibility.Collapsed;
        MarkdownViewerHost.Visibility = Visibility.Collapsed;
        SpreadsheetViewerHost.Visibility = Visibility.Collapsed;
        UnsupportedFileMessage.Visibility = Visibility.Collapsed;
        ErrorMessage.Visibility = Visibility.Collapsed;
        Toolbar.WordWrapVisibility = Visibility.Collapsed;
        Toolbar.RotationButtonsVisibility = Visibility.Collapsed;
        Toolbar.SidebarButtonVisibility = Visibility.Collapsed;
        Toolbar.FitWidthButtonVisibility = Visibility.Visible;
        Toolbar.FitPageButtonVisibility = Visibility.Visible;
        SidebarCard.Visibility = Visibility.Collapsed;
        SidebarColumn.Width = new GridLength(0);
        ViewModel.SidebarOpen = false;
    }

    private void WordWrapButton_Click(object? sender, EventArgs e)
    {
        TextViewerControl.TextControl.TextWrapping = Toolbar.IsWordWrapChecked
            ? TextWrapping.Wrap
            : TextWrapping.NoWrap;
    }

    private void ShowInfoBar(string message, InfoBarSeverity severity) =>
        Logger.Log($"[InfoBar] {severity}: {message}");

    // ── Public API (used by TitleBarWindow) ───────────────────────────────────
    public int           CurrentPage     => _page;
    public int           TotalPages      => _total;
    public double        CurrentZoom     => _zoom;
    public DocumentItem? CurrentDocument => _doc;

    public void NavigateToPage(int n)  => GoToPage(n);
    public void ZoomIn()               => SetZoom(_zoom * 1.25);
    public void ZoomOut()              => SetZoom(_zoom / 1.25);
    public void FitWidth()             => Toolbar_FitWidthRequested(this, EventArgs.Empty);
    public void RotateClockwise()      => Rotate(+90);
    public void ShowSearch()           => Toolbar_FindRequested(this, EventArgs.Empty);
}
