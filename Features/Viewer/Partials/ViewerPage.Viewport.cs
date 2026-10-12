using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using QuickView.Core.Models;
using QuickView.Rendering.Pdf;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Features.Viewer;

/// <summary>
/// PDF rendering: each page is rendered as a full WriteableBitmap and placed
/// on the canvas at its computed Y offset. Only pages near the viewport are
/// kept in the cache — others are evicted to free memory.
///
/// COORDINATE MODEL
///   PDF points (pt) → layout pixels (lp) via PtToLp = 96/72
///   Canvas is zoom-independent: sized in lp at zoom 1.0, scaled by ScrollViewer.ZoomFactor
///   Pages are rendered at screen DPI × displayScale for sharpness
/// </summary>
public sealed partial class ViewerPage
{
    private const double PtToLp   = 96.0 / 72.0;
    private const double LpToPt   = 72.0 / 96.0;
    private const double PageGapLp = 12.0;           // gap between pages in layout pixels

    private PdfViewportState? _viewportState;

    // Per-page Image controls on the canvas
    private readonly Dictionary<int, Image> _pdfPageCache = new();
    // Per-page text selection overlays
    private readonly Dictionary<int, UI.Controls.PdfTextSelectionOverlay> _pdfPageSelectionOverlays = new();
    // FEAT-004: Memory budget tracking
    private long _cachedBitmapBytes = 0L;
    private readonly Dictionary<int, long> _pdfPageByteSize = new();
    private const long MaxCacheBudgetBytes = 200_000_000L; // 200 MB budget
    
    private CancellationTokenSource? _renderCts;
    private float _lastRenderZoom = -1f;

    // ── Improvement 1: Concurrent rendering ───────────────────────────────────
    // Allow up to (CPU count) concurrent PDFium renders instead of pure serialization.
    // PDFium is thread-safe for concurrent FPDF_RenderPageBitmap calls on the
    // same document as long as we hold the gate during load/close of the page handle.
    private static readonly SemaphoreSlim _renderSemaphore =
        new(Math.Max(1, Environment.ProcessorCount), Math.Max(1, Environment.ProcessorCount));

    // ── Improvement 2: Cached page Y-offsets ──────────────────────────────────
    // Pre-computed so GetPageTopLp is O(1) instead of O(n) on every call.
    private double[]? _pageTopCache;

    // ── Init / Cleanup ────────────────────────────────────────────────────────

    private void InitializeViewportRendering()
    {
        if (_pdfState == null) return;

        // Let the ScrollViewer own zoom so pages scale naturally
        DocumentScrollViewer.ZoomMode = ZoomMode.Enabled;

        _lastRenderZoom = -1f;
        _viewportState = new PdfViewportState
        {
            ViewportWidthPx  = (int)DocumentScrollViewer.ViewportWidth,
            ViewportHeightPx = (int)DocumentScrollViewer.ViewportHeight,
            ZoomFactor       = 1.0f,
            ScrollXPt        = 0,
            ScrollYPt        = 0,
            CurrentPageIndex = 0
        };

        UpdateCanvasSize();

        DocumentScrollViewer.ViewChanged -= OnViewportViewChanged;
        DocumentScrollViewer.ViewChanged += OnViewportViewChanged;

        _ = RenderPagesAsync();
    }

    private void CleanupViewportRendering()
    {
        DocumentScrollViewer.ViewChanged -= OnViewportViewChanged;
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = null;
        _viewportState = null;
        _lastRenderZoom = -1f;
        _pageTopCache = null;
        ClearPdfPageCache();
    }

    private void ClearPdfPageCache()
    {
        foreach (var img in _pdfPageCache.Values)
        {
            PdfViewportCanvas.Children.Remove(img);
            img.Source = null;
        }
        _pdfPageCache.Clear();

        // Clear selection overlays
        foreach (var overlay in _pdfPageSelectionOverlays.Values)
        {
            PdfViewportCanvas.Children.Remove(overlay);
        }
        _pdfPageSelectionOverlays.Clear();

        // FEAT-004: Reset memory budget tracking
        _cachedBitmapBytes = 0L;
        _pdfPageByteSize.Clear();

        // Keep ViewportImage (hide it — not used for PDF page cache)
        ViewportImage.Source = null;
        ViewportImage.Visibility = Visibility.Collapsed;
    }

    // ── Canvas sizing ─────────────────────────────────────────────────────────

    internal void UpdateCanvasSize()
    {
        if (_pdfState == null) return;

        // Rebuild the O(1) page-top cache at the same time.
        _pageTopCache = new double[_pdfState.PageCount];
        double maxW = 0, totalH = 0;
        for (int i = 0; i < _pdfState.PageCount; i++)
        {
            var (w, h) = GetRotatedPageSize(i);
            _pageTopCache[i] = totalH;
            maxW    = Math.Max(maxW, w * PtToLp);
            totalH += h * PtToLp;
            if (i < _pdfState.PageCount - 1) totalH += PageGapLp;
        }

        PdfViewportCanvas.Width  = maxW;
        PdfViewportCanvas.Height = totalH;

        if (SearchHighlightCanvas != null)
        {
            SearchHighlightCanvas.Width  = maxW;
            SearchHighlightCanvas.Height = totalH;
        }
    }

    private (double W, double H) GetRotatedPageSize(int i)
    {
        var (w, h) = _pdfState!.GetPageSize(i);
        int rot = ((int)Math.Round(_rotation) % 360 + 360) % 360;
        return (rot == 90 || rot == 270) ? (h, w) : (w, h);
    }

    // ── Scroll / zoom handler ─────────────────────────────────────────────────

    private void OnViewportViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        try
        {
            if (_viewportState == null || _pdfState == null)
            {
                return;
            }

            float zoom = DocumentScrollViewer.ZoomFactor;
            _viewportState.ZoomFactor       = zoom;
            _viewportState.ViewportWidthPx  = (int)DocumentScrollViewer.ViewportWidth;
            _viewportState.ViewportHeightPx = (int)DocumentScrollViewer.ViewportHeight;
            _viewportState.ScrollXPt        = DocumentScrollViewer.HorizontalOffset * LpToPt;
            _viewportState.ScrollYPt        = DocumentScrollViewer.VerticalOffset   * LpToPt;

            UpdateCurrentPageFromScroll();

            // BUGFIX: On zoom change, invalidate cached pages so they re-render at new DPI
            // But don't clear them immediately - let old ones show until new ones are ready
            bool zoomChanged = Math.Abs(zoom - _lastRenderZoom) > _lastRenderZoom * 0.15f;

            if (!e.IsIntermediate)
            {
                if (zoomChanged)
                {
                    _lastRenderZoom = zoom;
                    
                    // Mark all cached pages as needing re-render by clearing cache tracking
                    // but leave the images visible on canvas until new renders replace them
                    var cachedPages = _pdfPageCache.Keys.ToList();
                    foreach (var pageIndex in cachedPages)
                    {
                        // Remove from cache tracking so RenderPagesAsync will re-render
                        _pdfPageCache.Remove(pageIndex);
                        if (_pdfPageByteSize.TryGetValue(pageIndex, out long byteSize))
                        {
                            _cachedBitmapBytes -= byteSize;
                            _pdfPageByteSize.Remove(pageIndex);
                        }
                        // Note: Images stay on canvas and visible until replaced
                    }
                    
                    Infrastructure.Logging.Logger.Log($"[Viewport] Zoom changed to {zoom:F2}, invalidated {cachedPages.Count} cached pages for re-render");
                }
                
                // Render visible pages at current zoom
                _ = RenderPagesAsync();
            }
        }
        catch (Exception ex)
        {
            Infrastructure.Logging.Logger.Error($"[Viewport] OnViewportViewChanged crashed", ex);
        }
    }

    // ── Page tracking ─────────────────────────────────────────────────────────

    private void UpdateCurrentPageFromScroll()
    {
        if (_pdfState == null || _viewportState == null) return;

        double scrollYLp = _viewportState.ScrollYPt / LpToPt;
        double readingY  = scrollYLp + (_viewportState.ViewportHeightPx * 0.1);

        double topLp = 0;
        for (int i = 0; i < _pdfState.PageCount; i++)
        {
            var (_, h) = GetRotatedPageSize(i);
            double bottomLp = topLp + h * PtToLp;
            if (readingY <= bottomLp || i == _pdfState.PageCount - 1)
            {
                int newPage = i + 1;
                if (newPage != _page)
                {
                    _viewportState.CurrentPageIndex = i;
                    int captured = i;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (_viewportState?.CurrentPageIndex == captured)
                            UpdateCurrentPage(captured + 1);
                    });
                }
                break;
            }
            topLp = bottomLp + PageGapLp;
        }
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    private async Task RenderPagesAsync()
    {
        // Must start on UI thread to access ScrollViewer properties and manipulate canvas safely
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => _ = RenderPagesAsync());
            return;
        }

        try
        {
            Infrastructure.Logging.Logger.Log("[Render] RenderPagesAsync started");
            
            if (_pdfState == null || _viewportState == null)
            {
                Infrastructure.Logging.Logger.Log("[Render] Skipping - null state");
                return;
            }

            // Cancel any in-flight render (safe on UI thread)
            _renderCts?.Cancel();
            _renderCts?.Dispose();
            _renderCts = new CancellationTokenSource();
            var ct = _renderCts.Token;

            Infrastructure.Logging.Logger.Log("[Render] Calculating render parameters");
            
            float  zoom    = DocumentScrollViewer.ZoomFactor;
            double scrollY = DocumentScrollViewer.VerticalOffset;
            double vpH     = DocumentScrollViewer.ViewportHeight;
            double display = Math.Max(1.0, XamlRoot?.RasterizationScale ?? 1.0);

            Infrastructure.Logging.Logger.Log($"[Render] zoom={zoom:F2}, scrollY={scrollY:F0}, vpH={vpH:F0}, display={display:F2}");

            // Calculate render DPI from zoom × display scale (no artificial cap)
            // Byte-size guards in RenderSinglePageAsync prevent OOM
            float dpi = (float)(96.0 * display * zoom);
            
            Infrastructure.Logging.Logger.Log($"[Render] Calculated DPI: {dpi:F1} (zoom={zoom:F2} × display={display:F2})");

            // Strictly visible pages (no buffer) — highest priority
            var strictVisible = GetVisiblePageRange(scrollY, vpH, 0);
            // Visible + lookahead buffer for eviction bounds
            int bufferPages = 2;
            var withBuffer  = GetVisiblePageRange(scrollY, vpH, bufferPages);
            int firstVisible = withBuffer.First;
            int lastVisible  = withBuffer.Last;

            Infrastructure.Logging.Logger.Log($"[Render] Visible range: {strictVisible.First}-{strictVisible.Last}, with buffer: {firstVisible}-{lastVisible}");

            // FEAT-004: Byte-based eviction - evict pages far from viewport first
            var toEvict = _pdfPageCache.Keys
                .Where(k => k < firstVisible - bufferPages || k > lastVisible + bufferPages)
                .OrderByDescending(k => Math.Min(Math.Abs(k - firstVisible), Math.Abs(k - lastVisible))) // Farthest first
                .ToList();
                
            Infrastructure.Logging.Logger.Log($"[Render] Evicting {toEvict.Count} pages outside buffer");
            
            foreach (int k in toEvict)
            {
                if (_pdfPageCache.TryGetValue(k, out var old))
                {
                    PdfViewportCanvas.Children.Remove(old);
                    old.Source = null;
                    _pdfPageCache.Remove(k);
                    
                    // FEAT-004: Decrement byte count
                    if (_pdfPageByteSize.TryGetValue(k, out long byteSize))
                    {
                        _cachedBitmapBytes -= byteSize;
                        _pdfPageByteSize.Remove(k);
                        Infrastructure.Logging.Logger.Log($"[Cache] Evicted page {k}, freed {byteSize / 1024}KB, new total: {_cachedBitmapBytes / 1024}KB");
                    }
                }
                
                // Also evict selection overlay
                if (_pdfPageSelectionOverlays.TryGetValue(k, out var overlay))
                {
                    PdfViewportCanvas.Children.Remove(overlay);
                    _pdfPageSelectionOverlays.Remove(k);
                }
            }

            // FEAT-004: Check if we're over budget and need to evict more offscreen pages
            var visibleToRender = Enumerable
                .Range(strictVisible.First, strictVisible.Last - strictVisible.First + 1)
                .Where(i => !_pdfPageCache.ContainsKey(i))
                .ToList();

            var bufferToRender = Enumerable
                .Range(firstVisible, lastVisible - firstVisible + 1)
                .Where(i => !_pdfPageCache.ContainsKey(i) && !visibleToRender.Contains(i))
                .ToList();

            // FEAT-004: Estimate bytes needed for new pages (conservative estimate)
            long estimatedNewBytes = (visibleToRender.Count + bufferToRender.Count) * 8_000_000L; // ~8MB per page estimate
            
            if (_cachedBitmapBytes + estimatedNewBytes > MaxCacheBudgetBytes)
            {
                Infrastructure.Logging.Logger.Log($"[Cache] Over budget: {_cachedBitmapBytes / 1024}KB + {estimatedNewBytes / 1024}KB > {MaxCacheBudgetBytes / 1024}KB, evicting offscreen pages");
                
                // Evict offscreen pages farthest from viewport until under budget
                var offscreenPages = _pdfPageCache.Keys
                    .Where(k => (k < strictVisible.First || k > strictVisible.Last))
                    .OrderByDescending(k => Math.Min(Math.Abs(k - strictVisible.First), Math.Abs(k - strictVisible.Last)))
                    .ToList();
                
                foreach (int k in offscreenPages)
                {
                    if (_cachedBitmapBytes + estimatedNewBytes <= MaxCacheBudgetBytes)
                        break;
                    
                    if (_pdfPageCache.TryGetValue(k, out var old))
                    {
                        PdfViewportCanvas.Children.Remove(old);
                        old.Source = null;
                        _pdfPageCache.Remove(k);
                        
                        if (_pdfPageByteSize.TryGetValue(k, out long byteSize))
                        {
                            _cachedBitmapBytes -= byteSize;
                            _pdfPageByteSize.Remove(k);
                            Infrastructure.Logging.Logger.Log($"[Cache] Budget evicted page {k}, freed {byteSize / 1024}KB, new total: {_cachedBitmapBytes / 1024}KB");
                        }
                    }
                    
                    if (_pdfPageSelectionOverlays.TryGetValue(k, out var overlay))
                    {
                        PdfViewportCanvas.Children.Remove(overlay);
                        _pdfPageSelectionOverlays.Remove(k);
                    }
                }
            }

            Infrastructure.Logging.Logger.Log($"[Render] Rendering {visibleToRender.Count} visible pages, {bufferToRender.Count} buffer pages");

            // Render strictly-visible pages first
            foreach (int pageIndex in visibleToRender)
            {
                if (ct.IsCancellationRequested)
                {
                    Infrastructure.Logging.Logger.Log("[Render] Cancelled during visible page render");
                    return;
                }
                Infrastructure.Logging.Logger.Log($"[Render] Rendering visible page {pageIndex}");
                await RenderSinglePageAsync(_pdfState, pageIndex, dpi, display, ct);
            }

            Infrastructure.Logging.Logger.Log("[Render] Starting buffer page render");

            // Render buffer pages concurrently (Improvement 1: SemaphoreSlim parallelism)
            var bufferTasks = bufferToRender.Select(pageIndex =>
                Task.Run(async () =>
                {
                    if (ct.IsCancellationRequested) return;
                    await _renderSemaphore.WaitAsync(ct);
                    try
                    {
                        Infrastructure.Logging.Logger.Log($"[Render] Rendering buffer page {pageIndex}");
                        await RenderSinglePageAsync(_pdfState, pageIndex, dpi, display, ct);
                    }
                    finally { _renderSemaphore.Release(); }
                }, ct));

            await Task.WhenAll(bufferTasks);
            
            Infrastructure.Logging.Logger.Log("[Render] RenderPagesAsync completed successfully");
        }
        catch (OperationCanceledException)
        {
            Infrastructure.Logging.Logger.Log("[Render] RenderPagesAsync cancelled");
        }
        catch (Exception ex)
        {
            Infrastructure.Logging.Logger.Error("[Render] RenderPagesAsync failed", ex);
            throw; // Re-throw to see crash
        }
    }

    /// <summary>Renders one page and places/updates its Image on the canvas.</summary>
    private async Task RenderSinglePageAsync(
        PdfDocumentState pdfState, int pageIndex, float dpi, double display, CancellationToken ct)
    {
        // Snapshot generation at the start to detect stale renders
        int snapshotGeneration = pdfState.Generation;
        
        try
        {
            Infrastructure.Logging.Logger.Log($"[RenderSingle] Starting page {pageIndex}, gen={snapshotGeneration}");
            
            if (ct.IsCancellationRequested)
            {
                Infrastructure.Logging.Logger.Log($"[RenderSingle] Cancelled before start - page {pageIndex}");
                return;
            }

            var (ptW, ptH) = GetRotatedPageSize(pageIndex);
            double pageW = ptW * PtToLp;
            double pageH = ptH * PtToLp;
            double pageY = GetPageTopLp(pageIndex);   // O(1) via cached array

            // For crisp high-DPI rendering, calculate render size from DPI (which already includes display scale)
            // Page size in points × (DPI / 72 points per inch) = pixel size
            int renderW = Math.Max(1, (int)Math.Round(ptW * dpi / 72.0));
            int renderH = Math.Max(1, (int)Math.Round(ptH * dpi / 72.0));

            // Validate byte count to prevent integer overflow and OOM
            long byteCount = (long)renderW * renderH * 4;
            const long MaxSafeBytesPerPage = 100_000_000L; // 100 MB safety limit per page
            
            if (byteCount > MaxSafeBytesPerPage)
            {
                // Scale down proportionally to fit within budget
                double scaleFactor = Math.Sqrt((double)MaxSafeBytesPerPage / byteCount);
                int origW = renderW;
                int origH = renderH;
                renderW = Math.Max(1, (int)(renderW * scaleFactor));
                renderH = Math.Max(1, (int)(renderH * scaleFactor));
                byteCount = (long)renderW * renderH * 4;
                Infrastructure.Logging.Logger.Log($"[RenderSingle] Page {pageIndex}: Scaled down to {renderW}x{renderH} (from {origW}x{origH}, {byteCount / 1024 / 1024}MB) to fit budget");
            }

            int rot = ((int)Math.Round(_rotation) % 360 + 360) % 360;
            int rotateParam = rot / 90; // 0, 1, 2, or 3

            Infrastructure.Logging.Logger.Log($"[RenderSingle] Page {pageIndex}: size={renderW}x{renderH} px at {dpi:F0} DPI, pageY={pageY:F0}");
            Infrastructure.Logging.Logger.Log($"[RenderSingle] Calling RenderFullPage for page {pageIndex}");

            var pixelBuffer = await Task.Run(() =>
                RenderFullPage(pdfState, pageIndex, dpi, renderW, renderH,
                               rotateParam, ct), ct);

            Infrastructure.Logging.Logger.Log($"[RenderSingle] RenderFullPage returned for page {pageIndex}, buffer={pixelBuffer != null}");

            if (pixelBuffer == null || ct.IsCancellationRequested)
            {
                Infrastructure.Logging.Logger.Log($"[RenderSingle] Null buffer or cancelled - page {pageIndex}");
                return;
            }

            // Check if document generation is still current (not closed/replaced)
            if (_pdfState == null || _pdfState.Generation != snapshotGeneration)
            {
                Infrastructure.Logging.Logger.Log($"[RenderSingle] Stale generation for page {pageIndex} - discarding result");
                ArrayPool<byte>.Shared.Return(pixelBuffer);
                return;
            }

            // WriteableBitmap must be created on the UI thread — dispatch if we're on a background thread
            Infrastructure.Logging.Logger.Log($"[RenderSingle] Creating bitmap from buffer - page {pageIndex}");
            WriteableBitmap? bitmap = null;
            try
            {
                bitmap = await CreateBitmapFromBufferAsync(pixelBuffer, renderW, renderH);
            }
            finally
            {
                // PDF-08 fix: Always return buffer even if bitmap creation throws
                ArrayPool<byte>.Shared.Return(pixelBuffer);
            }

            Infrastructure.Logging.Logger.Log($"[RenderSingle] Bitmap created, returned buffer - page {pageIndex}");

            if (bitmap == null || ct.IsCancellationRequested)
            {
                Infrastructure.Logging.Logger.Log($"[RenderSingle] Null bitmap or cancelled - page {pageIndex}");
                return;
            }

            // Final generation check before publishing to UI
            if (_pdfState == null || _pdfState.Generation != snapshotGeneration)
            {
                Infrastructure.Logging.Logger.Log($"[RenderSingle] Stale generation at UI publish for page {pageIndex} - discarding");
                return;
            }

            // Create Image on UI thread to avoid WinUI crash
            Infrastructure.Logging.Logger.Log($"[RenderSingle] Creating Image control - page {pageIndex}");
            bool success = false;
            if (DispatcherQueue.HasThreadAccess)
            {
                CreateImageOnUI();
                success = true;
            }
            else
            {
                var tcs = new TaskCompletionSource<bool>();
                DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        CreateImageOnUI();
                        tcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        Infrastructure.Logging.Logger.Error($"[RenderSingle] CreateImageOnUI failed - page {pageIndex}", ex);
                        tcs.SetException(ex);
                    }
                });
                success = await tcs.Task;
            }

            Infrastructure.Logging.Logger.Log($"[RenderSingle] Completed successfully - page {pageIndex}");

            void CreateImageOnUI()
            {
                // Check if image already exists
                bool imageExists = _pdfPageCache.TryGetValue(pageIndex, out var img);
                
                if (!imageExists)
                {
                    // Create new image
                    img = new Image
                    {
                        Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill,
                        Width   = pageW,
                        Height  = pageH,
                        Opacity = 0.0
                    };
                    
                    img.OpacityTransition = new Microsoft.UI.Xaml.ScalarTransition
                    {
                        Duration = TimeSpan.FromMilliseconds(167)
                    };
                    
                    Canvas.SetLeft(img, 0);
                    Canvas.SetTop(img, pageY);
                    
                    int insertAt = Math.Max(0, PdfViewportCanvas.Children.Count - 1);
                    if (SearchHighlightCanvas != null)
                        insertAt = Math.Max(0, PdfViewportCanvas.Children.IndexOf(SearchHighlightCanvas));
                    PdfViewportCanvas.Children.Insert(insertAt, img);
                    _pdfPageCache[pageIndex] = img;
                }
                else
                {
                    // Update existing image with new size and position for zoom
                    img.Width = pageW;
                    img.Height = pageH;
                    Canvas.SetTop(img, pageY);
                }

                // Set new bitmap source (replaces old low-res with new high-res)
                img.Source = bitmap;
                img.Opacity = 1.0;

                // FEAT-004: Track bitmap byte size
                long byteSize = (long)renderW * renderH * 4;
                if (!_pdfPageByteSize.ContainsKey(pageIndex))
                {
                    _pdfPageByteSize[pageIndex] = byteSize;
                    _cachedBitmapBytes += byteSize;
                    Infrastructure.Logging.Logger.Log($"[Cache] Page {pageIndex} cached: {byteSize / 1024}KB, total: {_cachedBitmapBytes / 1024}KB / {MaxCacheBudgetBytes / 1024}KB");
                }
                else
                {
                    // Update byte count when replacing with different resolution
                    long oldSize = _pdfPageByteSize[pageIndex];
                    _cachedBitmapBytes = _cachedBitmapBytes - oldSize + byteSize;
                    _pdfPageByteSize[pageIndex] = byteSize;
                    Infrastructure.Logging.Logger.Log($"[Cache] Page {pageIndex} re-rendered: {byteSize / 1024}KB (was {oldSize / 1024}KB), total: {_cachedBitmapBytes / 1024}KB");
                }

                // FEAT-002: Text selection overlay - DISABLED until drag system is fixed
                // TODO: Current overlay stops after 1 word, needs proper multi-word drag support
                // Will need to investigate:
                // - Character box coordinates at different zoom levels
                // - Pointer event capture across scroll operations
                // - Selection painting across zoom changes
                /*
                if (!_pdfPageSelectionOverlays.TryGetValue(pageIndex, out var selectionOverlay))
                {
                    selectionOverlay = new UI.Controls.PdfTextSelectionOverlay
                    {
                        Width  = pageW,
                        Height = pageH,
                    };
                    
                    Canvas.SetLeft(selectionOverlay, 0);
                    Canvas.SetTop(selectionOverlay, pageY);
                    Canvas.SetZIndex(selectionOverlay, 100);
                    
                    int selectionInsertAt = PdfViewportCanvas.Children.IndexOf(img) + 1;
                    if (SearchHighlightCanvas != null)
                    {
                        int highlightIndex = PdfViewportCanvas.Children.IndexOf(SearchHighlightCanvas);
                        if (highlightIndex > 0)
                            selectionInsertAt = Math.Min(selectionInsertAt, highlightIndex);
                    }
                    PdfViewportCanvas.Children.Insert(selectionInsertAt, selectionOverlay);
                    _pdfPageSelectionOverlays[pageIndex] = selectionOverlay;
                    
                    selectionOverlay.SetPage(_pdfState, pageIndex, pageW, pageH);
                    Infrastructure.Logging.Logger.Log($"[RenderSingle] Created selection overlay for page {pageIndex}");
                }
                else
                {
                    // Update overlay size and position for zoom
                    selectionOverlay.Width = pageW;
                    selectionOverlay.Height = pageH;
                    Canvas.SetTop(selectionOverlay, pageY);
                    // Refresh character boxes at new zoom
                    selectionOverlay.SetPage(_pdfState, pageIndex, pageW, pageH);
                }
                */
            }
        }
        catch (Exception ex)
        {
            Infrastructure.Logging.Logger.Error($"[RenderSingle] Exception on page {pageIndex}", ex);
            throw;
        }
    }

    /// <summary>Returns the first/last page indices that intersect the viewport + buffer.</summary>
    private (int First, int Last) GetVisiblePageRange(double scrollY, double vpH, int bufferPages)
    {
        if (_pdfState == null) return (0, 0);

        double topLp = 0;
        int first = 0, last = _pdfState.PageCount - 1;
        bool foundFirst = false;

        for (int i = 0; i < _pdfState.PageCount; i++)
        {
            var (_, h) = GetRotatedPageSize(i);
            double pageH      = h * PtToLp;
            double pageBottom = topLp + pageH;

            if (!foundFirst && pageBottom > scrollY)
            {
                first = Math.Max(0, i - bufferPages);
                foundFirst = true;
            }

            if (topLp > scrollY + vpH)
            {
                last = Math.Min(_pdfState.PageCount - 1, i + bufferPages);
                break;
            }

            topLp = pageBottom + PageGapLp;
        }

        return (first, last);
    }

    /// <summary>
    /// Returns the canvas Y offset of page i in layout pixels.
    /// Improvement 2: O(1) lookup via pre-computed _pageTopCache array.
    /// Falls back to O(n) if cache is not yet built.
    /// </summary>
    private double GetPageTopLp(int pageIndex)
    {
        if (_pageTopCache != null && (uint)pageIndex < (uint)_pageTopCache.Length)
            return _pageTopCache[pageIndex];

        // Fallback (should not normally be reached after InitializeViewportRendering)
        if (_pdfState == null) return 0;
        double y = 0;
        for (int i = 0; i < pageIndex; i++)
        {
            var (_, h) = GetRotatedPageSize(i);
            y += h * PtToLp + PageGapLp;
        }
        return y;
    }

    /// <summary>
    /// Renders a full PDF page into a rented BGRA byte buffer on a background thread.
    ///
    /// Improvement 1 (concurrent): page handle load/close is guarded by Gate, but
    /// FPDF_RenderPageBitmap itself runs concurrently — PDFium is safe for concurrent
    /// renders on the same document as long as we don't mutate the document.
    ///
    /// Improvement 4 (no double fill): removed the redundant CPU-side Buffer.BlockCopy
    /// white-fill loop. FPDFBitmap_FillRect already fills via native memset, which is
    /// faster and renders to the same rented buffer.
    /// </summary>
    private static byte[]? RenderFullPage(
        PdfDocumentState state, int pageIndex, float dpi,
        int renderW, int renderH, int rotate, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return null;

        int stride    = renderW * 4;
        int byteCount = stride * renderH;
        byte[] output = ArrayPool<byte>.Shared.Rent(byteCount);

        PdfRenderer.EnsureInitialized();

        // CRITICAL FIX: All PDFium operations including FPDF_RenderPageBitmap
        // must be under Pdfium.Gate lock. PDFium is NOT thread-safe for concurrent
        // rendering on the same document.
        lock (Pdfium.Gate)
        {
            if (ct.IsCancellationRequested)
            {
                ArrayPool<byte>.Shared.Return(output);
                return null;
            }
            
            IntPtr page = Pdfium.FPDF_LoadPage(state.Handle, pageIndex);
            if (page == IntPtr.Zero)
            {
                ArrayPool<byte>.Shared.Return(output);
                return null;
            }

            try
            {
                unsafe
                {
                    fixed (byte* p = output)
                    {
                        IntPtr bmp = Pdfium.FPDFBitmap_CreateEx(
                            renderW, renderH, Pdfium.BitmapBgra, (IntPtr)p, stride);
                        if (bmp == IntPtr.Zero)
                        {
                            ArrayPool<byte>.Shared.Return(output);
                            return null;
                        }
                        
                        try
                        {
                            Pdfium.FPDFBitmap_FillRect(bmp, 0, 0, renderW, renderH, 0xFFFFFFFF);
                            Pdfium.FPDF_RenderPageBitmap(
                                bmp, page,
                                0, 0, renderW, renderH,
                                rotate,
                                Pdfium.RenderAnnot | Pdfium.RenderLcdText);
                        }
                        finally
                        {
                            Pdfium.FPDFBitmap_Destroy(bmp);
                        }
                    }
                }
            }
            finally
            {
                Pdfium.FPDF_ClosePage(page);
            }
        }

        return output;
    }

    /// <summary>
    /// Renders a full PDF page with progressive rendering (time-sliced) to prevent
    /// UI freezes during scrolling. Yields to the UI thread every 16ms.
    /// Falls back to RenderFullPage on any error.
    /// </summary>
    private static byte[]? RenderFullPageProgressive(
        PdfDocumentState state, int pageIndex, float dpi,
        int renderW, int renderH, int rotate, CancellationToken ct)
    {
        try
        {
            if (ct.IsCancellationRequested) return null;

            int stride    = renderW * 4;
            int byteCount = stride * renderH;
            byte[] output = ArrayPool<byte>.Shared.Rent(byteCount);

            PdfRenderer.EnsureInitialized();

            IntPtr page = IntPtr.Zero;
            IntPtr bmp  = IntPtr.Zero;
            GCHandle cbHandle = default;
            Pdfium.PauseCallback? callback = null;
            Stopwatch sw = new Stopwatch();

            try
            {
                lock (Pdfium.Gate)
                {
                    if (ct.IsCancellationRequested)
                    {
                        ArrayPool<byte>.Shared.Return(output);
                        return null;
                    }

                    page = Pdfium.FPDF_LoadPage(state.Handle, pageIndex);
                    if (page == IntPtr.Zero)
                    {
                        ArrayPool<byte>.Shared.Return(output);
                        return null;
                    }

                    unsafe
                    {
                        fixed (byte* p = output)
                        {
                            bmp = Pdfium.FPDFBitmap_CreateEx(
                                renderW, renderH, Pdfium.BitmapBgra, (IntPtr)p, stride);
                            if (bmp == IntPtr.Zero)
                            {
                                Pdfium.FPDF_ClosePage(page);
                                ArrayPool<byte>.Shared.Return(output);
                                return null;
                            }

                            Pdfium.FPDFBitmap_FillRect(bmp, 0, 0, renderW, renderH, 0xFFFFFFFF);

                            // Create pause callback: returns 1 when elapsed > 16ms
                            callback = new Pdfium.PauseCallback(_ => sw.ElapsedMilliseconds > 16 ? 1 : 0);
                            cbHandle = GCHandle.Alloc(callback);

                            var pause = new Pdfium.IFSDK_PAUSE
                            {
                                version = 1,
                                NeedToPauseNow = Marshal.GetFunctionPointerForDelegate(callback)
                            };

                            try
                            {
                                sw.Start();
                                int status = Pdfium.FPDF_RenderPage_Start(
                                    bmp, page,
                                    0, 0, renderW, renderH,
                                    rotate,
                                    Pdfium.RenderAnnot | Pdfium.RenderLcdText,
                                    ref pause);

                                int iteration = 0;
                                while (status == Pdfium.FPDF_RENDER_TOBECONTINUED && 
                                       !ct.IsCancellationRequested && 
                                       iteration < 200)
                                {
                                    // Yield briefly to let other threadpool work proceed
                                    Thread.Yield();
                                    sw.Restart();
                                    status = Pdfium.FPDF_RenderPage_Continue(page, ref pause);
                                    iteration++;
                                }
                            }
                            finally
                            {
                                // Always close progressive render before closing page
                                Pdfium.FPDF_RenderPage_Close(page);
                            }
                        }
                    }

                    Pdfium.FPDF_ClosePage(page);
                    page = IntPtr.Zero;
                }
            }
            finally
            {
                if (cbHandle.IsAllocated)
                    cbHandle.Free();

                if (bmp != IntPtr.Zero)
                    Pdfium.FPDFBitmap_Destroy(bmp);

                if (page != IntPtr.Zero)
                {
                    lock (Pdfium.Gate)
                    {
                        Pdfium.FPDF_ClosePage(page);
                    }
                }
            }

            if (ct.IsCancellationRequested)
            {
                ArrayPool<byte>.Shared.Return(output);
                return null;
            }

            return output;
        }
        catch (Exception ex)
        {
            Infrastructure.Logging.Logger.Error("RenderFullPageProgressive failed, falling back to RenderFullPage", ex);
            // Fallback to non-progressive rendering
            return RenderFullPage(state, pageIndex, dpi, renderW, renderH, rotate, ct);
        }
    }

    /// <summary>
    /// Creates a WriteableBitmap from a raw BGRA pixel buffer.
    /// WriteableBitmap is a WinRT UI object and must be constructed on the UI thread.
    /// This method dispatches to the DispatcherQueue when called from a background thread.
    /// </summary>
    private Task<WriteableBitmap?> CreateBitmapFromBufferAsync(byte[] buffer, int width, int height)
    {
        // If already on the UI thread, create directly
        if (DispatcherQueue.HasThreadAccess)
            return Task.FromResult(CreateBitmapCore(buffer, width, height));

        // Otherwise, marshal to UI thread via DispatcherQueue
        var tcs = new TaskCompletionSource<WriteableBitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool enqueued = DispatcherQueue.TryEnqueue(() =>
        {
            tcs.TrySetResult(CreateBitmapCore(buffer, width, height));
        });

        if (!enqueued)
            tcs.TrySetResult(null);

        return tcs.Task;
    }

    private static WriteableBitmap? CreateBitmapCore(byte[] buffer, int width, int height)
    {
        try
        {
            var bmp = new WriteableBitmap(width, height);
            using var stream = bmp.PixelBuffer.AsStream();
            stream.Write(buffer, 0, width * height * 4);
            bmp.Invalidate();
            return bmp;
        }
        catch (Exception ex)
        {
            Infrastructure.Logging.Logger.Error("CreateBitmapFromBuffer failed", ex);
            return null;
        }
    }
}
