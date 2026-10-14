// ViewerPage.Spreadsheet.cs — Spreadsheet search using native GridCraft FFI
using Microsoft.UI.Xaml;
using DocGlance.Infrastructure.Logging;
using DocGlance.Rendering.Document;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DocGlance.Features.Viewer;

public sealed partial class ViewerPage
{
    // ── Spreadsheet search state ──────────────────────────────────────────────
    private List<SpreadsheetMatch> _spreadsheetMatches = new();
    private int _activeSpreadsheetIndex = -1;
    private CancellationTokenSource? _spreadsheetSearchCts;
    private int _spreadsheetSearchGeneration;

    // ── Search implementation ─────────────────────────────────────────────────

    /// <summary>
    /// Search spreadsheet using native GridCraft search.
    /// Called from ViewerPage.Search.cs when spreadsheet viewer is active.
    /// </summary>
    internal async void SearchInSpreadsheet(string query, bool matchCase)
    {
        _spreadsheetSearchCts?.Cancel();
        _spreadsheetSearchCts?.Dispose();
        _spreadsheetSearchCts = new CancellationTokenSource();
        CancellationToken token = _spreadsheetSearchCts.Token;
        int generation = ++_spreadsheetSearchGeneration;

        if (string.IsNullOrEmpty(query) || _spreadsheetDocument == null)
        {
            ClearSpreadsheetSearch();
            UpdateSpreadsheetSearchCount();
            SetSearchLoading(false);
            return;
        }

        var document = _spreadsheetDocument;
        SetSearchLoading(true);
        try
        {
            await Task.Delay(200, token);
            var results = await Task.Run(
                () => document.SearchText(query, matchCase),
                token);
            if (token.IsCancellationRequested || generation != _spreadsheetSearchGeneration)
                return;

            _spreadsheetMatches = results;
            _activeSpreadsheetIndex = results.Count > 0 ? 0 : -1;
            UpdateSpreadsheetSearchCount();

            SpreadsheetViewerControl?.SetSearchMatches(_spreadsheetMatches, _activeSpreadsheetIndex);
            if (_activeSpreadsheetIndex >= 0)
                ScrollToSpreadsheetMatch(_activeSpreadsheetIndex);

            Logger.Log($"[Spreadsheet] Found {_spreadsheetMatches.Count} matches for '{query}'");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Error("[Spreadsheet] Search failed", ex);
            if (generation == _spreadsheetSearchGeneration)
                FindCount.Text = "Search failed";
        }
        finally
        {
            if (generation == _spreadsheetSearchGeneration)
                SetSearchLoading(false);
        }
    }

    /// <summary>
    /// Navigate to previous/next spreadsheet search match.
    /// Called from ViewerPage.Search.cs navigation.
    /// </summary>
    internal void NavigateSpreadsheetSearchHit(int direction)
    {
        if (_spreadsheetMatches.Count == 0) return;

        _activeSpreadsheetIndex = 
            (_activeSpreadsheetIndex + direction + _spreadsheetMatches.Count) % _spreadsheetMatches.Count;

        UpdateSpreadsheetSearchCount();
        SpreadsheetViewerControl?.SetSearchMatches(_spreadsheetMatches, _activeSpreadsheetIndex);
        ScrollToSpreadsheetMatch(_activeSpreadsheetIndex);
    }

    private void ClearSpreadsheetSearch()
    {
        _spreadsheetMatches.Clear();
        _activeSpreadsheetIndex = -1;
        SpreadsheetViewerControl?.ClearSearchMatches();
        if (FindCount != null) FindCount.Text = "";
    }

    private void UpdateSpreadsheetSearchCount()
    {
        if (FindCount == null) return;

        if (_spreadsheetMatches.Count == 0 || _activeSpreadsheetIndex < 0)
            FindCount.Text = string.IsNullOrWhiteSpace(FindBox.Text) ? "" : "No matches";
        else
            FindCount.Text = $"{_activeSpreadsheetIndex + 1} of {_spreadsheetMatches.Count}";
    }

    /// <summary>
    /// Scrolls to and selects the cell containing the search match.
    /// Uses the SpreadsheetViewer's selection system to highlight the cell.
    /// </summary>
    private void ScrollToSpreadsheetMatch(int matchIndex)
    {
        if (matchIndex < 0 || matchIndex >= _spreadsheetMatches.Count) return;
        if (SpreadsheetViewerControl == null) return;

        var match = _spreadsheetMatches[matchIndex];
        UpdateCurrentPage(match.SheetIndex + 1);

        SpreadsheetViewerControl.SelectCell(match.SheetIndex, match.Row, match.Col);

        Logger.Log($"[Spreadsheet] Navigated to Sheet {match.SheetIndex + 1}, Row {match.Row + 1}, Col {match.Col + 1}");
    }
}
