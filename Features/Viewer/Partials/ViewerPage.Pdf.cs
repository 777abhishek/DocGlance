using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using QuickView.Services.Documents;
using QuickView.UI.Controls;
using QuickView.Infrastructure.Logging;
using QuickView.Core.Models;
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
        _doc = doc;
        _cts?.Cancel();

        _searchCts?.Cancel();
        
        // Service owns document lifecycle — just clear our reference
        _pdfState = null;
        
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        // Clean up old viewport rendering
        CleanupViewportRendering();
        
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

            Infrastructure.Logging.Logger.Log("[PDF] Loading outline");
            
            // Load outline from the open document
            _ = Task.Run(() => LoadOutline(_pdfState), ct);

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
                Toolbar.SidebarIsChecked = true;
                SidebarCard.Visibility   = Visibility.Visible;
                SidebarColumn.Width      = new Microsoft.UI.Xaml.GridLength(240);
                ViewModel.SidebarOpen    = true;
            }

            Infrastructure.Logging.Logger.Log("[PDF] Setting zoom");
            
            // Apply 100% zoom
            _zoom = NaturalZoom;
            DocumentScrollViewer.ChangeView(null, null, (float)NaturalZoom, disableAnimation: true);
            ZoomLabel.Text = "100%";
            ViewModel.NaturalZoom = NaturalZoom;
            ViewModel.Zoom        = _zoom;

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
            
            // Background thumbnail rendering
            _ = PrefetchThumbnailsAroundAsync(0, _pdfState, ct);
            
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

        int first = Math.Max(0, centerPage - 4);
        int last  = Math.Min(pdfState.PageCount - 1, centerPage + 4);

        var queue = new List<int> { centerPage };
        for (int offset = 1; offset <= 4; offset++)
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
    
    private async Task LoadPptxAsync(string filePath, CancellationToken ct)
    {
        ShowLoading(true);
        try
        {
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
            _lastPptxRenderZoom = _zoom;
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
            
            // Apply fit-width so slides fit in viewport by default
            ApplyFitWidth();
            
            // Wait for zoom animation to complete before rendering
            await Task.Delay(100);
            
            // Force another layout update after zoom
            DocumentScrollViewer.UpdateLayout();
            
            // Trigger initial viewport render
            await RenderPptxViewportAsync();
            
            Logger.Log("PPTX loaded, initial render complete");
            LoadSlideThumbnails();
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
    
    private async Task RenderPptxViewportAsync()
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
            
            // Calculate which slides are visible in viewport
            double scrollY = DocumentScrollViewer.VerticalOffset;
            double viewportHeight = DocumentScrollViewer.ViewportHeight;
            
            Logger.Log($"Viewport: width={DocumentScrollViewer.ViewportWidth}, height={viewportHeight}, scrollY={scrollY}");
            
            // Add buffer above/below for smooth scrolling
            double bufferSlides = 2;
            double visibleStart = scrollY - (bufferSlides * (slideHeightLp + PageGapLp));
            double visibleEnd = scrollY + viewportHeight + (bufferSlides * (slideHeightLp + PageGapLp));
            
            // Calculate slide indices to render
            int firstSlide = Math.Max(0, (int)((visibleStart - VerticalPadding) / (slideHeightLp + PageGapLp)));
            int lastSlide = Math.Min(_pptxDocument.SlideCount - 1, (int)((visibleEnd - VerticalPadding) / (slideHeightLp + PageGapLp)) + 1);
            
            Logger.Log($"Rendering PPTX viewport: slides {firstSlide}-{lastSlide} (scroll={scrollY})");
            
            // Adaptive render scale based on zoom level
            // Always render at least 1.5x for quality, scale up further when zoomed
            // At fit-width (~0.93x zoom): render at 1.5x (crisp when scaled down)
            // At 100% zoom: render at 1.5x (crisp at native size)
            // At 200% zoom: render at 2.5x (crisp when zoomed in)
            double baseScale = 1.5; // Minimum quality
            double zoomMultiplier = Math.Max(1.0, _zoom);
            double renderScale = baseScale * zoomMultiplier * DisplayScale;
            renderScale = Math.Min(3.0, renderScale); // Cap at 3x to avoid huge memory
            
            Logger.Log($"Render scale: {renderScale:F2} (zoom={_zoom:F2}, display={DisplayScale:F2})");
            
            // Render visible slides
            for (int i = firstSlide; i <= lastSlide; i++)
            {
                // Skip if already rendered
                if (_pptxSlideCache.ContainsKey(i))
                {
                    Logger.Log($"Slide {i} already in cache, skipping");
                    continue;
                }
                
                Logger.Log($"⏳ Rendering slide {i} at scale {renderScale:F2}...");
                
                WriteableBitmap? bitmap = await _pptxDocument.RenderSlideAsync(i, scale: renderScale, default);
                if (bitmap == null)
                {
                    Logger.Log($"❌ Slide {i} render returned null bitmap");
                    continue;
                }
                
                Logger.Log($"✅ Slide {i} bitmap: {bitmap.PixelWidth}×{bitmap.PixelHeight} pixels");
                
                // Calculate Y position for this slide (include top padding)
                double slideY = VerticalPadding + (i * (slideHeightLp + PageGapLp));
                
                // Create image control
                var image = new Microsoft.UI.Xaml.Controls.Image
                {
                    Source = bitmap,
                    Width = slideWidthLp,
                    Height = slideHeightLp,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill
                };
                
                // Center horizontally (Canvas.Left = 0, ScrollViewer centers via zoom)
                Microsoft.UI.Xaml.Controls.Canvas.SetLeft(image, 0);
                Microsoft.UI.Xaml.Controls.Canvas.SetTop(image, slideY);
                
                PdfViewportCanvas.Children.Add(image);
                _pptxSlideCache[i] = image;
                
                Logger.Log($"✅ Slide {i} added to canvas at Y={slideY:F0}, size={slideWidthLp:F0}×{slideHeightLp:F0} lp");
            }
            
            // Remove slides that are too far from viewport (free memory)
            // Aggressive cleanup: only keep visible slides + 3 buffer
            var toRemove = _pptxSlideCache.Keys.Where(idx => 
                idx < firstSlide - 3 || idx > lastSlide + 3).ToList();
            
            foreach (int idx in toRemove)
            {
                if (_pptxSlideCache.TryGetValue(idx, out var img))
                {
                    PdfViewportCanvas.Children.Remove(img);
                    
                    // Force bitmap cleanup
                    if (img.Source is WriteableBitmap bmp)
                    {
                        img.Source = null;
                    }
                    
                    _pptxSlideCache.Remove(idx);
                }
            }
            
            Logger.Log($"PPTX cache size: {_pptxSlideCache.Count} slides");
        }
        catch (Exception ex)
        {
            Logger.Error($"RenderPptxViewportAsync error", ex);
        }
    }
    
    private async Task RenderCurrentSlideAsync(CancellationToken ct)
    {
        // Legacy method - now using viewport rendering
        await RenderPptxViewportAsync();
    }
    
    private double _lastPptxRenderZoom = 1.0;
    
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
        
        // Check if we need to re-render at different resolution
        // Re-render if zoom changed by more than 25%
        bool needsRerender = Math.Abs(_zoom - _lastPptxRenderZoom) > (_lastPptxRenderZoom * 0.25);
        
        // Render visible slides when scroll/zoom settles
        if (!e.IsIntermediate)
        {
            if (needsRerender)
            {
                // Clear cache to force re-render at new resolution
                Logger.Log($"Zoom changed significantly ({_lastPptxRenderZoom:F2} -> {_zoom:F2}), clearing cache");
                ClearPptxCache();
                _lastPptxRenderZoom = _zoom;
            }
            
            _ = RenderPptxViewportAsync();
        }
    }
    
    private void ClearPptxCache()
    {
        if (_pptxSlideCache.Count == 0)
        {
            Logger.Log("ClearPptxCache: cache already empty");
            return;
        }
        
        Logger.Log($"Clearing {_pptxSlideCache.Count} PPTX slides from canvas (canvas has {PdfViewportCanvas.Children.Count} total children)");
        
        // Remove only PPTX slide images (don't remove ViewportImage or SearchHighlightCanvas!)
        foreach (var img in _pptxSlideCache.Values)
        {
            PdfViewportCanvas.Children.Remove(img);
            if (img.Source is WriteableBitmap bmp)
            {
                img.Source = null;
            }
        }
        _pptxSlideCache.Clear();
        
        Logger.Log($"After clear: canvas has {PdfViewportCanvas.Children.Count} children (should be 2: ViewportImage + SearchHighlightCanvas)");
    }
    
    private async void LoadSlideThumbnails()
    {
        if (_pptxDocument == null) return;
        
        // Render thumbnails sequentially on UI thread (WriteableBitmap requires it)
        for (int i = 0; i < _pptxDocument.SlideCount && i < 50; i++)
        {
            try
            {
                // Render thumbnail at low resolution (0.3x for better quality than 0.2x)
                var thumb = await _pptxDocument.RenderSlideAsync(i, scale: 0.3, default);
                if (thumb != null && i < Thumbnails.Count)
                {
                    Thumbnails[i].Thumbnail = thumb;
                }
            }
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
        try
        {
            // Clean up any previous state (PPTX slides, PDF doc, etc.)
            ClearPptxCache();
            _pdfState = null;
            Thumbnails.Clear();

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
}
