using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using QuickView.Rendering.Pdf;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;

namespace QuickView.Features.Viewer;

public sealed partial class ViewerPage
{
    // ── FitMode tracking ──────────────────────────────────────────────────────
    private enum FitMode { None, FitWidth, FitPage }
    private FitMode _fitMode = FitMode.None;
    
    // ── Zoom quality tracking ─────────────────────────────────────────────────
    // RenderScale = zoom expressed as a multiplier of 100% reading size.
    // At 100% zoom = 1.0, at 200% zoom = 2.0, etc.
    // Effective render DPI = RenderDpi × RenderScale
    private float RenderScale => (float)(_zoom / NaturalZoom);
    private CancellationTokenSource? _reRenderCts;

    // ── Zoom buttons ──────────────────────────────────────────────────────────

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
    {
        _fitMode = FitMode.None;
        FitWidthButton.IsChecked = FitPageButton.IsChecked = false;
        SetZoomAnchored(_zoom / 1.25, GetViewportCenter());
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e)
    {
        _fitMode = FitMode.None;
        FitWidthButton.IsChecked = FitPageButton.IsChecked = false;
        SetZoomAnchored(_zoom * 1.25, GetViewportCenter());
    }

    private void ZoomPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item &&
            double.TryParse(item.Tag?.ToString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double pct))
        {
            _fitMode = FitMode.None;
            FitWidthButton.IsChecked = FitPageButton.IsChecked = false;
            // pct is the user-facing percentage (0.5 = 50%, 1.0 = 100%)
            // Convert to ScrollViewer zoom: 100% reading size = NaturalZoom
            SetZoomAnchored(NaturalZoom * pct, GetViewportCenter());
        }
    }

    // ── Core zoom setter ──────────────────────────────────────────────────────

    /// <summary>
    /// Sets the ScrollViewer ZoomFactor and updates the zoom pill label.
    ///
    /// CRITICAL: Unlike our old approach, we now render pages at the CURRENT
    /// zoom level (like Chrome/Rune), not at a fixed DPI. When zoom changes,
    /// we mark visible pages as stale and re-render them at the new scale.
    /// </summary>
    internal void SetZoom(double z)
    {
        _zoom = Math.Clamp(z, DocumentScrollViewer.MinZoomFactor,
                              DocumentScrollViewer.MaxZoomFactor);
        DocumentScrollViewer.ChangeView(null, null, (float)_zoom);

        // Display percentage: 100% = NaturalZoom (reading size)
        double displayPct = Math.Round(_zoom / NaturalZoom * 100.0);
        ZoomLabel.Text = $"{displayPct}%";
        
        // Trigger re-render at new zoom level for crisp quality
        _ = ReRenderVisiblePagesAsync();
    }

    private Windows.Foundation.Point GetViewportCenter() =>
        new(DocumentScrollViewer.ViewportWidth / 2.0,
            DocumentScrollViewer.ViewportHeight / 2.0);

    private void SetZoomAnchored(double z, Windows.Foundation.Point anchor)
    {
        double oldZoom = Math.Max(0.01, DocumentScrollViewer.ZoomFactor);
        double newZoom = Math.Clamp(z, DocumentScrollViewer.MinZoomFactor,
                                       DocumentScrollViewer.MaxZoomFactor);
        if (Math.Abs(newZoom - oldZoom) < 0.0001) return;

        if (!TryCapturePageAnchor(anchor, oldZoom, out int pageIndex,
                                  out double localX, out double localY))
            return;

        // Restore the page-relative point after rebuilding the scale. Page
        // gaps and margins remain fixed, so scaling the total scroll offset
        // would accumulate error on later pages, exactly as Rune documents.
        double pageTop = GetPageContentTop(pageIndex);
        double pageLeft = PagesPanel.Margin.Left;
        double newOffsetX = Math.Max(0, (pageLeft + localX) * newZoom - anchor.X);
        double newOffsetY = Math.Max(0, (pageTop + localY) * newZoom - anchor.Y);

        _zoom = newZoom;
        _suppressScrollSync = true;
        DocumentScrollViewer.ChangeView(newOffsetX, newOffsetY, (float)newZoom,
            disableAnimation: true);
        _ = DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => _suppressScrollSync = false);
        ZoomLabel.Text = $"{Math.Round(_zoom / NaturalZoom * 100.0)}%";
        
        // Re-render at new zoom for crisp quality
        _ = ReRenderVisiblePagesAsync();
    }

    private bool TryCapturePageAnchor(Windows.Foundation.Point viewportPoint,
        double zoom, out int pageIndex, out double localX, out double localY)
    {
        pageIndex = -1;
        localX = localY = 0;

        for (int i = 0; i < PagesPanel.Children.Count; i++)
        {
            if (PagesPanel.Children[i] is not FrameworkElement page || page.ActualHeight <= 0)
                continue;
            try
            {
                var transform = page.TransformToVisual(DocumentScrollViewer);
                var topLeft = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                var bottomRight = transform.TransformPoint(
                    new Windows.Foundation.Point(page.ActualWidth, page.ActualHeight));
                if (viewportPoint.X >= topLeft.X && viewportPoint.X <= bottomRight.X &&
                    viewportPoint.Y >= topLeft.Y && viewportPoint.Y <= bottomRight.Y)
                {
                    pageIndex = i;
                    localX = (viewportPoint.X - topLeft.X) / zoom;
                    localY = (viewportPoint.Y - topLeft.Y) / zoom;
                    return true;
                }
            }
            catch (Exception) { }
        }

        // Fit operations can be requested while the current page is just
        // outside the viewport. Preserve that page's identity nonetheless.
        pageIndex = Math.Clamp(_page - 1, 0, PagesPanel.Children.Count - 1);
        if (pageIndex < 0 || pageIndex >= PagesPanel.Children.Count) return false;
        localX = 0;
        localY = 0;
        return true;
    }

    // ── Fit-Width ─────────────────────────────────────────────────────────────

    internal void ApplyFitWidth()
    {
        // Force a synchronous layout pass so ViewportWidth reflects the current
        // column geometry (sidebar open/closed, window size).
        DocumentScrollViewer.UpdateLayout();

        if (_pdfState == null) return;

        // Get the physical pixel width of the first rendered page directly from
        // the stored Tag tuple — this is always accurate even before layout has
        // fully settled (unlike img.Width which may differ from the layout size).
        double physW = 0;
        foreach (var child in PagesPanel.Children)
        {
            if (child is Border b && b.Tag is (double pw, double _) && pw > 0)
            {
                physW = pw;
                break;
            }
        }

        // Fallback: read from rendered Image if Tag isn't set yet.
        if (physW <= 0)
        {
            foreach (var child in PagesPanel.Children)
            {
                if (child is Border b2 && b2.Child is Image img && img.Width > 0)
                {
                    physW = img.Width;
                    break;
                }
            }
        }
        if (physW <= 0) return;

        // Available logical-pixel width = viewport minus left + right padding.
        // PagesPanel.Margin is 12px each side = 24 total. Add 4px extra breathing
        // room on each side (matches Rune's PageLayout.Margin = 24 strategy).
        const double sidePad = 28.0; // px per side
        double avail = DocumentScrollViewer.ViewportWidth - sidePad * 2;
        if (avail <= 0) return;

        // The ScrollViewer's ZoomFactor scales the child in its *own* coordinate
        // system, which uses the child's Width/Height as set in XAML (physical px).
        // At ZoomFactor = z, a child of width physW occupies physW * z *screen*
        // pixels, but those screen pixels map to logical pixels at rate 1:1 from
        // the ScrollViewer's perspective (the ScrollViewer measures viewport in
        // logical px = screen px / DisplayScale, and the child's Width is already
        // in that same logical px space because WinUI XAML uses logical px).
        //
        // Wait — actually in WinUI 3 Image.Width / img.Width is set in XAML
        // logical pixels (DIPs).  We set it to the physical pixel count, which
        // means the Image is enormously wide in logical pixels.  ZoomFactor then
        // scales that enormous logical width down to reading size via NaturalZoom.
        //
        // So the correct formula is:
        //   displayed_logical_width = physW * ZoomFactor
        //   we want:  physW * zoom = avail
        //   therefore: zoom = avail / physW
        //
        // NaturalZoom (= 96/384 = 0.25) is already baked into that: avail / physW
        // will naturally be ≈ NaturalZoom when avail ≈ the page's natural width.

        SetZoomAnchored(avail / physW, GetViewportCenter());
    }

    // ── Fit-Page ──────────────────────────────────────────────────────────────

    internal void ApplyFitPage()
    {
        DocumentScrollViewer.UpdateLayout();

        double physW = 0, physH = 0;
        foreach (var child in PagesPanel.Children)
        {
            if (child is Border b && b.Tag is (double pw, double ph) && pw > 0)
            {
                physW = pw; physH = ph;
                break;
            }
        }
        if (physW <= 0 || physH <= 0)
        {
            foreach (var child in PagesPanel.Children)
            {
                if (child is Border b2 && b2.Child is Image img && img.Width > 0)
                {
                    physW = img.Width; physH = img.Height;
                    break;
                }
            }
        }
        if (physW <= 0 || physH <= 0) return;

        const double sidePad = 28.0;
        const double topPad  = 16.0;
        double aw = DocumentScrollViewer.ViewportWidth  - sidePad * 2;
        double ah = DocumentScrollViewer.ViewportHeight - topPad  * 2;
        if (aw <= 0 || ah <= 0) return;

        SetZoomAnchored(Math.Min(aw / physW, ah / physH), GetViewportCenter());
    }

    // ── Re-apply active fit on viewport change ────────────────────────────────

    internal void ReapplyFitMode()
    {
        switch (_fitMode)
        {
            case FitMode.FitWidth: ApplyFitWidth(); break;
            case FitMode.FitPage:  ApplyFitPage();  break;
        }
    }

    // ── Ctrl+Wheel zoom ───────────────────────────────────────────────────────

    internal void OnViewerPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(DocumentScrollViewer);
        bool ctrl = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (!ctrl || point.Properties.MouseWheelDelta == 0) return;

        _fitMode = FitMode.None;
        FitWidthButton.IsChecked = FitPageButton.IsChecked = false;

        // One notch = 120; multiplicative so each step feels the same at any zoom.
        double steps  = point.Properties.MouseWheelDelta / 120.0;
        double factor = Math.Pow(1.1, steps);

        // Anchor the zoom at the cursor position so the content under the
        // pointer stays stationary — same technique as Rune's SetZoom(anchor).
        double newZoom = Math.Clamp(_zoom * factor,
                                    DocumentScrollViewer.MinZoomFactor,
                                    DocumentScrollViewer.MaxZoomFactor);

        SetZoomAnchored(newZoom, point.Position);

        e.Handled = true;
    }

    // ── Re-render at new zoom for crisp quality (Rune's approach) ────────────
    
    /// <summary>
    /// Re-renders visible pages at the current zoom level for crisp quality.
    /// Key insight: keep page layout dimensions (physW×physH) the same so the
    /// ScrollViewer position never jumps. Render a higher-res bitmap and use
    /// Stretch.Fill to fill the same slot — no blurry upscaling.
    ///
    /// Effective render DPI = RenderDpi × (zoom / NaturalZoom)
    /// At 100% zoom this equals RenderDpi (192). At 200% this equals 384.
    /// Chrome does the same thing: re-rasterizes at the actual screen DPI.
    /// </summary>
    private async Task ReRenderVisiblePagesAsync()
    {
        if (_pdfState == null || _cts == null) return;
        
        // Cancel any in-flight re-render
        _reRenderCts?.Cancel();
        _reRenderCts?.Dispose();
        _reRenderCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        var ct = _reRenderCts.Token;

        // Small delay to coalesce rapid zoom steps (wheel spin)
        await Task.Delay(80, ct).ContinueWith(_ => { }, TaskContinuationOptions.None);
        if (ct.IsCancellationRequested) return;

        var visiblePages = GetVisiblePageIndices();
        if (visiblePages.Count == 0) return;
        
        // Effective DPI = base DPI × zoom factor expressed as multiple of 100%
        // NaturalZoom maps RenderDpi → 100% reading size, so:
        //   effectiveDpi = RenderDpi × (_zoom / NaturalZoom)
        float effectiveDpi = PdfRenderer.RenderDpi * (float)(_zoom / NaturalZoom);
        // H3: Cap at 2.5× physical display DPI (not a fixed 768)
        // On 96 DPI display: max 240 DPI. On 192 DPI (200% scaling): max 480 DPI
        // This prevents excessive memory while respecting high-DPI displays
        float maxDpi = DisplayScale * 96f * 2.5f;
        effectiveDpi = Math.Min(effectiveDpi, maxDpi);

        foreach (int pageIdx in visiblePages)
        {
            if (ct.IsCancellationRequested) return;
            
            var (ptW, ptH) = _pdfState.GetPageSize(pageIdx);
            
            // Layout dimensions stay constant — same as initial render at RenderDpi
            double physW = Math.Round(ptW * PdfRenderer.RenderDpi / 96.0);
            double physH = Math.Round(ptH * PdfRenderer.RenderDpi / 96.0);

            // Render at the higher effective DPI
            var bmp = await PdfRenderer.RenderPageAsync(_pdfState, pageIdx, effectiveDpi, ct);
            if (bmp == null || ct.IsCancellationRequested) return;
            
            // Capture for closure
            int idx = pageIdx;
            double pw = physW, ph = physH;

            DispatcherQueue.TryEnqueue(() =>
            {
                if (_pdfState == null) return;
                if (idx >= PagesPanel.Children.Count) return;
                if (PagesPanel.Children[idx] is not Border border) return;
                if (border.Child is not Grid grid) return;
                
                // Replace only the Image source; keep layout size (physW×physH) fixed
                // so the ScrollViewer offset doesn't move.
                if (grid.Children.Count > 0 && grid.Children[0] is Image img)
                {
                    // Stretch.Fill: fills the physW×physH slot with the new high-res bitmap
                    img.Source  = bmp;
                    img.Width   = pw;
                    img.Height  = ph;
                    img.Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill;
                }
                else
                {
                    // Slot had a placeholder — build the full grid
                    grid.Width  = pw;
                    grid.Height = ph;
                    grid.Children.Clear();
                    grid.Children.Add(new Image
                    {
                        Source  = bmp,
                        Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill,
                        Width   = pw,
                        Height  = ph,
                    });
                    grid.Children.Add(CreateHighlightCanvas(idx, pw, ph));
                }
                DrawHighlights(idx);
            });
        }
    }
    
    private List<int> GetVisiblePageIndices()
    {
        var visible = new List<int>();
        
        for (int i = 0; i < PagesPanel.Children.Count; i++)
        {
            if (PagesPanel.Children[i] is not FrameworkElement page) continue;
            
            try
            {
                var transform = page.TransformToVisual(DocumentScrollViewer);
                var topLeft   = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                
                if (topLeft.Y < DocumentScrollViewer.ViewportHeight &&
                    topLeft.Y + page.ActualHeight > 0)
                {
                    visible.Add(i);
                }
            }
            catch { }
        }
        
        return visible;
    }
}
