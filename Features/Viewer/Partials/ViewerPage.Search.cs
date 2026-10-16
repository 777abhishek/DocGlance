using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using DocGlance.Core.Models;
using DocGlance.Rendering.Docx;
using DocGlance.Rendering.Xlsx;
using DocGlance.Infrastructure.Logging;
using DocGlance.Features.Viewer.Formats.Text;
using DocGlance.Features.Viewer.Contracts;
using DocGlance.Features.Viewer.Formats.Spreadsheet;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;

namespace DocGlance.Features.Viewer;

public sealed partial class ViewerPage
{
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _searchDebounceCts;
    private SearchResult[] _activeViewerSearchResults = Array.Empty<SearchResult>();
    private int _activeViewerSearchIndex = -1;

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

        if (_activeViewer is ISupportsSearch viewerSearch)
        {
            bool matchCase = MatchCaseButton?.IsChecked ?? false;
            await SearchInViewer(viewerSearch, query, matchCase);
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

        // 300 ms debounce — prevents hammering the native text layer on every keystroke
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
            SetSearchLoading(false);
            return;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            DocGlance.Infrastructure.Logging.Logger.Error("Document search failed", ex);
            FindCount.Text = "Search failed";
        }
        finally { SetSearchLoading(false); }
    }

    // ── Clear ─────────────────────────────────────────────────────────────────

    private void ClearSearchHighlights()
    {
        if (_activeViewer is ISupportsSearch viewerSearch)
            viewerSearch.ClearHighlight();
        _activeViewerSearchResults = Array.Empty<SearchResult>();
        _activeViewerSearchIndex = -1;
        FindCount.Text     = "";
        DrawAllHighlights();
    }

    private async Task SearchInViewer(ISupportsSearch viewer, string query, bool matchCase)
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        _searchDebounceCts = new CancellationTokenSource();
        CancellationToken debounceToken = _searchDebounceCts.Token;

        if (string.IsNullOrEmpty(query))
        {
            _searchCts?.Cancel();
            ClearSearchHighlights();
            SetSearchLoading(false);
            return;
        }

        try
        {
            await Task.Delay(200, debounceToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        CancellationToken cancellationToken = _searchCts.Token;
        if (viewer is TextViewer textViewer)
            textViewer.MatchCase = matchCase;
        else if (viewer is SpreadsheetViewer spreadsheetViewer)
            spreadsheetViewer.MatchCase = matchCase;
        else if (viewer is Formats.Pdf.PdfViewer pdfViewer)
            pdfViewer.MatchCase = matchCase;
        else if (viewer is Formats.Docx.DocxViewer docxViewer)
            docxViewer.MatchCase = matchCase;
        else if (viewer is Formats.Pptx.PptxViewer pptxViewer)
            pptxViewer.MatchCase = matchCase;

        ClearSearchHighlights();
        SetSearchLoading(true);
        try
        {
            SearchResult[] results = await viewer.SearchAsync(query, cancellationToken);
            if (cancellationToken.IsCancellationRequested || !ReferenceEquals(_activeViewer, viewer))
                return;

            _activeViewerSearchResults = results;
            _activeViewerSearchIndex = results.Length > 0 ? 0 : -1;
            if (_activeViewerSearchIndex >= 0)
                viewer.Highlight(results[_activeViewerSearchIndex]);
            FindCount.Text = results.Length == 0
                ? "No matches"
                : $"1 of {results.Length}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Error("Viewer search failed", ex);
            if (ReferenceEquals(_activeViewer, viewer))
                FindCount.Text = "Search failed";
        }
        finally
        {
            if (ReferenceEquals(_activeViewer, viewer))
                SetSearchLoading(false);
        }
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
        PdfViewerControl.RefreshHighlights();
    }

    // ── Navigate Prev / Next ──────────────────────────────────────────────────

    private void NavigateSearchHit(int direction)
    {
        if (_activeViewer is ISupportsSearch viewerSearch && _activeViewerSearchResults.Length > 0)
        {
            _activeViewerSearchIndex =
                (_activeViewerSearchIndex + direction + _activeViewerSearchResults.Length)
                % _activeViewerSearchResults.Length;
            viewerSearch.Highlight(_activeViewerSearchResults[_activeViewerSearchIndex]);
            FindCount.Text = $"{_activeViewerSearchIndex + 1} of {_activeViewerSearchResults.Length}";
            return;
        }

    }

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

        if (_docxDocument != null)
        {
            string docxText = _docxDocument.ExtractPageText(_page - 1);
            if (docxText.Length == 0) return;

            var docxPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            docxPackage.SetText(docxText);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(docxPackage);
            e.Handled = true;
            return;
        }

        if (IsSpreadsheetViewerActive() && _spreadsheetDocument != null)
        {
            string sheetText = _spreadsheetDocument.ExtractSheetText(
                SpreadsheetViewerControl.ActiveSheetIndex);
            if (sheetText.Length == 0) return;

            var sheetPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            sheetPackage.SetText(sheetText);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(sheetPackage);
            e.Handled = true;
            return;
        }

        if (_pdfState == null) return;
    }

    // ── Helper: true when spreadsheet viewer is active ────────────────────────

    private bool IsSpreadsheetViewerActive() =>
        SpreadsheetViewerHost?.Visibility == Visibility.Visible;
}
