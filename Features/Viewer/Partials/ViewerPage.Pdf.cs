using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using QuickView.Services.Documents;
using QuickView.UI.Controls;
using QuickView.Infrastructure.Logging;
using QuickView.Core.Models;
using QuickView.Rendering;
using QuickView.Rendering.Pdf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Features.Viewer;

public sealed partial class ViewerPage
{
    private float DisplayScale => (float)(XamlRoot?.RasterizationScale ?? 1.0);
    
    // Natural zoom is 1.0 now since we render at screen DPI × zoom dynamically
    private float NaturalZoom => 1.0f;




    // ── PdfViewerService (Phase 1 foundation) ─────────────────────────────
    private IPdfViewerService _pdfService = new PdfViewerService();

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
        // Correlated trace for this open attempt: every log entry inside can be tied
        // back via OperationId; duration and outcome are recorded on dispose.
        using var openOp = Logger.BeginOperation("OpenDocument", ("FilePath", doc.FilePath));
        Infrastructure.Diagnostics.DiagnosticsService.SetCurrentOperation(
            $"OpenDocument {System.IO.Path.GetExtension(doc.FilePath ?? string.Empty)}");

        _doc = doc;
        _cts?.Cancel();

        _searchCts?.Cancel();
        _spreadsheetSearchCts?.Cancel();
        _spreadsheetSearchGeneration++;
        ClearSpreadsheetSearch();
        SpreadsheetViewerControl.Cleanup();
        _spreadsheetDocument?.Dispose();
        _spreadsheetDocument = null;
        
        // Service owns document lifecycle — just clear our reference
        _pdfState = null;
        
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        // Clean up old viewport rendering
        CleanupViewportRendering();
        DocumentScrollViewer.ViewChanged -= OnPptxViewportChanged;
        DocumentScrollViewer.ViewChanged -= OnDocxViewportChanged;
        ClearPptxCache();
        ClearDocxCache();
        
        Thumbnails.Clear();
        ThumbnailList.ItemsSource = Thumbnails;
        _outline.Clear();
        OutlineTree.RootNodes.Clear();

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

        if (ext == ".pdf")
            await LoadPdfAsync(doc.FilePath ?? string.Empty, ct);
        else if (ext == ".docx" || ext == ".doc")
            await LoadDocxAsync(doc.FilePath ?? string.Empty, ct);
        else if (ext == ".pptx" || ext == ".ppt" || ext == ".pptm" || ext == ".ppsx")
            await LoadPptxAsync(doc.FilePath ?? string.Empty, ct);
        else if (ext == ".xlsx" || ext == ".xlsm" || ext == ".xltx" || ext == ".xltm")
            await LoadXlsxAsync(doc.FilePath ?? string.Empty, ct);
        else if (IsTextFile(ext))  // All text-based files now use the same lightweight viewer
            await LoadTextFileAsync(doc.FilePath ?? string.Empty, ct);
        else
        {
            // Try RendererRegistry (Office etc.) before falling back to info card
            System.Diagnostics.Debug.WriteLine($"[ViewerPage] Looking for renderer for extension: '{ext}'");
            var renderer = QuickView.Rendering.RendererRegistry.GetRenderer(ext);
            System.Diagnostics.Debug.WriteLine($"[ViewerPage] Renderer found: {renderer?.GetType().Name ?? "null"}");
            
            if (renderer != null)
                await LoadViaRendererAsync(renderer, doc.FilePath ?? string.Empty, ct);
            else
                ShowInfoCard(doc);
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
            
            // Clear any PPTX slides from canvas
            ClearPptxCache();
            
            Infrastructure.Logging.Logger.Log("[PDF] Calling PdfViewerService.LoadDocumentAsync");
            
            // Delegate PDF loading to service (Phase 2: LoadDocument extraction)
            var result = await _pdfService.LoadDocumentAsync(
                filePath,
                ShowPasswordDialogAsync, // UI callback for password prompt
                ct);

            Infrastructure.Logging.Logger.Log($"[PDF] LoadDocumentAsync result: Success={result.Success}, Error={result.ErrorMessage}");

            if (!result.Success)
            {
                ShowErrorCard(result.ErrorMessage ?? "Cannot open PDF");
                return;
            }

            // Service owns the document lifecycle — ViewerPage just holds a reference
            // Do NOT dispose here (service handles it in CloseDocument)
            _pdfState = result.Document;
            _total = result.PageCount;
            
            if (_pdfState == null)
            {
                Infrastructure.Logging.Logger.Log("[PDF] ERROR: _pdfState is null after successful load");
                ShowErrorCard("Failed to load PDF document");
                return;
            }

            Infrastructure.Logging.Logger.Log($"[PDF] Document loaded: {_total} pages");

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
            
            // Create thumbnail placeholders
            const double thumbLogicalW = 200.0;
            for (int i = 0; i < _total; i++)
            {
                var (ptW, ptH) = _pdfState.GetPageSize(i);
                double aspect = ptH / Math.Max(1.0, ptW);
                
                Thumbnails.Add(new PageThumbnailItem
                {
                    PageNumber = i + 1,
                    PageLabel  = $"{i + 1}",
                    BoxHeight  = thumbLogicalW * aspect,
                    IsSelected = i == 0,
                });
            }

            Infrastructure.Logging.Logger.Log("[PDF] Initializing viewport rendering");
            
            // VIEWPORT RENDERING: Initialize viewport system
            InitializeViewportRendering();
            
            Infrastructure.Logging.Logger.Log("[PDF] Hiding loading indicator");
            
            ShowLoading(false);
            DocumentScrollViewer.UpdateLayout();

            Infrastructure.Logging.Logger.Log("[PDF] Starting thumbnail prefetch");
            
            PrefetchThumbnailsAroundAsync(0, _pdfState, ct).SafeFireAndForget("PdfThumbnailPrefetch");
            
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

    // ── F5: Password dialog ───────────────────────────────────────────────────

    private async Task<string?> ShowPasswordDialogAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Password Required",
            Content = CreatePasswordDialogContent(out var passwordBox),
            PrimaryButtonText = "Open",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? passwordBox.Password : null;
    }

    private StackPanel CreatePasswordDialogContent(out PasswordBox passwordBox)
    {
        var stack = new StackPanel { Spacing = 12 };
        
        stack.Children.Add(new TextBlock
        {
            Text = "This PDF is password protected. Enter the password to open it:",
            TextWrapping = TextWrapping.Wrap
        });

        passwordBox = new PasswordBox
        {
            PlaceholderText = "Enter password",
            Width = 300
        };
        stack.Children.Add(passwordBox);

        return stack;
    }

    // ── Thumbnail Rendering ───────────────────────────────────────────────────

    private async Task PrefetchThumbnailsAroundAsync(
        int centerPage, PdfDocumentState pdfState, CancellationToken ct)
    {
        if (SidebarCard.Visibility != Visibility.Visible) return;

        int first = Math.Max(0, centerPage - 8);
        int last  = Math.Min(pdfState.PageCount - 1, centerPage + 8);

        var queue = new List<int> { centerPage };
        for (int offset = 1; offset <= 8; offset++)
        {
            if (centerPage + offset <= last) queue.Add(centerPage + offset);
            if (centerPage - offset >= first) queue.Add(centerPage - offset);
        }

        foreach (int pageIndex in queue)
        {
            ct.ThrowIfCancellationRequested();
            await RenderThumbnailAsync(pdfState, pageIndex, ct);
        }
    }

    /// <summary>Renders a single thumbnail via the service</summary>
    private async Task RenderThumbnailAsync(PdfDocumentState pdfState, int pageIdx, CancellationToken ct)
    {
        var bmp = await _pdfService.RenderThumbnailAsync(pageIdx, ct);
        if (bmp == null || ct.IsCancellationRequested) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(_pdfState, pdfState)) return;
            if (pageIdx < Thumbnails.Count)
                Thumbnails[pageIdx].Thumbnail = bmp;
        });
    }

    // ── Non-PDF Viewers ───────────────────────────────────────────────────────

    private void ShowInfoCard(DocumentItem doc)
    {
        _total = 1;
        Toolbar.UpdatePageInfo(1, 1);

        // Populate the unsupported file message UI (defined in XAML)
        UnsupportedIcon.Glyph = doc.FileGlyph;
        UnsupportedIcon.Foreground = doc.TypeColor;
        UnsupportedFileName.Text = doc.Title;
        UnsupportedMetadata.Text = $"{doc.FileType?.ToUpperInvariant()}  ·  {doc.FileSize}  ·  {doc.LastWriteTime:MMM dd, yyyy}";
        UnsupportedPath.Text = doc.FilePath;
        UnsupportedFileMessage.Visibility = Visibility.Visible;
        
        // Hide other viewers and sidebar
        DocumentScrollViewer.Visibility = Visibility.Collapsed;
        PlainTextViewerGrid.Visibility = Visibility.Collapsed;
        TextViewerScroll.Visibility = Visibility.Collapsed;
        MarkdownViewerHost.Visibility = Visibility.Collapsed;
        ErrorMessage.Visibility = Visibility.Collapsed;
        
        SidebarCard.Visibility = Visibility.Collapsed;
        SidebarColumn.Width = new GridLength(0);
        Toolbar.SidebarButtonVisibility = Visibility.Collapsed;
    }

    private async Task LoadViaRendererAsync(QuickView.Core.Interfaces.IFileRenderer renderer,
                                           string filePath, CancellationToken ct)
    {
        try
        {
            // Clean up previous renderer
            if (_currentRenderer != null && _currentRenderedElement != null)
            {
                try
                {
                    await _currentRenderer.CleanupAsync(_currentRenderedElement);
                }
                catch
                {
                    // Swallow cleanup exceptions
                }
                _currentRenderer = null;
                _currentRenderedElement = null;
            }

            ShowLoading(true);
            var element = await renderer.RenderAsync(filePath, ct);
            ShowLoading(false);

            if (ct.IsCancellationRequested || element == null)
                return;

            // If renderer returned a ViewerInfoCard, show our centered unsupported message instead
            if (element is ViewerInfoCard)
            {
                var doc = DocumentItem.FromFileInfo(new FileInfo(filePath));
                ShowInfoCard(doc);
                return;
            }

            // Track for cleanup later
            _currentRenderer = renderer;
            _currentRenderedElement = element;

            // Hide non-PDF viewers
            PlainTextViewerGrid.Visibility  = Visibility.Collapsed;
            TextViewerScroll.Visibility     = Visibility.Collapsed;
            MarkdownViewerHost.Visibility   = Visibility.Collapsed;
            DocumentScrollViewer.Visibility = Visibility.Visible;
            UnsupportedFileMessage.Visibility = Visibility.Collapsed;
            ErrorMessage.Visibility = Visibility.Collapsed;
            
            // Place the rendered element into Canvas
            var canvasForRenderer = new Canvas { Width = 800, Height = 1000 };
            canvasForRenderer.Children.Add(element);
            PdfViewportCanvas.Children.Clear();
            PdfViewportCanvas.Children.Add(canvasForRenderer);
            
            // Hide toolbar buttons not applicable
            ZoomPill.Visibility        = Visibility.Collapsed;
            Toolbar.WordWrapVisibility = Visibility.Collapsed;
            Toolbar.SidebarButtonVisibility = Visibility.Collapsed;
            SidebarCard.Visibility     = Visibility.Collapsed;
            SidebarColumn.Width        = new GridLength(0);
            Toolbar.FitWidthButtonVisibility = Visibility.Collapsed;
            Toolbar.FitPageButtonVisibility = Visibility.Collapsed;
            
            _total = 1;
            Toolbar.UpdatePageInfo(1, 1);
        }
        catch (System.Exception ex)
        {
            ShowLoading(false);
            QuickView.Infrastructure.Logging.Logger.Error($"Renderer failed for {filePath}", ex);
            ShowErrorCard($"Failed to load file: {ex.Message}");
        }
    }

    private void ShowErrorCard(string message)
    {
        ViewModel.ErrorMessage = message;
        
        // Populate the error message UI (defined in XAML)
        ErrorMessageText.Text = message;
        ErrorMessage.Visibility = Visibility.Visible;
        
        // Hide other viewers and sidebar
        DocumentScrollViewer.Visibility = Visibility.Collapsed;
        PlainTextViewerGrid.Visibility = Visibility.Collapsed;
        TextViewerScroll.Visibility = Visibility.Collapsed;
        MarkdownViewerHost.Visibility = Visibility.Collapsed;
        UnsupportedFileMessage.Visibility = Visibility.Collapsed;
        
        SidebarCard.Visibility = Visibility.Collapsed;
        SidebarColumn.Width = new GridLength(0);
        Toolbar.SidebarButtonVisibility = Visibility.Collapsed;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PPTX SUPPORT — Treat slides like PDF pages
    // ══════════════════════════════════════════════════════════════════════════
    
    private QuickView.Rendering.Presentation.PptxDocument? _pptxDocument;
    private QuickView.Rendering.Document.DocxDocument? _docxDocument;
    
    private async Task LoadPptxAsync(string filePath, CancellationToken ct)
    {
        ShowLoading(true);
        try
        {
            _pptxSearchResults = Array.Empty<QuickView.Rendering.Presentation.PptxSearchMatch>();
            _activePptxSearchIndex = -1;
            FindCount.Text = "";

            // Clear PDF viewport image
            if (ViewportImage != null)
                ViewportImage.Source = null;

            // PPTX zoom is handled by the ScrollViewer itself (slides are
            // pre-rendered and scaled) — the PDF path pins it to 1.0.
            DocumentScrollViewer.ZoomMode = ZoomMode.Enabled;

            // Clear any existing PPTX slides
            ClearPptxCache();
            
            if (!QuickView.Rendering.Presentation.DeckCraftNative.IsAvailable)
            {
                ShowErrorCard("PPTX renderer not available");
                return;
            }

            _pptxDocument?.Dispose();
            _pptxDocument = await QuickView.Rendering.Presentation.PptxDocument.OpenAsync(filePath, ct);
            
            if (_pptxDocument == null)
            {
                ShowErrorCard("Cannot open presentation");
                return;
            }

            // Treat PPTX like PDF: each slide = 1 page
            _total = _pptxDocument.SlideCount;
            _page = 1;
            Toolbar.UpdatePageInfo(_page, _total);
            ViewModel.TotalPages = _total;
            ViewModel.CurrentPage = _page;
            
            // Show sidebar with thumbnails
            Toolbar.SidebarIsChecked = true;
            SidebarCard.Visibility = Visibility.Visible;
            SidebarColumn.Width = new Microsoft.UI.Xaml.GridLength(240);
            ViewModel.SidebarOpen = true;
            
            // Create thumbnail placeholders
            Thumbnails.Clear();
            for (int i = 0; i < _total; i++)
            {
                Thumbnails.Add(new PageThumbnailItem
                {
                    PageNumber = i + 1,
                    PageLabel = $"Slide {i + 1}",
                    IsSelected = i == 0,
                });
            }
            
            // Apply 100% zoom initially
            _zoom = NaturalZoom;
            ZoomLabel.Text = "100%";
            ViewModel.NaturalZoom = NaturalZoom;
            ViewModel.Zoom = _zoom;
            
            // Wire viewport change handler for zoom/resize
            DocumentScrollViewer.ViewChanged -= OnPptxViewportChanged;
            DocumentScrollViewer.ViewChanged += OnPptxViewportChanged;
            
            // Size canvas to fit all slides vertically
            UpdatePptxCanvasSize();
            
            // Force layout update before rendering
            DocumentScrollViewer.UpdateLayout();
            
            // Start PPTX in fit-width mode and keep the matching toolbar toggle active.
            SetFitMode(FitMode.FitWidth);
            ApplyFitWidth();
            
            // Wait for zoom animation to complete before rendering
            await Task.Delay(100);
            
            // Force another layout update after zoom
            DocumentScrollViewer.UpdateLayout();
            
            // Trigger initial viewport render
            _pptxRenderCts?.Cancel();
            // Deliberately not disposing: in-flight render tasks still hold tokens
            // from this CTS; disposing would make their WaitAsync/Register throw.
            _pptxRenderCts = new CancellationTokenSource();
            await RenderPptxViewportAsync(_pptxRenderCts.Token);
            
            Logger.Log("PPTX loaded, initial render complete");
            LoadSlideThumbnailsAsync().SafeFireAndForget("PptxThumbnails");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Error($"LoadPptxAsync: {filePath}", ex);
            ShowErrorCard("Cannot open presentation");
        }
        finally { ShowLoading(false); }
    }
    
    private void UpdatePptxCanvasSize()
    {
        if (_pptxDocument == null) return;
        
        const double PageGapLp = 12.0;
        const double PtToLp = 96.0 / 72.0;
        const double VerticalPadding = 20.0;   // Padding on top/bottom only
        
        double maxWidthLp = 0;
        double totalHeightLp = VerticalPadding; // Start with top padding
        
        // Get first slide size (assume all slides same size for now)
        var (ptW, ptH) = _pptxDocument.GetSlideSize(0);
        maxWidthLp = ptW * PtToLp; // No horizontal padding in canvas size - zoom will handle fitting
        
        // Stack all slides vertically with gaps
        for (int i = 0; i < _pptxDocument.SlideCount; i++)
        {
            totalHeightLp += ptH * PtToLp;
            if (i < _pptxDocument.SlideCount - 1)
                totalHeightLp += PageGapLp;
        }
        
        totalHeightLp += VerticalPadding; // Add bottom padding
        
        // Set canvas size (will be scaled by zoom via ScrollViewer)
        PdfViewportCanvas.Width = maxWidthLp;
        PdfViewportCanvas.Height = totalHeightLp;
        
        Logger.Log($"Canvas sized: {maxWidthLp} x {totalHeightLp} lp");
    }
    
    // ── PPTX Viewport Rendering (memory-efficient, only render visible slides) ────
    
    private Dictionary<int, Microsoft.UI.Xaml.Controls.Image> _pptxSlideCache = new();
    private readonly Dictionary<int, double> _pptxSlideScale = new();
    private readonly List<Border> _pptxSearchHighlights = new();
    private DispatcherTimer? _pptxRenderDebounceTimer;
    private CancellationTokenSource? _pptxRenderCts;
    
    private async Task RenderPptxViewportAsync(CancellationToken ct = default)
    {
        if (_pptxDocument == null)
        {
            Logger.Log("RenderPptxViewportAsync: _pptxDocument is null");
            return;
        }
        
        try
        {
            const double PageGapLp = 12.0;
            const double PtToLp = 96.0 / 72.0;
            const double VerticalPadding = 20.0;   // Top/bottom padding only
            
            // Get slide dimensions
            var (widthPt, heightPt) = _pptxDocument.GetSlideSize(0);
            double slideHeightLp = heightPt * PtToLp;
            double slideWidthLp = widthPt * PtToLp;
            
            Logger.Log($"Slide dimensions: {widthPt}pt × {heightPt}pt = {slideWidthLp}lp × {slideHeightLp}lp");
            
            // Ensure viewport is ready
            if (DocumentScrollViewer.ViewportHeight <= 0 || DocumentScrollViewer.ViewportWidth <= 0)
            {
                Logger.Log($"❌ Viewport not ready: width={DocumentScrollViewer.ViewportWidth}, height={DocumentScrollViewer.ViewportHeight}");
                return;
            }
            
            // Canvas is in un-zoomed layout pixels; ScrollViewer offsets are zoomed.
            double zoom = Math.Max(0.01, DocumentScrollViewer.ZoomFactor);
            double scrollY = DocumentScrollViewer.VerticalOffset / zoom;
            double viewportHeight = DocumentScrollViewer.ViewportHeight / zoom;
            
            Logger.Log($"Viewport: width={DocumentScrollViewer.ViewportWidth}, height={viewportHeight}, scrollY={scrollY}");
            
            // Keep one nearby slide rendered ahead/behind
            const double bufferSlides = 1;
            double visibleStart = scrollY - (bufferSlides * (slideHeightLp + PageGapLp));
            double visibleEnd = scrollY + viewportHeight + (bufferSlides * (slideHeightLp + PageGapLp));
            
            // Calculate slide indices to render
            int firstSlide = Math.Max(0, (int)((visibleStart - VerticalPadding) / (slideHeightLp + PageGapLp)));
            int lastSlide = Math.Min(_pptxDocument.SlideCount - 1, (int)((visibleEnd - VerticalPadding) / (slideHeightLp + PageGapLp)) + 1);
            
            Logger.Log($"Rendering PPTX viewport: slides {firstSlide}-{lastSlide} (scroll={scrollY})");
            
            // Calculate target scale for 1:1 physical pixel rendering
            double targetScale = RenderBudget.OfficeScale(_zoom, DisplayScale, widthPt, heightPt);
            
            Logger.Log($"Render scale: {targetScale:F2} (zoom={_zoom:F2}, display={DisplayScale:F2})");
            
            // Render visible slides
            for (int i = firstSlide; i <= lastSlide; i++)
            {
                if (ct.IsCancellationRequested) return;

                // Check if we need to render or re-render this slide
                bool needsRender = !_pptxSlideCache.ContainsKey(i) ||
                                  !_pptxSlideScale.TryGetValue(i, out double cachedScale) ||
                                  RenderBudget.NeedsRerenderScale(cachedScale, targetScale);

                if (needsRender)
                {
                    Logger.Log($"⏳ Rendering slide {i} at scale {targetScale:F2}...");
                    
                    WriteableBitmap? bitmap = await _pptxDocument.RenderSlideAsync(i, scale: targetScale, ct);
                    if (bitmap == null || ct.IsCancellationRequested)
                    {
                        Logger.Log($"❌ Slide {i} render returned null bitmap or was cancelled");
                        continue;
                    }
                    
                    Logger.Log($"✅ Slide {i} bitmap: {bitmap.PixelWidth}×{bitmap.PixelHeight} pixels");
                    
                    // Calculate Y position for this slide (include top padding)
                    double slideY = VerticalPadding + (i * (slideHeightLp + PageGapLp));
                    
                    // Image size must match bitmap pixels / rasterization scale for 1:1 rendering
                    double imageWidth = bitmap.PixelWidth / DisplayScale;
                    double imageHeight = bitmap.PixelHeight / DisplayScale;
                    
                    // Create image control
                    var image = new Microsoft.UI.Xaml.Controls.Image
                    {
                        Source = bitmap,
                        Width = imageWidth,
                        Height = imageHeight,
                        Stretch = Microsoft.UI.Xaml.Media.Stretch.None,  // No resampling!
                        UseLayoutRounding = true,
                    };
                    
                    // Center horizontally (Canvas.Left = 0, ScrollViewer centers via zoom)
                    Microsoft.UI.Xaml.Controls.Canvas.SetLeft(image, 0);
                    Microsoft.UI.Xaml.Controls.Canvas.SetTop(image, slideY);
                    
                    // Remove old version if exists
                    if (_pptxSlideCache.TryGetValue(i, out var oldImage))
                    {
                        PdfViewportCanvas.Children.Remove(oldImage);
                        if (oldImage.Source is WriteableBitmap oldBmp)
                            oldImage.Source = null;
                    }
                    
                    PdfViewportCanvas.Children.Add(image);
                    _pptxSlideCache[i] = image;
                    _pptxSlideScale[i] = targetScale;
                    
                    Logger.Log($"✅ Slide {i} added to canvas at Y={slideY:F0}, size={slideWidthLp:F0}×{slideHeightLp:F0} DIP");
                }
            }
            
            // Evict slides outside buffer zone
            var toRemove = _pptxSlideCache.Keys.Where(idx =>
                idx < firstSlide - 1 || idx > lastSlide + 1).ToList();
            
            foreach (int idx in toRemove)
            {
                if (_pptxSlideCache.TryGetValue(idx, out var img))
                {
                    PdfViewportCanvas.Children.Remove(img);
                    
                    if (img.Source is WriteableBitmap bmp)
                        img.Source = null;
                    
                    _pptxSlideCache.Remove(idx);
                    _pptxSlideScale.Remove(idx);
                }
            }
            
            Logger.Log($"PPTX cache size: {_pptxSlideCache.Count} slides");
            DrawPptxSearchHighlights();
        }
        catch (OperationCanceledException)
        {
            // Expected when zoom/scroll happens during render
        }
        catch (Exception ex)
        {
            Logger.Error($"RenderPptxViewportAsync error", ex);
        }
    }
    
    private async Task RenderCurrentSlideAsync(CancellationToken ct)
    {
        // Legacy method - now using viewport rendering
        await RenderPptxViewportAsync(ct);
    }
    
    private void OnPptxViewportChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_pptxDocument == null) return;

        // Update zoom display
        float currentZoom = DocumentScrollViewer.ZoomFactor;
        if (Math.Abs(currentZoom - _zoom) > 0.001)
        {
            _zoom = currentZoom;
            double displayPct = Math.Round(_zoom / NaturalZoom * 100.0);
            ZoomLabel.Text = $"{displayPct}%";
            SyncToViewModel();
        }
        
        // Cancel any pending render
        _pptxRenderDebounceTimer?.Stop();
        _pptxRenderCts?.Cancel();
        
        // Render visible slides when scroll/zoom settles
        if (!e.IsIntermediate)
        {
            UpdateCurrentPptxPage();
            
            // Debounce: wait 200ms after view settles before re-rendering
            _pptxRenderDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _pptxRenderDebounceTimer.Tick -= OnPptxRenderDebounceTimerTick;
            _pptxRenderDebounceTimer.Tick += OnPptxRenderDebounceTimerTick;
            _pptxRenderDebounceTimer.Start();
        }
    }

    private void OnPptxRenderDebounceTimerTick(object? sender, object e)
    {
        _pptxRenderDebounceTimer?.Stop();

        // Cancel any in-flight render (not disposing — tokens may still be in flight)
        _pptxRenderCts?.Cancel();
        _pptxRenderCts = new CancellationTokenSource();

        // Fire and forget - don't await in event handler to avoid deadlocks
        RenderPptxViewportAsync(_pptxRenderCts.Token).SafeFireAndForget("PptxViewportRender");
    }
    
    private void UpdateCurrentPptxPage()
    {
        if (_pptxDocument == null || _pptxDocument.SlideCount == 0) return;

        const double PtToLp = 96.0 / 72.0;
        const double PageGapLp = 12.0;
        const double VerticalPaddingLp = 20.0;
        var (_, slideHeightPt) = _pptxDocument.GetSlideSize(0);
        double slideStrideLp = slideHeightPt * PtToLp + PageGapLp;
        double zoom = Math.Max(0.1, DocumentScrollViewer.ZoomFactor);
        double viewportCenterLp =
            (DocumentScrollViewer.VerticalOffset + DocumentScrollViewer.ViewportHeight / 2.0) / zoom;
        int slideIndex = (int)Math.Floor((viewportCenterLp - VerticalPaddingLp) / slideStrideLp);
        int currentPage = Math.Clamp(slideIndex + 1, 1, _pptxDocument.SlideCount);

        if (currentPage != _page)
            UpdateCurrentPage(currentPage);
    }

    private void ClearPptxCache()
    {
        _pptxRenderDebounceTimer?.Stop();
        _pptxRenderCts?.Cancel();
        _pptxRenderCts = null;

        ClearPptxSearchHighlights();

        if (_pptxSlideCache.Count == 0)
        {
            Logger.Log("ClearPptxCache: cache already empty");
            return;
        }
        
        Logger.Log($"Clearing {_pptxSlideCache.Count} PPTX slides from canvas (canvas has {PdfViewportCanvas.Children.Count} total children)");
        
        // Remove only PPTX slide images
        foreach (var img in _pptxSlideCache.Values)
        {
            PdfViewportCanvas.Children.Remove(img);
            if (img.Source is WriteableBitmap bmp)
            {
                img.Source = null;
            }
        }
        _pptxSlideCache.Clear();
        _pptxSlideScale.Clear();
        
        Logger.Log($"After clear: canvas has {PdfViewportCanvas.Children.Count} children (should be 2: ViewportImage + SearchHighlightCanvas)");
    }

    private void ClearPptxSearchHighlights()
    {
        foreach (var highlight in _pptxSearchHighlights)
            PdfViewportCanvas.Children.Remove(highlight);
        _pptxSearchHighlights.Clear();
    }

    private void DrawPptxSearchHighlights()
    {
        ClearPptxSearchHighlights();
        if (_pptxDocument == null || _pptxSearchResults.Count == 0 || _pptxSlideCache.Count == 0)
            return;

        const double PtToLp = 96.0 / 72.0;
        const double PageGapLp = 12.0;
        const double VerticalPaddingLp = 20.0;
        var (_, slideHeightPt) = _pptxDocument.GetSlideSize(0);
        double slideStrideLp = slideHeightPt * PtToLp + PageGapLp;

        for (int matchIndex = 0; matchIndex < _pptxSearchResults.Count; matchIndex++)
        {
            var match = _pptxSearchResults[matchIndex];
            if (!_pptxSlideCache.ContainsKey(match.SlideIndex))
                continue;

            bool active = matchIndex == _activePptxSearchIndex;
            foreach (var rect in match.Rects)
            {
                var highlight = new Border
                {
                    Width = Math.Max(2, rect.Width * PtToLp),
                    Height = Math.Max(2, rect.Height * PtToLp),
                    CornerRadius = new CornerRadius(2),
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        active
                            ? Windows.UI.Color.FromArgb(145, 255, 105, 97)
                            : Windows.UI.Color.FromArgb(105, 255, 224, 102)),
                    BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        active
                            ? Windows.UI.Color.FromArgb(190, 210, 70, 65)
                            : Windows.UI.Color.FromArgb(145, 205, 160, 40)),
                    BorderThickness = new Thickness(active ? 1 : 0.5),
                    IsHitTestVisible = false,
                };

                Microsoft.UI.Xaml.Controls.Canvas.SetLeft(highlight, rect.X * PtToLp);
                Microsoft.UI.Xaml.Controls.Canvas.SetTop(
                    highlight,
                    VerticalPaddingLp + match.SlideIndex * slideStrideLp + rect.Y * PtToLp);
                Microsoft.UI.Xaml.Controls.Canvas.SetZIndex(highlight, 10);
                PdfViewportCanvas.Children.Add(highlight);
                _pptxSearchHighlights.Add(highlight);
            }
        }
    }
    
    private async Task LoadSlideThumbnailsAsync()
    {
        // Capture the document so a file switch mid-loop cannot touch the new one,
        // and bind to the page token so closing the tab stops the loop early.
        var document = _pptxDocument;
        var ct = _cts?.Token ?? default;
        if (document == null) return;

        for (int i = 0; i < document.SlideCount && i < 50; i++)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                // Render thumbnail at low resolution (0.3x for better quality than 0.2x)
                var thumb = await document.RenderSlideAsync(i, scale: RenderBudget.ThumbnailOfficeScale, ct);
                if (thumb == null || ct.IsCancellationRequested) return;

                // Only publish if this document is still the one being displayed
                if (!ReferenceEquals(_pptxDocument, document) || i >= Thumbnails.Count) return;
                Thumbnails[i].Thumbnail = thumb;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Logger.Error($"LoadSlideThumbnails: slide {i}", ex);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SPREADSHEET (GridCraft XLSX — native Win2D grid viewer)
    // ══════════════════════════════════════════════════════════════════════════

    private QuickView.Rendering.Document.SpreadsheetDocument? _spreadsheetDocument;

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
            // Clean up any previous state (PPTX slides, PDF doc, etc.)
            ClearPptxCache();
            _pdfState = null;
            Thumbnails.Clear();
            _spreadsheetSearchCts?.Cancel();
            _spreadsheetMatches.Clear();
            _activeSpreadsheetIndex = -1;
            SpreadsheetViewerControl.ClearSearchMatches();

            if (!QuickView.Rendering.Document.GridCraftNative.IsAvailable)
            {
                ShowErrorCard("GridCraft XLSX renderer not available.\nPlease ensure gridcraft_ffi.dll is present.");
                return;
            }

            _spreadsheetDocument?.Dispose();
            _spreadsheetDocument = await QuickView.Rendering.Document.SpreadsheetDocument.OpenAsync(filePath, ct);

            if (_spreadsheetDocument == null)
            {
                ShowErrorCard("Cannot open spreadsheet — file may be corrupt or in an unsupported format.");
                return;
            }

            // Switch the viewer area to the native spreadsheet grid
            ShowSpreadsheetViewer();

            // Toolbar: show page info as sheet count
            int sheetCount = _spreadsheetDocument.SheetCount;
            _total = sheetCount;
            _page  = 1;
            Toolbar.UpdatePageInfo(1, sheetCount);
            ViewModel.TotalPages   = sheetCount;
            ViewModel.CurrentPage  = 1;

            // Hand off to the Win2D cell-grid control
            await SpreadsheetViewerControl.LoadDocumentAsync(_spreadsheetDocument, ct);
            _zoom = SpreadsheetViewerControl.Zoom;
            SetFitMode(FitMode.FitWidth);
            ApplyFitWidth();
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

    private readonly Dictionary<int, Image> _docxPageCache = new();
    private double[] _docxPageTopLp = Array.Empty<double>();
    private double[] _docxPageWidthLp = Array.Empty<double>();
    private double[] _docxPageHeightLp = Array.Empty<double>();

    private async Task LoadDocxAsync(string filePath, CancellationToken ct)
    {
        ShowLoading(true);
        try
        {
            // Clear the PDF viewport image
            if (ViewportImage != null)
                ViewportImage.Source = null;

            // DOCX uses zoom-enabled scroll viewer like PPTX
            DocumentScrollViewer.ZoomMode = ZoomMode.Enabled;
            DocumentScrollViewer.ViewChanged -= OnPptxViewportChanged;
            _pptxDocument?.Dispose();
            _pptxDocument = null;
            ClearPptxCache();

            if (!QuickView.Rendering.Document.WordCraftNative.IsAvailable)
            {
                ShowErrorCard("WordCraft DOCX renderer not available.\nPlease ensure wordcraft_ffi.dll is present.");
                return;
            }

            DocumentScrollViewer.ViewChanged -= OnDocxViewportChanged;
            ClearDocxCache();
            _docxDocument?.Dispose();
            _docxDocument = await QuickView.Rendering.Document.DocxDocument.OpenAsync(filePath, ct);

            if (_docxDocument == null)
            {
                ShowErrorCard("Cannot open document — file may be corrupt or encrypted.");
                return;
            }

            // Each DOCX page is a page
            _total = _docxDocument.PageCount;
            _page = 1;
            _docxSearchResults = Array.Empty<QuickView.Rendering.Document.DocxDocument.DocxSearchMatch>();
            _activeDocxSearchIndex = -1;
            SearchHighlightCanvas.Children.Clear();
            Toolbar.UpdatePageInfo(_page, _total);
            ViewModel.TotalPages = _total;
            ViewModel.CurrentPage = _page;

            // Show sidebar with thumbnails
            Toolbar.SidebarIsChecked = true;
            SidebarCard.Visibility = Visibility.Visible;
            SidebarColumn.Width = new Microsoft.UI.Xaml.GridLength(240);
            ViewModel.SidebarOpen = true;

            // Create thumbnail placeholders
            Thumbnails.Clear();
            for (int i = 0; i < _total; i++)
            {
                Thumbnails.Add(new PageThumbnailItem
                {
                    PageNumber = i + 1,
                    PageLabel = $"{i + 1}",
                    IsSelected = i == 0,
                });
            }

            // Apply 100% zoom initially
            _zoom = NaturalZoom;
            ZoomLabel.Text = "100%";
            ViewModel.NaturalZoom = NaturalZoom;
            ViewModel.Zoom = _zoom;

            // Size canvas to fit all pages vertically
            UpdateDocxCanvasSize();

            // Force layout update
            DocumentScrollViewer.UpdateLayout();

            // Start DOCX in fit-width mode and keep the matching toolbar toggle active.
            SetFitMode(FitMode.FitWidth);
            ApplyFitWidth();

            // Wait for zoom animation
            await Task.Delay(100);
            DocumentScrollViewer.UpdateLayout();

            // Render visible pages
            _docxRenderCts?.Cancel();
            _docxRenderCts = new CancellationTokenSource();
            await RenderDocxViewportAsync(_docxRenderCts.Token);
            DocumentScrollViewer.ViewChanged += OnDocxViewportChanged;

            // Load thumbnails in background
            LoadDocxThumbnailsAsync().SafeFireAndForget("DocxThumbnails");
        }
        catch (OperationCanceledException) { }
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

    private DispatcherTimer? _docxRenderDebounceTimer;
    private CancellationTokenSource? _docxRenderCts;

    private void OnDocxViewportChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_docxDocument == null) return;

        float currentZoom = DocumentScrollViewer.ZoomFactor;
        if (Math.Abs(currentZoom - _zoom) > 0.001)
        {
            _zoom = currentZoom;
            double displayPct = Math.Round(_zoom / NaturalZoom * 100.0);
            ZoomLabel.Text = $"{displayPct}%";
            SyncToViewModel();
        }

        // Cancel any pending render
        _docxRenderDebounceTimer?.Stop();
        _docxRenderCts?.Cancel();

        if (!e.IsIntermediate)
        {
            UpdateCurrentPageFromDocxScroll();
            DrawDocxSearchHighlights();

            // Debounce: wait 200ms after view settles before re-rendering
            _docxRenderDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _docxRenderDebounceTimer.Tick -= OnDocxRenderDebounceTimerTick;
            _docxRenderDebounceTimer.Tick += OnDocxRenderDebounceTimerTick;
            _docxRenderDebounceTimer.Start();
        }
    }

    private void OnDocxRenderDebounceTimerTick(object? sender, object e)
    {
        _docxRenderDebounceTimer?.Stop();

        // Cancel any in-flight render (not disposing — tokens may still be in flight)
        _docxRenderCts?.Cancel();
        _docxRenderCts = new CancellationTokenSource();

        // Fire and forget - don't await in event handler to avoid deadlocks
        RenderDocxViewportAsync(_docxRenderCts.Token).SafeFireAndForget("DocxViewportRender");
    }

    private void UpdateDocxCanvasSize()
    {
        if (_docxDocument == null) return;

        const double PtToLp = 96.0 / 72.0;
        _docxPageTopLp = new double[_docxDocument.PageCount];
        _docxPageWidthLp = new double[_docxDocument.PageCount];
        _docxPageHeightLp = new double[_docxDocument.PageCount];
        double totalHeight = 0;
        double maxWidth = 0;

        for (int i = 0; i < _docxDocument.PageCount; i++)
        {
            var (w, h) = _docxDocument.GetPageSize(i);
            _docxPageWidthLp[i] = w * PtToLp;
            _docxPageHeightLp[i] = h * PtToLp;
            _docxPageTopLp[i] = totalHeight;
            totalHeight += _docxPageHeightLp[i];
            if (i < _docxDocument.PageCount - 1)
                totalHeight += 12;
            if (_docxPageWidthLp[i] > maxWidth) maxWidth = _docxPageWidthLp[i];
        }

        PdfViewportCanvas.Width = maxWidth;
        PdfViewportCanvas.Height = totalHeight;
        SearchHighlightCanvas.Width = maxWidth;
        SearchHighlightCanvas.Height = totalHeight;
        Canvas.SetZIndex(SearchHighlightCanvas, 10);
    }

    private double GetDocxPageTopLp(int pageIndex) =>
        pageIndex >= 0 && pageIndex < _docxPageTopLp.Length
            ? _docxPageTopLp[pageIndex]
            : 0;

    private (int firstPage, int lastPage) GetVisibleDocxPageRange()
    {
        if (_docxDocument == null || _docxPageTopLp.Length != _docxDocument.PageCount)
            return (-1, -1);

        double zoom = Math.Max(0.01, DocumentScrollViewer.ZoomFactor);
        double visibleTop = DocumentScrollViewer.VerticalOffset / zoom;
        double visibleBottom =
            (DocumentScrollViewer.VerticalOffset + DocumentScrollViewer.ViewportHeight) / zoom;
        int firstPage = -1;
        int lastPage = -1;

        for (int i = 0; i < _docxDocument.PageCount; i++)
        {
            double pageTop = _docxPageTopLp[i];
            double pageBottom = pageTop + _docxPageHeightLp[i];
            if (pageBottom >= visibleTop && pageTop <= visibleBottom)
            {
                if (firstPage < 0)
                    firstPage = i;
                lastPage = i;
            }
        }

        return (firstPage, lastPage);
    }

    private void UpdateCurrentPageFromDocxScroll()
    {
        if (_docxDocument == null || _docxPageTopLp.Length != _docxDocument.PageCount)
            return;

        double zoom = Math.Max(0.01, DocumentScrollViewer.ZoomFactor);
        double centerY =
            (DocumentScrollViewer.VerticalOffset + DocumentScrollViewer.ViewportHeight / 2) / zoom;
        double nearestDistance = double.MaxValue;
        int currentPage = 0;

        for (int i = 0; i < _docxDocument.PageCount; i++)
        {
            double pageTop = _docxPageTopLp[i];
            double pageHeight = _docxPageHeightLp[i];
            double pageBottom = pageTop + pageHeight;
            if (centerY >= pageTop && centerY <= pageBottom)
            {
                currentPage = i;
                break;
            }

            double distance = Math.Abs(centerY - (pageTop + pageHeight / 2));
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                currentPage = i;
            }
        }

        if (_page != currentPage + 1)
            UpdateCurrentPage(currentPage + 1);
    }

    private async Task RenderDocxViewportAsync(CancellationToken ct = default)
    {
        if (_docxDocument == null) return;

        try
        {
            const double PtToLp = 96.0 / 72.0;
            var (firstPage, lastPage) = GetVisibleDocxPageRange();
            if (firstPage < 0) return;

            // Render visible pages + 1 buffer
            int renderStart = Math.Max(0, firstPage - 1);
            int renderEnd = Math.Min(_docxDocument.PageCount - 1, lastPage + 1);

            for (int i = 0; i < _docxDocument.PageCount; i++)
            {
                if (ct.IsCancellationRequested) return;

                double w = _docxPageWidthLp[i];
                double h = _docxPageHeightLp[i];
                double y = GetDocxPageTopLp(i);

                if (i >= renderStart && i <= renderEnd && !_docxPageCache.ContainsKey(i))
                {
                    // Render above the display's pixel density for crisp text,
                    // while capping resolution to keep page memory bounded.
                    double renderScale = Math.Clamp(
                        Math.Max(1.5, _zoom * DisplayScale * PtToLp * 1.25),
                        1.5,
                        6.0);
                    
                    var bmp = await _docxDocument.RenderPageAsync(i, renderScale, ct);
                    if (bmp == null) continue;

                    var image = new Image
                    {
                        Source = bmp,
                        Width = w,
                        Height = h,
                        Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill,
                    };

                    Microsoft.UI.Xaml.Controls.Canvas.SetLeft(image, 0);
                    Microsoft.UI.Xaml.Controls.Canvas.SetTop(image, y);
                    Canvas.SetZIndex(image, 0);

                    PdfViewportCanvas.Children.Add(image);
                    _docxPageCache[i] = image;
                }
            }

            // Remove pages far from viewport
            var toRemove = _docxPageCache.Keys.Where(idx =>
                idx < renderStart - 2 || idx > renderEnd + 2).ToList();

            foreach (int idx in toRemove)
            {
                if (_docxPageCache.TryGetValue(idx, out var img))
                {
                    PdfViewportCanvas.Children.Remove(img);
                    if (img.Source is WriteableBitmap bmp)
                        img.Source = null;
                    _docxPageCache.Remove(idx);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when zoom/scroll happens during render
        }
        catch (Exception ex)
        {
            Logger.Error($"RenderDocxViewportAsync error", ex);
        }
    }

    private void ClearDocxCache()
    {
        _docxRenderDebounceTimer?.Stop();
        _docxRenderCts?.Cancel();
        _docxRenderCts = null;

        foreach (var img in _docxPageCache.Values)
        {
            PdfViewportCanvas.Children.Remove(img);
            if (img.Source is WriteableBitmap bmp)
                img.Source = null;
        }
        _docxPageCache.Clear();
    }

    private async Task LoadDocxThumbnailsAsync()
    {
        // Capture the document so a file switch mid-loop cannot touch the new one,
        // and bind to the page token so closing the tab stops the loop early.
        var document = _docxDocument;
        var ct = _cts?.Token ?? default;
        if (document == null) return;

        for (int i = 0; i < document.PageCount && i < 50; i++)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                var thumb = await document.RenderPageAsync(i, scale: RenderBudget.ThumbnailOfficeScale, ct);
                if (thumb == null || ct.IsCancellationRequested) return;

                // Only publish if this document is still the one being displayed
                if (!ReferenceEquals(_docxDocument, document) || i >= Thumbnails.Count) return;
                Thumbnails[i].Thumbnail = thumb;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Logger.Error($"LoadDocxThumbnails: page {i}", ex);
            }
        }
    }
}
