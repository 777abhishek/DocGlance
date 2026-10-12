// ViewerPage.Text.cs — Plain text, code, and markdown viewers
// Search highlighting uses the shared SearchHighlighter engine (same visual as PDF).
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using QuickView.Infrastructure.Logging;
using QuickView.Features.Viewer.Components;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Features.Viewer;

public sealed partial class ViewerPage
{
    // ── Extension sets ────────────────────────────────────────────────────────
    // ALL text-based files use the same lightweight TextBox viewer (no syntax highlighting)
    // Simple, fast, memory-efficient — like Notepad

    private static readonly HashSet<string> _textExts = new(StringComparer.OrdinalIgnoreCase)
    {
        // Plain text
        ".txt", ".log", ".csv", ".tsv", ".ini", ".cfg", ".conf",
        ".env", ".toml", ".yaml", ".yml", ".properties", ".gitignore",
        ".editorconfig", ".htaccess",
        ".md", ".markdown", ".mdx",
        // Code files (no syntax highlighting - keeps it simple and fast)
        ".cs", ".vb", ".fs", ".csx",
        ".js", ".mjs", ".cjs", ".jsx",
        ".ts", ".tsx",
        ".py", ".pyw",
        ".java", ".kt", ".kts",
        ".cpp", ".c", ".h", ".hpp", ".cc",
        ".go", ".rs", ".swift",
        ".rb", ".php", ".pl",
        ".sh", ".bash", ".zsh", ".ps1", ".psm1", ".bat", ".cmd",
        ".html", ".htm", ".cshtml", ".razor",
        ".css", ".scss", ".less",
        ".json", ".jsonc", ".json5",
        ".xml", ".xaml", ".svg", ".csproj", ".sln", ".props", ".targets",
        ".sql", ".graphql", ".gql",
        ".dockerfile", ".makefile",
        ".r", ".lua", ".dart", ".ex", ".exs",
    };

    internal static bool IsTextFile(string ext) => _textExts.Contains(ext);
    internal static bool IsCodeFile(string ext) => false;  // No longer using separate code viewer

    // ── State ─────────────────────────────────────────────────────────────────

    private string? _currentTextFilePath;

    // Shared search state — one instance per active query, replaced on new search
    private SearchState? _textSearchState;

    // ── Unified text viewer (TextBox - lightweight like Notepad) ─────────────
    // Uses a single TextBox.Text — no per-line XAML objects.
    // RAM cost: ~2 bytes/char → ~2MB for a 1MB file regardless of line count.
    // Fast, simple, memory-efficient for all text files including code.

    private async Task LoadTextFileAsync(string filePath, CancellationToken ct)
    {
        ShowLoading(true);
        try
        {
            var loaded = await Task.Run(
                () => Rendering.Text.TextFileReader.ReadCapped(filePath, 5_000_000), ct);  // Increased to 5MB cap

            if (ct.IsCancellationRequested) return;

            string raw = loaded.Text;
            int lineCount = loaded.LineCount;
            if (loaded.Truncated)
            {
                raw += $"\n\n--- Truncated: showing first 5 MB of {loaded.FileBytes / 1_000_000.0:F1} MB ---";
                lineCount += 2;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    // Single string assignment — no per-line objects, minimal RAM
                    PlainTextBlock.FontFamily   = new FontFamily("Cascadia Code, Cascadia Mono, Consolas, Courier New");
                    PlainTextBlock.FontSize     = 13;
                    PlainTextBlock.TextWrapping = TextWrapping.NoWrap;
                    PlainTextBlock.IsReadOnly   = true;
                    PlainTextBlock.AcceptsReturn = true;
                    PlainTextBlock.Text         = raw;

                    LineNumberBlock.FontFamily  = new FontFamily("Cascadia Code, Cascadia Mono, Consolas, Courier New");
                    LineNumberBlock.FontSize    = 13;
                    LineNumberBlock.LineHeight  = 22;
                    GeneratePlainLineNumbers(lineCount);

                    _currentTextFilePath = filePath;
                    Toolbar.IsWordWrapChecked = false;
                    ClearTextHighlights();

                    ShowPlainViewer();
                    Logger.Log($"[TextViewer] {lineCount:N0} lines, {loaded.FileBytes / 1024.0:F1} KB");
                }
                catch (Exception ex)
                {
                    Logger.Log($"[TextViewer] UI error: {ex.Message}");
                    ShowErrorCard($"Cannot display file\n{ex.Message}");
                }
                finally { ShowLoading(false); }
            });
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Logger.Log($"[TextViewer] Error: {ex.Message}");
            ShowErrorCard($"Cannot open file\n{ex.Message}");
            ShowLoading(false);
        }
    }


    // ── Visibility helpers ────────────────────────────────────────────────────

    private void ShowPlainViewer()
    {
        PlainTextViewerGrid.Visibility  = Visibility.Visible;
        TextViewerScroll.Visibility     = Visibility.Collapsed;
        DocumentScrollViewer.Visibility = Visibility.Collapsed;
        MarkdownViewerHost.Visibility   = Visibility.Collapsed;
        SpreadsheetViewerHost.Visibility = Visibility.Collapsed;
        ZoomPill.Visibility             = Visibility.Collapsed;

        Toolbar.WordWrapVisibility  = Visibility.Visible;
        Toolbar.SidebarButtonVisibility = Visibility.Collapsed;
        SidebarCard.Visibility     = Visibility.Collapsed;
        SidebarColumn.Width        = new GridLength(0);
        Toolbar.FitWidthButtonVisibility = Visibility.Collapsed;
        Toolbar.FitPageButtonVisibility = Visibility.Collapsed;
    }

    private void ShowMarkdownViewer()
    {
        PlainTextViewerGrid.Visibility  = Visibility.Collapsed;
        TextViewerScroll.Visibility     = Visibility.Collapsed;
        DocumentScrollViewer.Visibility = Visibility.Collapsed;
        MarkdownViewerHost.Visibility   = Visibility.Visible;
        ZoomPill.Visibility             = Visibility.Collapsed;

        Toolbar.WordWrapVisibility  = Visibility.Collapsed;
        Toolbar.SidebarButtonVisibility = Visibility.Collapsed;
        SidebarCard.Visibility     = Visibility.Collapsed;
        SidebarColumn.Width        = new GridLength(0);
        Toolbar.FitWidthButtonVisibility = Visibility.Collapsed;
        Toolbar.FitPageButtonVisibility = Visibility.Collapsed;
    }

    internal void ShowPdfViewer()
    {
        PlainTextViewerGrid.Visibility   = Visibility.Collapsed;
        TextViewerScroll.Visibility      = Visibility.Collapsed;
        DocumentScrollViewer.Visibility  = Visibility.Visible;
        MarkdownViewerHost.Visibility    = Visibility.Collapsed;
        SpreadsheetViewerHost.Visibility = Visibility.Collapsed;
        ZoomPill.Visibility              = Visibility.Visible;

        Toolbar.WordWrapVisibility       = Visibility.Collapsed;
        Toolbar.SidebarButtonVisibility  = Visibility.Visible;
        Toolbar.FitWidthButtonVisibility = Visibility.Visible;
        Toolbar.FitPageButtonVisibility  = Visibility.Visible;
    }

    internal void ShowSpreadsheetViewer()
    {
        PlainTextViewerGrid.Visibility   = Visibility.Collapsed;
        TextViewerScroll.Visibility      = Visibility.Collapsed;
        DocumentScrollViewer.Visibility  = Visibility.Collapsed;
        MarkdownViewerHost.Visibility    = Visibility.Collapsed;
        SpreadsheetViewerHost.Visibility = Visibility.Visible;
        ZoomPill.Visibility              = Visibility.Collapsed;

        Toolbar.WordWrapVisibility       = Visibility.Collapsed;
        Toolbar.SidebarButtonVisibility  = Visibility.Collapsed;
        Toolbar.FitWidthButtonVisibility = Visibility.Visible;
        Toolbar.FitPageButtonVisibility  = Visibility.Visible;
        SidebarCard.Visibility           = Visibility.Collapsed;
        SidebarColumn.Width              = new GridLength(0);
        ViewModel.SidebarOpen            = false;
        ZoomPill.Visibility              = Visibility.Visible;
    }

    private bool IsDarkTheme() =>
        ActualTheme == ElementTheme.Dark ||
        (ActualTheme == ElementTheme.Default &&
         Application.Current.RequestedTheme == ApplicationTheme.Dark);

    // ── Line number generation ────────────────────────────────────────────────

    private void GeneratePlainLineNumbers(int count)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 1; i <= count; i++) { if (i > 1) sb.Append('\n'); sb.Append(i); }
        LineNumberBlock.Text = sb.ToString();
    }

    // ── Word wrap ─────────────────────────────────────────────────────────────

    private void WordWrapButton_Click(object? sender, EventArgs e)
    {
        bool wrap = Toolbar.IsWordWrapChecked;
        PlainTextBlock.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
    }

    // ── Save ──────────────────────────────────────────────────────────────────
    // Removed - not needed for read-only viewer

    // ── Search entry points (called from ViewerPage.Search.cs) ───────────────

    /// <summary>Build matches and draw highlights. Called on every keystroke in FindBox.</summary>
    internal void SearchInTextViewer(string query, bool matchCase)
    {
        ClearTextHighlights();
        if (string.IsNullOrEmpty(query))
        {
            UpdateTextSearchCount();
            return;
        }

        _textSearchState = SearchHighlighter.FindInText(PlainTextBlock.Text, query, matchCase);
        if (_textSearchState.HasHits)
            SearchHighlighter.ApplyToTextBox(_textSearchState, PlainTextBlock);

        UpdateTextSearchCount();
        Logger.Log($"[Search] {_textSearchState.Count} matches for '{query}'");
    }

    /// <summary>Step prev/next through matches.</summary>
    internal void NavigateTextSearchHit(int direction)
    {
        if (_textSearchState == null || !_textSearchState.HasHits) return;
        _textSearchState.Navigate(direction);

        SearchHighlighter.ApplyToTextBox(_textSearchState, PlainTextBlock);
        UpdateTextSearchCount();
    }

    private void ClearTextHighlights()
    {
        _textSearchState = null;
        if (PlainTextBlock != null) PlainTextBlock.SelectionLength = 0;
        if (FindCount != null) FindCount.Text = "";
    }

    // ── Count label ───────────────────────────────────────────────────────────

    private void UpdateTextSearchCount()
    {
        if (FindCount == null) return;
        if (_textSearchState == null || string.IsNullOrEmpty(_textSearchState.Query))
            FindCount.Text = "";
        else if (!_textSearchState.HasHits)
            FindCount.Text = "No results";
        else
            FindCount.Text = $"{_textSearchState.ActiveIndex + 1} of {_textSearchState.Count}";
    }

    // ── Info bar stub ─────────────────────────────────────────────────────────

    private void ShowInfoBar(string message, InfoBarSeverity severity) =>
        Logger.Log($"[InfoBar] {severity}: {message}");
}
