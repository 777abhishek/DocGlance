using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using QuickView.Core.Models;
using QuickView.Rendering.Pdf;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;

namespace QuickView.Features.Viewer;

public sealed partial class ViewerPage
{
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _searchDebounceCts;
    private IReadOnlyList<PdfSearchResult> _searchResults = Array.Empty<PdfSearchResult>();
    private int _activeSearchIndex = -1;

    // ── Canvas overlay creation ───────────────────────────────────────────────

    /// <summary>
    /// Creates a transparent, non-hit-testable Canvas that sits on top of the
    /// page image inside the per-page Grid. Tag stores the page index so
    /// DrawHighlights can locate it by index.
    /// </summary>
    internal Canvas CreateHighlightCanvas(int pageIndex, double width, double height) => new()
    {
        Width            = width,
        Height           = height,
        IsHitTestVisible = false,
        Tag              = pageIndex,
    };

    // ── Find-box text changed → debounced search ──────────────────────────────

    private async void FindBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string query = FindBox.Text.Trim();

        // Route to text/code viewer search if either viewer is active
        if (IsTextViewerActive())
        {
            bool matchCase = MatchCaseButton?.IsChecked ?? false;
            SearchInTextViewer(query, matchCase);
            return;
        }

        // Cancel any in-flight search and pending debounce
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        _searchDebounceCts = new CancellationTokenSource();
        var debounceCt = _searchDebounceCts.Token;

        // Clear immediately when query is empty
        if (query.Length == 0)
        {
            _searchCts?.Cancel();
            ClearSearchHighlights();
            SetSearchLoading(false);
            return;
        }

        // 300 ms debounce — prevents hammering PDFium on every keystroke
        try { await Task.Delay(300, debounceCt); }
        catch (OperationCanceledException) { return; }

        // Debounce passed — start the real search
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        ClearSearchHighlights();
        SetSearchLoading(true);

        try
        {
            bool caseSensitive = MatchCaseButton?.IsChecked == true;
            if (_pdfState == null) { SetSearchLoading(false); return; }
            var results = await PdfTextService.SearchAsync(_pdfState, query, caseSensitive, ct);
            if (ct.IsCancellationRequested) return;

            _searchResults     = results;
            _activeSearchIndex = results.Count > 0 ? 0 : -1;

            UpdateFindCount();
            DrawAllHighlights();

            if (_activeSearchIndex >= 0)
                ScrollToHit(_activeSearchIndex);
        }
        catch (OperationCanceledException) { }
        catch { FindCount.Text = ""; }
        finally { SetSearchLoading(false); }
    }

    // ── Clear ─────────────────────────────────────────────────────────────────

    private void ClearSearchHighlights()
    {
        _searchResults     = Array.Empty<PdfSearchResult>();
        _activeSearchIndex = -1;
        FindCount.Text     = "";
        DrawAllHighlights();
    }

    // ── Loading indicator ─────────────────────────────────────────────────────

    private void SetSearchLoading(bool loading)
    {
        if (FindSpinner == null) return;
        FindSpinner.IsActive   = loading;
        FindSpinner.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    private void DrawAllHighlights()
    {
        for (int i = 0; i < PagesPanel.Children.Count; i++)
            DrawHighlights(i);
    }

    /// <summary>
    /// Redraws search-highlight borders on the overlay Canvas for one page.
    ///
    /// Coordinate maths (matches Rune's HighlightRect):
    ///   PdfSearchRect.X / Y are already in top-left-origin page points
    ///   (1 pt = 1 px at 72 DPI), produced by FPDF_PageToDevice in PdfTextService.
    ///   scale = RenderDpi / 96.0 converts them to the physical-pixel space the
    ///   Canvas lives in (Canvas.Width == physW, Canvas.Height == physH).
    ///   No Y-flip is needed because the origin is already top-left.
    /// </summary>
    internal void DrawHighlights(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= PagesPanel.Children.Count) return;
        if (PagesPanel.Children[pageIndex] is not Border border) return;

        Canvas? canvas = FindHighlightCanvas(border);
        if (canvas == null) return;

        canvas.Children.Clear();

        // Rects from PdfTextService are ALREADY in physical pixels at RenderDpi (384),
        // matching Canvas size exactly. Place directly without scaling.

        for (int resultIndex = 0; resultIndex < _searchResults.Count; resultIndex++)
        {
            var result = _searchResults[resultIndex];
            if (result.PageIndex != pageIndex) continue;

            // Active hit: match by CharIndex (stable identity), falling back to
            // list position — mirrors Rune's ReferenceEquals + CharIndex check.
            bool active = resultIndex == _activeSearchIndex ||
                          (_activeSearchIndex >= 0 &&
                           _activeSearchIndex < _searchResults.Count &&
                           _searchResults[_activeSearchIndex].PageIndex == result.PageIndex &&
                           _searchResults[_activeSearchIndex].CharIndex == result.CharIndex);

            foreach (var rect in result.Rects)
            {
                // rect.X/Y/Width/Height are already in physical pixels
                var highlight = new Border
                {
                    Width         = Math.Max(1, rect.Width),
                    Height        = Math.Max(1, rect.Height),
                    CornerRadius  = new CornerRadius(2),
                    Background    = new SolidColorBrush(active
                        ? Color.FromArgb(160, 255, 140, 0)   // orange — active hit
                        : Color.FromArgb(100, 255, 220, 0)), // yellow — passive hit
                    BorderBrush   = new SolidColorBrush(active
                        ? Color.FromArgb(230, 210, 90, 0)
                        : Color.FromArgb(140, 200, 180, 0)),
                    BorderThickness = new Thickness(1),
                };
                Canvas.SetLeft(highlight, rect.X);
                Canvas.SetTop (highlight, rect.Y);
                canvas.Children.Add(highlight);
            }
        }
    }

    /// <summary>
    /// Finds the highlight Canvas in a page border, regardless of whether
    /// the page contains a plain Image (not yet rendered) or a Grid+Image+Canvas
    /// (already rendered). Searches the child tree one level deep.
    /// </summary>
    private static Canvas? FindHighlightCanvas(Border border)
    {
        // Rendered pages: border.Child is Grid containing Image + Canvas
        if (border.Child is Grid grid)
        {
            foreach (var child in grid.Children)
                if (child is Canvas c) return c;
        }
        // Placeholder pages: border.Child is Grid with ProgressRing (no Canvas yet)
        return null;
    }

    // ── Navigate Prev / Next ──────────────────────────────────────────────────

    private void NavigateSearchHit(int direction)
    {
        // Route to text/code viewer navigation if either viewer is active
        if (IsTextViewerActive())
        {
            NavigateTextSearchHit(direction);
            return;
        }

        if (_searchResults.Count == 0) return;

        _activeSearchIndex =
            (_activeSearchIndex + direction + _searchResults.Count) % _searchResults.Count;

        UpdateFindCount();
        DrawAllHighlights();
        ScrollToHit(_activeSearchIndex);
    }

    /// <summary>
    /// Scrolls so the active hit rect is centred vertically in the viewport —
    /// matches Rune's HighlightHit which centres on hitTop rather than jumping
    /// to the page top (important for long/multi-column pages).
    /// </summary>
    private void ScrollToHit(int hitIndex)
    {
        if (hitIndex < 0 || hitIndex >= _searchResults.Count) return;

        var hit = _searchResults[hitIndex];
        if (hit.Rects.Count == 0)
        {
            // No rect info — fall back to scrolling to the page top.
            GoToPage(hit.PageIndex + 1);
            return;
        }

        // Rects are already in physical pixels, so convert directly to content coords.
        // No scale multiplication needed.
        double pageTop    = GetPageContentTop(hit.PageIndex);
        double hitOffsetY = hit.Rects[0].Y;  // already in physical pixels

        // Content-space Y of the hit's top edge
        double hitContentY = pageTop + hitOffsetY;

        // Centre the hit vertically in the viewport
        double zoom       = Math.Max(0.01, DocumentScrollViewer.ZoomFactor);
        double viewportH  = DocumentScrollViewer.ViewportHeight;
        double targetOffset = Math.Max(0, hitContentY * zoom - viewportH / 2);

        _suppressScrollSync = true;
        DocumentScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: true);
        _ = DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => _suppressScrollSync = false);

        // Keep thumbnail + page number in sync.
        UpdateCurrentPage(hit.PageIndex + 1);
        PrefetchPagesAround(hit.PageIndex);
    }

    // ── Count label ───────────────────────────────────────────────────────────

    private void UpdateFindCount() =>
        FindCount.Text = _activeSearchIndex < 0
            ? (_searchResults.Count > 0 ? "No matches" : "")
            : $"{_activeSearchIndex + 1} of {_searchResults.Count}";

    // ── Match-case toggle re-triggers search ──────────────────────────────────

    private void MatchCaseButton_Click(object sender, RoutedEventArgs e)
    {
        // Re-run search with the new case setting (simulate text change)
        if (FindBox.Text.Trim().Length > 0)
            FindBox_TextChanged(sender, null!);
    }

    // ── Ctrl+C on the scroll viewer → copy current page text ─────────────────

    private async void DocumentScrollViewer_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.C) return;

        var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
            Windows.System.VirtualKey.Control);
        if ((state & Windows.UI.Core.CoreVirtualKeyStates.Down) == 0 ||
            _doc?.FilePath == null)
            return;

        if (_pdfState == null) return;
        string text = await PdfTextService.ExtractTextAsync(_pdfState, _page - 1);
        if (text.Length == 0) return;

        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        e.Handled = true;
    }

    // ── Helper: true when either text viewer (plain or code) is active ────────

    private bool IsTextViewerActive() =>
        (PlainTextViewerGrid?.Visibility == Visibility.Visible) ||
        (TextViewerScroll?.Visibility    == Visibility.Visible);
}
