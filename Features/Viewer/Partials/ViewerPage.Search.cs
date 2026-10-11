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

        // Route to spreadsheet viewer search if active
        if (IsSpreadsheetViewerActive())
        {
            bool matchCase = MatchCaseButton?.IsChecked ?? false;
            SearchInSpreadsheet(query, matchCase);
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
        // VIEWPORT ARCHITECTURE: highlights are anchored in canvas coordinates
        // (canvas lp = pt × 96/72 × zoom), so they scroll with the content.
        if (SearchHighlightCanvas == null || _viewportState == null || _pdfState == null) return;

        SearchHighlightCanvas.Children.Clear();

        // Rects are in unrotated page space — skip rather than draw misplaced.
        if (_rotation != 0) return;

        var visiblePages = _viewportState.GetVisiblePages(_pdfState);

        for (int resultIndex = 0; resultIndex < _searchResults.Count; resultIndex++)
        {
            var result = _searchResults[resultIndex];

            // Skip results not in visible pages
            bool isVisible = false;
            foreach (var (pageIndex, _, _) in visiblePages)
            {
                if (pageIndex == result.PageIndex)
                {
                    isVisible = true;
                    break;
                }
            }
            if (!isVisible) continue;

            // Active hit: match by CharIndex (stable identity), falling back to list position
            bool active = resultIndex == _activeSearchIndex ||
                          (_activeSearchIndex >= 0 &&
                           _activeSearchIndex < _searchResults.Count &&
                           _searchResults[_activeSearchIndex].PageIndex == result.PageIndex &&
                           _searchResults[_activeSearchIndex].CharIndex == result.CharIndex);

            // Get page position in document to offset highlight coordinates
            double pageOffsetY = GetPageOffsetInDocument(result.PageIndex);

            double zoom = _zoom;
            foreach (var rect in result.Rects)
            {
                // PDF points → canvas layout pixels (× zoom, canvas is zoom-scaled)
                double canvasX = rect.X * PtToLp * zoom;
                double canvasY = (pageOffsetY + rect.Y) * PtToLp * zoom;

                var highlight = new Border
                {
                    Width         = Math.Max(1, rect.Width  * PtToLp * zoom),
                    Height        = Math.Max(1, rect.Height * PtToLp * zoom),
                    CornerRadius  = new CornerRadius(2),
                    Background    = new SolidColorBrush(active
                        ? Color.FromArgb(160, 255, 140, 0)   // orange — active hit
                        : Color.FromArgb(100, 255, 220, 0)), // yellow — passive hit
                    BorderBrush   = new SolidColorBrush(active
                        ? Color.FromArgb(230, 210, 90, 0)
                        : Color.FromArgb(140, 200, 180, 0)),
                    BorderThickness = new Thickness(1),
                };
                Canvas.SetLeft(highlight, canvasX);
                Canvas.SetTop(highlight, canvasY);
                SearchHighlightCanvas.Children.Add(highlight);
            }
        }
    }

    /// <summary>
    /// Gets the Y offset of a page within the entire document in PDF points.
    /// </summary>
    private double GetPageOffsetInDocument(int pageIndex)
    {
        if (_pdfState == null || pageIndex <= 0) return 0;

        const double PageGapPt = 12.0 * 72.0 / 96.0;  // gap converted to PDF points

        double offset = 0;
        for (int i = 0; i < pageIndex && i < _pdfState.PageCount; i++)
        {
            var (_, pageHeight) = _pdfState.GetPageSize(i);
            offset += pageHeight + PageGapPt;
        }
        return offset;
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

        // Route to spreadsheet navigation if active
        if (IsSpreadsheetViewerActive())
        {
            NavigateSpreadsheetSearchHit(direction);
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
    /// Scrolls so the active hit rect is centred vertically in the viewport.
    /// VIEWPORT ARCHITECTURE: Calculate page offset and scroll to position.
    /// </summary>
    private void ScrollToHit(int hitIndex)
    {
        if (hitIndex < 0 || hitIndex >= _searchResults.Count) return;

        var hit = _searchResults[hitIndex];
        if (hit.Rects.Count == 0)
        {
            ScrollToPage(hit.PageIndex + 1);
            return;
        }

        const double PtToLp   = 96.0 / 72.0;

        // Page top + hit position in canvas layout pixels (canvas is zoom-scaled)
        double pageTopLp  = GetPageOffsetInDocument(hit.PageIndex) * PtToLp;
        double hitOffsetLp = hit.Rects[0].Y * PtToLp;   // rect coords are in PDF points
        double hitContentLp = (pageTopLp + hitOffsetLp) * _zoom;

        double viewportH  = DocumentScrollViewer.ViewportHeight;
        double targetOffset = Math.Max(0, hitContentLp - viewportH / 2);

        DocumentScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: true);

        UpdateCurrentPage(hit.PageIndex + 1);
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

    // ── Helper: true when spreadsheet viewer is active ────────────────────────

    private bool IsSpreadsheetViewerActive() =>
        SpreadsheetViewerHost?.Visibility == Visibility.Visible;
}
