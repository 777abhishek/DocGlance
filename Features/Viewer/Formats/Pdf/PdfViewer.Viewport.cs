using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using DocGlance.Rendering.Shared;
using DocGlance.Rendering.Pdf.Models;
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
using Windows.Foundation;

namespace DocGlance.Features.Viewer.Formats.Pdf;

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
public sealed partial class PdfViewer
{
    private const double PtToLp   = 96.0 / 72.0;
    private const double LpToPt   = 72.0 / 96.0;
    private const double PageGapLp = 12.0;           // gap between pages in layout pixels

    // Per-page Image controls on the canvas
    private readonly Dictionary<int, Image> _pdfPageCache = new();
    private readonly Dictionary<int, float> _pdfPageDpi = new();
    // FEAT-004: Memory budget tracking
    private long _cachedBitmapBytes = 0L;
    private readonly Dictionary<int, long> _pdfPageByteSize = new();
    private const long MaxCacheBudgetBytes = RenderBudget.MaxViewerCacheBytes;
    
    private CancellationTokenSource? _renderCts;
    private float _lastRenderZoom = -1f;

    // Lookahead pages rendered past the visible range; eviction keeps this many beyond it too.
    private const int RenderLookaheadPages = 1;

    // Coalesces bursts of ViewChanged events (mouse-wheel ticks, scrollbar drags) into a
    // single render pass; without it every event cancels the previous pass's in-flight
    // renders and their bitmaps are thrown away.
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _renderDebounceTimer;
    private static readonly TimeSpan RenderDebounceInterval = TimeSpan.FromMilliseconds(120);

    // PdfCraft serializes on a Mutex; extra in-flight pages only duplicate pixmaps in RAM.
    private static readonly SemaphoreSlim _renderSemaphore = new(1, 1);

    // ── In-flight render dedup ─────────────────────────────────────────────────
    // PdfCraft's native renders cannot be aborted once queued, and every render pass
    // used to enqueue its own copy of a still-uncached page — during a scroll the same
    // page piled up 5–11 deep behind the native mutex and wait times ballooned
    // (observed 18–46s per page). One claim per page fixes that; claims are stamped
    // with an epoch so rotation/annotation invalidation can retire stale ones.
    private readonly object _inFlightLock = new();
    private readonly Dictionary<int, int> _inFlightPages = new(); // page → epoch when claimed
    private int _renderEpoch;

    private bool IsPageInFlight(int pageIndex)
    {
        lock (_inFlightLock) return _inFlightPages.ContainsKey(pageIndex);
    }

    // ── Improvement 2: Cached page Y-offsets ──────────────────────────────────
    // Pre-computed so GetPageTopLp is O(1) instead of O(n) on every call.
    private double[]? _pageTopCache;

    private float DisplayScale => (float)(XamlRoot?.RasterizationScale ?? 1.0);

    internal event EventHandler<int>? PageChanged;
    internal event EventHandler? ClearTextSelectionRequested;

    // ── Init / Cleanup ────────────────────────────────────────────────────────

    internal void InitializeViewportRendering()
    {
        if (DocumentState == null) return;

        // Let the ScrollViewer own zoom so pages scale naturally
        DocumentScrollViewer.ZoomMode = ZoomMode.Enabled;

        _lastRenderZoom = -1f;
        ViewportState = new PdfViewportState
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
        DocumentScrollViewer.SizeChanged -= OnViewportSizeChanged;
        DocumentScrollViewer.SizeChanged += OnViewportSizeChanged;
        EnsureTextPageMap(0);

        RenderPagesAsync().SafeFireAndForget("PdfViewportRender");
    }

    internal void CleanupViewportRendering()
    {
        DocumentScrollViewer.ViewChanged -= OnViewportViewChanged;
        DocumentScrollViewer.SizeChanged -= OnViewportSizeChanged;
        _renderDebounceTimer?.Stop();
        // Cancel only — in-flight render tasks hold tokens from this CTS and would
        // throw ObjectDisposedException if we disposed it out from under them.
        _renderCts?.Cancel();
        _renderCts = null;
        ClearTextSelectionRequested?.Invoke(this, EventArgs.Empty);
        ViewportState = null;
        _lastRenderZoom = -1f;
        _pageTopCache = null;
        ClearPdfPageCache();
    }

    internal void CancelPendingRenders()
    {
        _renderCts?.Cancel();
        // Invalidate in-flight claims: their results are stale (rotation/annotation
        // state changed) and new passes must be free to claim the same pages.
        Interlocked.Increment(ref _renderEpoch);
        lock (_inFlightLock) _inFlightPages.Clear();
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
        if (DocumentState == null) return;

        // Rebuild the O(1) page-top cache at the same time.
        _pageTopCache = new double[DocumentState.PageCount];
        double maxW = 0, totalH = 0;
        for (int i = 0; i < DocumentState.PageCount; i++)
        {
            var (w, h) = GetRotatedPageSize(i);
            _pageTopCache[i] = totalH;
            maxW    = Math.Max(maxW, w * PtToLp);
            totalH += h * PtToLp;
            if (i < DocumentState.PageCount - 1) totalH += PageGapLp;
        }

        double maxWidth = maxW;
        double totalHeightLp = totalH;
        PdfViewportCanvas.Width = maxWidth;
        PdfViewportCanvas.Height = totalHeightLp;
        SearchHighlightCanvas.Width = maxWidth;
        SearchHighlightCanvas.Height = totalHeightLp;
        PdfAnnotationCanvas.Width = maxWidth;
        PdfAnnotationCanvas.Height = totalHeightLp;
        PdfSelectionCanvas.Width = maxWidth;
        PdfSelectionCanvas.Height = totalHeightLp;
        DrawAnnotationOverlays();
    }

    internal (double W, double H) GetRotatedPageSize(int i)
    {
        var (w, h) = DocumentState!.GetPageSize(i);
        int rot = ((int)Math.Round(PdfRotation) % 360 + 360) % 360;
        return (rot == 90 || rot == 270) ? (h, w) : (w, h);
    }

    internal Rect TransformPageRect(
        int pageIndex,
        double xPt,
        double yPt,
        double widthPt,
        double heightPt)
    {
        const double ptToLp = 96.0 / 72.0;
        var (widthPtRaw, heightPtRaw) = DocumentState!.GetPageSize(pageIndex);
        return PageRotation.RotateRect(
            new Rect(xPt * ptToLp, yPt * ptToLp, widthPt * ptToLp, heightPt * ptToLp),
            widthPtRaw * ptToLp,
            heightPtRaw * ptToLp,
            PdfRotation);
    }

    // ── Scroll / zoom handler ─────────────────────────────────────────────────

    private void OnViewportViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        try
        {
            if (ViewportState == null || DocumentState == null)
            {
                return;
            }

            float zoom = Math.Max(0.01f, DocumentScrollViewer.ZoomFactor);
            ViewportState.ZoomFactor       = zoom;
            ViewportState.ViewportWidthPx  = (int)DocumentScrollViewer.ViewportWidth;
            ViewportState.ViewportHeightPx = (int)DocumentScrollViewer.ViewportHeight;
            // Canvas is zoom-independent layout pixels; ScrollViewer offsets are zoomed.
            ViewportState.ScrollXPt        = (DocumentScrollViewer.HorizontalOffset / zoom) * LpToPt;
            ViewportState.ScrollYPt        = (DocumentScrollViewer.VerticalOffset / zoom) * LpToPt;

            UpdateCurrentPageFromScroll();
            EnsureTextPageMap(Math.Clamp(ViewportState.CurrentPageIndex, 0, DocumentState.PageCount - 1));

            if (!e.IsIntermediate)
            {
                _lastRenderZoom = zoom;
                RefreshHighlights();
                ScheduleDebouncedViewportRender();
            }
        }
        catch (Exception ex)
        {
            Infrastructure.Logging.Logger.Error($"[Viewport] OnViewportViewChanged crashed", ex);
        }
    }

    /// <summary>
    /// Runs one render pass ~120ms after the last scroll/zoom event settles, so a burst
    /// of ViewChanged events costs a single pass instead of one cancel+restart per event.
    /// </summary>
    private void ScheduleDebouncedViewportRender()
    {
        if (_renderDebounceTimer == null)
        {
            _renderDebounceTimer = DispatcherQueue.CreateTimer();
            _renderDebounceTimer.IsRepeating = false;
            _renderDebounceTimer.Interval = RenderDebounceInterval;
            _renderDebounceTimer.Tick += (_, _) =>
                RenderPagesAsync().SafeFireAndForget("PdfViewportRender");
        }
        _renderDebounceTimer.Stop();
        _renderDebounceTimer.Start();
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // First layout or a restored tab becoming visible: the pass that ran before
        // layout skipped (viewport had no height), so render now that it does.
        if (e.NewSize.Height > 0)
            ScheduleDebouncedViewportRender();
    }

    // ── Page tracking ─────────────────────────────────────────────────────────

    private void UpdateCurrentPageFromScroll()
    {
        if (DocumentState == null || ViewportState == null) return;

        double zoom = Math.Max(0.01, DocumentScrollViewer.ZoomFactor);
        double scrollYLp = DocumentScrollViewer.VerticalOffset / zoom;
        double readingY  = scrollYLp + (DocumentScrollViewer.ViewportHeight / zoom) * 0.1;

        double topLp = 0;
        for (int i = 0; i < DocumentState.PageCount; i++)
        {
            var (_, h) = GetRotatedPageSize(i);
            double bottomLp = topLp + h * PtToLp;
            if (readingY <= bottomLp || i == DocumentState.PageCount - 1)
            {
                EnsureTextPageMap(i);
                int newPage = i + 1;
                if (newPage != ViewportState.CurrentPageIndex + 1)
                {
                    ViewportState.CurrentPageIndex = i;
                    int captured = i;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (ViewportState?.CurrentPageIndex == captured)
                            PageChanged?.Invoke(this, captured + 1);
                    });
                }
                break;
            }
            topLp = bottomLp + PageGapLp;
        }
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    internal async Task RenderPagesAsync()
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
            
            if (DocumentState == null || ViewportState == null)
            {
                Infrastructure.Logging.Logger.Log("[Render] Skipping - null state");
                return;
            }

            // A restored-but-never-shown tab has no layout yet (viewport height 0);
            // rendering it would burn native renders for pages nobody can see.
            // OnViewportSizeChanged re-triggers the pass once the tab is laid out.
            if (DocumentScrollViewer.ViewportHeight <= 0)
            {
                Infrastructure.Logging.Logger.Log("[Render] Viewport not laid out - skipping pass");
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

            var sampleSize = GetRotatedPageSize(Math.Clamp(ViewportState.CurrentPageIndex, 0, DocumentState.PageCount - 1));
            float dpi = RenderBudget.PdfPageDpi(zoom, display, sampleSize.W, sampleSize.H);
            
            Infrastructure.Logging.Logger.Log($"[Render] Calculated DPI: {dpi:F1} (zoom={zoom:F2} × display={display:F2} × {RenderBudget.TextOversample:F2})");

            // Strictly visible pages (no buffer) — highest priority
            var strictVisible = GetVisiblePageRange(scrollY, vpH, 0);
            // Visible + lookahead pages
            var withBuffer  = GetVisiblePageRange(scrollY, vpH, RenderLookaheadPages);
            int firstVisible = withBuffer.First;
            int lastVisible  = withBuffer.Last;

            Infrastructure.Logging.Logger.Log($"[Render] Visible range: {strictVisible.First}-{strictVisible.Last}, with buffer: {firstVisible}-{lastVisible}");

            // FEAT-004: Byte-based eviction - evict pages far from viewport first
            var toEvict = _pdfPageCache.Keys
                .Where(k => k < firstVisible - RenderLookaheadPages || k > lastVisible + RenderLookaheadPages)
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

            // Pages already in flight are skipped: their render publishes itself when
            // done (if still wanted) — enqueuing another copy just clogs the native queue.
            bool NeedsRender(int i) =>
                !IsPageInFlight(i) &&
                (!_pdfPageCache.ContainsKey(i) ||
                 !_pdfPageDpi.TryGetValue(i, out float cachedDpi) ||
                 RenderBudget.NeedsRerender(cachedDpi, TargetDpi(i)));

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
                await RenderSinglePageAsync(DocumentState, pageIndex, dpi, display, ct);
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
                        await RenderSinglePageAsync(DocumentState, pageIndex, dpi, display, ct);
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

        if (ct.IsCancellationRequested)
        {
            Infrastructure.Logging.Logger.Log($"[RenderSingle] Cancelled before start - page {pageIndex}");
            return;
        }

        // One native render per page: duplicates queue behind PdfCraft's internal
        // mutex and cannot be cancelled once started. Claimed before the try so the
        // finally can always see the epoch it must release.
        int epoch = Volatile.Read(ref _renderEpoch);
        lock (_inFlightLock)
        {
            if (!_inFlightPages.TryAdd(pageIndex, epoch))
            {
                Infrastructure.Logging.Logger.Log($"[RenderSingle] Duplicate suppressed - page {pageIndex}");
                return;
            }
        }

        try
        {
            Infrastructure.Logging.Logger.Log($"[RenderSingle] Starting page {pageIndex}, gen={snapshotGeneration}");

            var (ptW, ptH) = pdfState.GetPageSize(pageIndex);
            var (displayWidthPt, displayHeightPt) = GetRotatedPageSize(pageIndex);
            float zoom = ViewportState?.ZoomFactor ?? 1f;
            dpi = RenderBudget.PdfPageDpi(zoom, display, displayWidthPt, displayHeightPt);
            if (_pdfPageCache.ContainsKey(pageIndex) &&
                _pdfPageDpi.TryGetValue(pageIndex, out float cachedDpi) &&
                !RenderBudget.NeedsRerender(cachedDpi, dpi))
            {
                return;
            }

            double pageW = ptW * PtToLp;
            double pageH = ptH * PtToLp;
            var (pageDisplayWidthPt, pageDisplayHeightPt) = GetRotatedPageSize(pageIndex);
            double displayWidth = pageDisplayWidthPt * PtToLp;
            double displayHeight = pageDisplayHeightPt * PtToLp;
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
            if (bitmap == null)
            {
                Infrastructure.Logging.Logger.Log($"[RenderSingle] Null bitmap - page {pageIndex}");
                return;
            }

            // A newer render pass may have cancelled this one while the bitmap was being
            // produced. The render is already paid for, so publish it when the page is
            // still inside the current viewport range; drop it only when the page has
            // scrolled away or the cache already holds an equal-or-better bitmap.
            if ((ct.IsCancellationRequested || Volatile.Read(ref _renderEpoch) != epoch) &&
                !await IsSupersededRenderStillWantedAsync(pdfState, pageIndex, dpi))
            {
                Infrastructure.Logging.Logger.Log($"[RenderSingle] Superseded render discarded - page {pageIndex}");
                return;
            }

            // Rotation/annotation invalidation replaced this render's geometry — publish
            // would place the bitmap with stale layout, so let the fresh pass render it.
            if (Volatile.Read(ref _renderEpoch) != epoch)
            {
                Infrastructure.Logging.Logger.Log($"[RenderSingle] Stale epoch discarded - page {pageIndex}");
                return;
            }

            // Check if document generation is still current (not closed/replaced)
            if (DocumentState == null || DocumentState.Generation != snapshotGeneration)
            {
                Infrastructure.Logging.Logger.Log($"[RenderSingle] Stale generation for page {pageIndex} - discarding result");
                return;
            }

            // Final generation check before publishing to UI
            if (DocumentState == null || DocumentState.Generation != snapshotGeneration)
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
                    // Create new image with all transform properties set BEFORE any transitions
                    img = new Image
                    {
                        Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill,
                        Width   = pageW,
                        Height  = pageH,
                        Opacity = 0.0,
                        RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
                        RenderTransform = new Microsoft.UI.Xaml.Media.RotateTransform { Angle = PdfRotation }
                    };
                    
                    // Do not set OpacityTransition on this element: WinUI 3 rejects it with
                    // 0x80070005 while a RenderTransform is attached.

                    Canvas.SetLeft(img, (displayWidth - pageW) / 2);
                    Canvas.SetTop(img, pageY + (displayHeight - pageH) / 2);
                    
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
                    img.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
                    img.RenderTransform = new Microsoft.UI.Xaml.Media.RotateTransform { Angle = PdfRotation };
                    Canvas.SetLeft(img, (displayWidth - pageW) / 2);
                    Canvas.SetTop(img, pageY + (displayHeight - pageH) / 2);
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
        finally
        {
            lock (_inFlightLock)
            {
                if (_inFlightPages.TryGetValue(pageIndex, out int claimedEpoch) && claimedEpoch == epoch)
                    _inFlightPages.Remove(pageIndex);
            }
        }
    }

    /// <summary>
    /// Decides whether a bitmap orphaned by a newer render pass should still be published:
    /// the page must still intersect the viewport plus its lookahead buffer, and the cache
    /// must not already hold an equal-or-better bitmap. Must run on the UI thread.
    /// </summary>
    private bool IsSupersededRenderStillWanted(PdfDocumentState pdfState, int pageIndex, float dpi)
    {
        if (!ReferenceEquals(DocumentState, pdfState) || ViewportState == null)
            return false;

        if (_pdfPageDpi.TryGetValue(pageIndex, out float cachedDpi) && cachedDpi >= dpi)
            return false;

        float zoom = Math.Max(0.01f, DocumentScrollViewer.ZoomFactor);
        double scrollY = DocumentScrollViewer.VerticalOffset / zoom;
        double vpH = DocumentScrollViewer.ViewportHeight / zoom;
        var (first, last) = GetVisiblePageRange(scrollY, vpH, RenderLookaheadPages);
        return pageIndex >= first && pageIndex <= last;
    }

    private async Task<bool> IsSupersededRenderStillWantedAsync(
        PdfDocumentState pdfState, int pageIndex, float dpi)
    {
        if (DispatcherQueue.HasThreadAccess)
            return IsSupersededRenderStillWanted(pdfState, pageIndex, dpi);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                tcs.TrySetResult(IsSupersededRenderStillWanted(pdfState, pageIndex, dpi));
            }
            catch (Exception ex)
            {
                Infrastructure.Logging.Logger.Error($"[RenderSingle] Superseded check failed - page {pageIndex}", ex);
                tcs.TrySetResult(false);
            }
        }))
        {
            return false;
        }
        return await tcs.Task;
    }

    /// <summary>Returns the first/last page indices that intersect the viewport + buffer.</summary>
    private (int First, int Last) GetVisiblePageRange(double scrollY, double vpH, int bufferPages)
    {
        if (DocumentState == null) return (0, 0);

        double topLp = 0;
        int first = 0, last = DocumentState.PageCount - 1;
        bool foundFirst = false;

        for (int i = 0; i < DocumentState.PageCount; i++)
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
                last = Math.Min(DocumentState.PageCount - 1, i + bufferPages);
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
    internal double GetPageTopLp(int pageIndex)
    {
        if (_pageTopCache != null && (uint)pageIndex < (uint)_pageTopCache.Length)
            return _pageTopCache[pageIndex];

        // Fallback (should not normally be reached after InitializeViewportRendering)
        if (DocumentState == null) return 0;
        double y = 0;
        for (int i = 0; i < pageIndex; i++)
        {
            var (_, h) = GetRotatedPageSize(i);
            y += h * PtToLp + PageGapLp;
        }
        return y;
    }

}
