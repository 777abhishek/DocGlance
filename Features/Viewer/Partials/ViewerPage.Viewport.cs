using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using DocGlance.Core.Models;
using DocGlance.Rendering;
using DocGlance.Rendering.Pdf;
using DocGlance.Infrastructure.Logging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;

namespace DocGlance.Features.Viewer;

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
    private readonly Dictionary<int, float> _pdfPageDpi = new();
    // FEAT-004: Memory budget tracking
    private long _cachedBitmapBytes = 0L;
    private readonly Dictionary<int, long> _pdfPageByteSize = new();
    private const long MaxCacheBudgetBytes = RenderBudget.MaxViewerCacheBytes;
    
    private CancellationTokenSource? _renderCts;
    private float _lastRenderZoom = -1f;

    // PdfCraft serializes on a Mutex; extra in-flight pages only duplicate pixmaps in RAM.
    private static readonly SemaphoreSlim _renderSemaphore = new(1, 1);

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

        RenderPagesAsync().SafeFireAndForget("PdfViewportRender");
    }

    private void CleanupViewportRendering()
    {
        DocumentScrollViewer.ViewChanged -= OnViewportViewChanged;
        // Cancel only — in-flight render tasks hold tokens from this CTS and would
        // throw ObjectDisposedException if we disposed it out from under them.
        _renderCts?.Cancel();
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
        _pdfPageDpi.Clear();

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

            float zoom = Math.Max(0.01f, DocumentScrollViewer.ZoomFactor);
            _viewportState.ZoomFactor       = zoom;
            _viewportState.ViewportWidthPx  = (int)DocumentScrollViewer.ViewportWidth;
            _viewportState.ViewportHeightPx = (int)DocumentScrollViewer.ViewportHeight;
            // Canvas is zoom-independent layout pixels; ScrollViewer offsets are zoomed.
            _viewportState.ScrollXPt        = (DocumentScrollViewer.HorizontalOffset / zoom) * LpToPt;
            _viewportState.ScrollYPt        = (DocumentScrollViewer.VerticalOffset / zoom) * LpToPt;

            UpdateCurrentPageFromScroll();

            if (!e.IsIntermediate)
            {
                _lastRenderZoom = zoom;
                DrawAllHighlights();
                RenderPagesAsync().SafeFireAndForget("PdfViewportRender");
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

        double zoom = Math.Max(0.01, DocumentScrollViewer.ZoomFactor);
        double scrollYLp = DocumentScrollViewer.VerticalOffset / zoom;
        double readingY  = scrollYLp + (DocumentScrollViewer.ViewportHeight / zoom) * 0.1;

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
            DispatcherQueue.TryEnqueue(() => RenderPagesAsync().SafeFireAndForget("PdfViewportRender"));
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

            // Cancel any in-flight render (safe on UI thread; not disposing —
            // buffer-page tasks still hold tokens from the old CTS)
            _renderCts?.Cancel();
            _renderCts = new CancellationTokenSource();
            var ct = _renderCts.Token;

            Infrastructure.Logging.Logger.Log("[Render] Calculating render parameters");
            
            float  zoom    = Math.Max(0.01f, DocumentScrollViewer.ZoomFactor);
            double scrollY = DocumentScrollViewer.VerticalOffset / zoom;
            double vpH     = DocumentScrollViewer.ViewportHeight / zoom;
            double display = Math.Max(1.0, XamlRoot?.RasterizationScale ?? 1.0);

            Infrastructure.Logging.Logger.Log($"[Render] zoom={zoom:F2}, scrollYLp={scrollY:F0}, vpHLp={vpH:F0}, display={display:F2}");

            var sampleSize = GetRotatedPageSize(Math.Clamp(_viewportState.CurrentPageIndex, 0, _pdfState.PageCount - 1));
            float dpi = RenderBudget.PdfPageDpi(zoom, display, sampleSize.W, sampleSize.H);
            
            Infrastructure.Logging.Logger.Log($"[Render] Calculated DPI: {dpi:F1} (zoom={zoom:F2} × display={display:F2} × {RenderBudget.TextOversample:F2})");

            // Strictly visible pages (no buffer) — highest priority
            var strictVisible = GetVisiblePageRange(scrollY, vpH, 0);
            // Visible + one lookahead page
            int bufferPages = 1;
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
                    _pdfPageDpi.Remove(k);
                    
                    // FEAT-004: Decrement byte count
                    if (_pdfPageByteSize.TryGetValue(k, out long byteSize))
                    {
                        _cachedBitmapBytes -= byteSize;
                        _pdfPageByteSize.Remove(k);
                        Infrastructure.Logging.Logger.Log($"[Cache] Evicted page {k}, freed {byteSize / 1024}KB, new total: {_cachedBitmapBytes / 1024}KB");
                    }
                }
                
            }

            // FEAT-004: Check if we're over budget and need to evict more offscreen pages
            float TargetDpi(int i)
            {
                var size = GetRotatedPageSize(i);
                return RenderBudget.PdfPageDpi(zoom, display, size.W, size.H);
            }

            bool NeedsRender(int i) =>
                !_pdfPageCache.ContainsKey(i) ||
                !_pdfPageDpi.TryGetValue(i, out float cachedDpi) ||
                RenderBudget.NeedsRerender(cachedDpi, TargetDpi(i));

            var visibleToRender = Enumerable
                .Range(strictVisible.First, strictVisible.Last - strictVisible.First + 1)
                .Where(NeedsRender)
                .ToList();

            var bufferToRender = Enumerable
                .Range(firstVisible, lastVisible - firstVisible + 1)
                .Where(i => NeedsRender(i) && !visibleToRender.Contains(i))
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
                        _pdfPageDpi.Remove(k);
                        
                        if (_pdfPageByteSize.TryGetValue(k, out long byteSize))
                        {
                            _cachedBitmapBytes -= byteSize;
                            _pdfPageByteSize.Remove(k);
                            Infrastructure.Logging.Logger.Log($"[Cache] Budget evicted page {k}, freed {byteSize / 1024}KB, new total: {_cachedBitmapBytes / 1024}KB");
                        }
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
            // Logged here, NOT rethrown: every caller is fire-and-forget, so a rethrow
            // would surface only as an unobserved task exception on the finalizer thread.
            Infrastructure.Logging.Logger.Error("[Render] RenderPagesAsync failed", ex);
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
            float zoom = _viewportState?.ZoomFactor ?? 1f;
            dpi = RenderBudget.PdfPageDpi(zoom, display, ptW, ptH);
            if (_pdfPageCache.ContainsKey(pageIndex) &&
                _pdfPageDpi.TryGetValue(pageIndex, out float cachedDpi) &&
                !RenderBudget.NeedsRerender(cachedDpi, dpi))
            {
                return;
            }

            double pageW = ptW * PtToLp;
            double pageH = ptH * PtToLp;
            double pageY = GetPageTopLp(pageIndex);   // O(1) via cached array

            // For crisp high-DPI rendering, calculate render size from DPI (which already includes display scale)
            // Page size in points × (DPI / 72 points per inch) = pixel size
            int renderW = Math.Max(1, (int)Math.Round(ptW * dpi / 72.0));
            int renderH = Math.Max(1, (int)Math.Round(ptH * dpi / 72.0));

            // Validate byte count to prevent integer overflow and OOM
            long byteCount = (long)renderW * renderH * 4;
            const long MaxSafeBytesPerPage = RenderBudget.MaxPageBitmapBytes;
            
            if (byteCount > MaxSafeBytesPerPage)
            {
                double scaleFactor = Math.Sqrt((double)MaxSafeBytesPerPage / byteCount);
                dpi *= (float)scaleFactor;
                renderW = Math.Max(1, (int)Math.Round(ptW * dpi / 72.0));
                renderH = Math.Max(1, (int)Math.Round(ptH * dpi / 72.0));
                byteCount = (long)renderW * renderH * 4;
            }

            Infrastructure.Logging.Logger.Log($"[RenderSingle] Page {pageIndex}: size={renderW}x{renderH} px at {dpi:F0} DPI, pageY={pageY:F0}");
            Infrastructure.Logging.Logger.Log($"[RenderSingle] Calling PdfCraft renderer for page {pageIndex}");

            WriteableBitmap? bitmap = await PdfRenderer.RenderPageAsync(pdfState, pageIndex, dpi, ct);
            if (bitmap == null || ct.IsCancellationRequested)
            {
                Infrastructure.Logging.Logger.Log($"[RenderSingle] Null bitmap or cancelled - page {pageIndex}");
                return;
            }

            // Check if document generation is still current (not closed/replaced)
            if (_pdfState == null || _pdfState.Generation != snapshotGeneration)
            {
                Infrastructure.Logging.Logger.Log($"[RenderSingle] Stale generation for page {pageIndex} - discarding result");
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
                if (!_pdfPageCache.TryGetValue(pageIndex, out var img))
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

                img.Source = bitmap;
                img.Opacity = 1.0;
                _pdfPageDpi[pageIndex] = dpi;

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

            }
        }
        catch (Exception ex)
        {
            Infrastructure.Logging.Logger.Error($"[RenderSingle] Exception on page {pageIndex}", ex);
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

}
