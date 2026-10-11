// ViewerPage.Spreadsheet.cs — Spreadsheet search using native GridCraft FFI
using Microsoft.UI.Xaml;
using QuickView.Infrastructure.Logging;
using QuickView.Rendering.Document;
using System.Collections.Generic;

namespace QuickView.Features.Viewer;

public sealed partial class ViewerPage
{
    // ── Spreadsheet search state ──────────────────────────────────────────────
    private List<SpreadsheetMatch> _spreadsheetMatches = new();
    private int _activeSpreadsheetIndex = -1;

    // ── Search implementation ─────────────────────────────────────────────────

    /// <summary>
    /// Search spreadsheet using native GridCraft search.
    /// Called from ViewerPage.Search.cs when spreadsheet viewer is active.
    /// </summary>
    internal void SearchInSpreadsheet(string query, bool matchCase)
    {
        ClearSpreadsheetSearch();

        if (string.IsNullOrEmpty(query) || _spreadsheetDocument == null)
        {
            UpdateSpreadsheetSearchCount();
            return;
        }

        // Use native GridCraft search via FFI
        _spreadsheetMatches = _spreadsheetDocument.SearchText(query, matchCase);
        _activeSpreadsheetIndex = _spreadsheetMatches.Count > 0 ? 0 : -1;

        UpdateSpreadsheetSearchCount();
        
        if (_activeSpreadsheetIndex >= 0)
            ScrollToSpreadsheetMatch(_activeSpreadsheetIndex);

        Logger.Log($"[Spreadsheet] Found {_spreadsheetMatches.Count} matches for '{query}'");
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
        ScrollToSpreadsheetMatch(_activeSpreadsheetIndex);
    }

    private void ClearSpreadsheetSearch()
    {
        _spreadsheetMatches.Clear();
        _activeSpreadsheetIndex = -1;
        if (FindCount != null) FindCount.Text = "";
    }

    private void UpdateSpreadsheetSearchCount()
    {
        if (FindCount == null) return;

        if (_spreadsheetMatches.Count == 0 || _activeSpreadsheetIndex < 0)
            FindCount.Text = _spreadsheetMatches.Count > 0 ? "No results" : "";
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

        // Switch to the sheet containing the match if necessary
        SpreadsheetViewerControl.SelectCell(match.SheetIndex, match.Row, match.Col);

        Logger.Log($"[Spreadsheet] Navigated to Sheet {match.SheetIndex + 1}, Row {match.Row + 1}, Col {match.Col + 1}");
    }
}
