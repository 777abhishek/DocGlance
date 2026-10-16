using DocGlance.Core.Models;
using DocGlance.Features.Viewer.Components;
using DocGlance.Features.Viewer.Contracts;
using DocGlance.Rendering.Shared;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace DocGlance.Features.Viewer.Formats.Text;

public sealed partial class TextViewer : UserControl, IDocumentViewer, ISupportsSearch, ISupportsTextSelection
{
    private SearchState? _searchState;

    public TextViewer()
    {
        InitializeComponent();
    }

    public event EventHandler? SelectionChanged;
    public FrameworkElement Control => this;
    public TextBox TextBox => PlainTextBlock;
    public TextBlock LineNumbers => LineNumberBlock;
    public bool MatchCase { get; set; }
    public int SearchMatchCount => _searchState?.Count ?? 0;
    public int ActiveSearchIndex => _searchState?.ActiveIndex ?? -1;

    public async Task LoadAsync(DocumentItem document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        string filePath = document.FilePath
            ?? throw new ArgumentException("The document has no file path.", nameof(document));

        var loaded = await Task.Run(
            () => TextFileReader.ReadCapped(filePath, 5_000_000),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        string text = loaded.Text;
        int lineCount = loaded.LineCount;
        if (loaded.Truncated)
        {
            text += $"\n\n--- Truncated: showing first 5 MB of {loaded.FileBytes / 1_000_000.0:F1} MB ---";
            lineCount += 2;
        }

        PlainTextBlock.FontFamily = new FontFamily("Cascadia Code, Cascadia Mono, Consolas, Courier New");
        PlainTextBlock.FontSize = 13;
        PlainTextBlock.TextWrapping = Microsoft.UI.Xaml.TextWrapping.NoWrap;
        PlainTextBlock.IsReadOnly = true;
        PlainTextBlock.AcceptsReturn = true;
        PlainTextBlock.Text = text;
        LineNumberBlock.FontFamily = new FontFamily("Cascadia Code, Cascadia Mono, Consolas, Courier New");
        LineNumberBlock.FontSize = 13;
        LineNumberBlock.LineHeight = 22;
        GenerateLineNumbers(lineCount);
        ClearHighlight();
        Infrastructure.Logging.Logger.Log(
            $"[TextViewer] {lineCount:N0} lines, {loaded.FileBytes / 1024.0:F1} KB");
    }

    public Task CleanupAsync()
    {
        ClearHighlight();
        PlainTextBlock.Text = string.Empty;
        LineNumberBlock.Text = string.Empty;
        return Task.CompletedTask;
    }

    public Task<SearchResult[]> SearchAsync(string query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _searchState = SearchHighlighter.FindInText(PlainTextBlock.Text, query, MatchCase);
        var results = new SearchResult[_searchState.Count];
        for (int i = 0; i < results.Length; i++)
        {
            SearchHit hit = _searchState.Hits[i];
            results[i] = new SearchResult(
                PlainTextBlock.Text.Substring(hit.CharOffset, hit.Length),
                new SearchLocation[] { new TextRangeLocation(hit.CharOffset, hit.Length) });
        }

        if (_searchState.HasHits)
            HighlightActiveSearchHit();

        return Task.FromResult(results);
    }

    public void Highlight(SearchResult result)
    {
        foreach (SearchLocation location in result.Locations)
        {
            if (location is TextRangeLocation range)
            {
                PlainTextBlock.SelectionHighlightColorWhenNotFocused =
                    new SolidColorBrush(Windows.UI.Color.FromArgb(180, 255, 140, 0));
                PlainTextBlock.Select(range.Start, range.Length);
                return;
            }
        }
    }

    public void ClearHighlight()
    {
        _searchState = null;
        if (PlainTextBlock != null)
            PlainTextBlock.SelectionLength = 0;
    }

    public Task<string?> GetSelectedTextAsync() =>
        Task.FromResult<string?>(string.IsNullOrWhiteSpace(PlainTextBlock.SelectedText)
            ? null
            : PlainTextBlock.SelectedText);

    public Task CopySelectionAsync()
    {
        string selectedText = PlainTextBlock.SelectedText;
        if (!string.IsNullOrEmpty(selectedText))
        {
            var package = new DataPackage();
            package.SetText(selectedText);
            Clipboard.SetContent(package);
            Clipboard.Flush();
        }
        return Task.CompletedTask;
    }

    public void NavigateSearchHit(int direction)
    {
        if (_searchState?.Navigate(direction) is not null)
            HighlightActiveSearchHit();
    }

    private void HighlightActiveSearchHit()
    {
        if (_searchState is not { HasHits: true, ActiveIndex: >= 0 })
            return;
        SearchHit hit = _searchState.Hits[_searchState.ActiveIndex];
        Highlight(new SearchResult(
            PlainTextBlock.Text.Substring(hit.CharOffset, hit.Length),
            new SearchLocation[] { new TextRangeLocation(hit.CharOffset, hit.Length) }));
    }

    private void GenerateLineNumbers(int count)
    {
        var builder = new StringBuilder();
        for (int line = 1; line <= count; line++)
        {
            if (line > 1)
                builder.Append('\n');
            builder.Append(line);
        }
        LineNumberBlock.Text = builder.ToString();
    }

    private void PlainTextBlock_SelectionChanged(object sender, RoutedEventArgs e) =>
        SelectionChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class TextViewerFactory : IDocumentViewerFactory
{
    private static readonly IReadOnlyCollection<string> Extensions = new[]
    {
        ".txt", ".log", ".csv", ".tsv", ".ini", ".cfg", ".conf",
        ".env", ".toml", ".yaml", ".yml", ".properties", ".gitignore",
        ".editorconfig", ".htaccess", ".md", ".markdown", ".mdx",
        ".cs", ".vb", ".fs", ".csx", ".js", ".mjs", ".cjs", ".jsx",
        ".ts", ".tsx", ".py", ".pyw", ".java", ".kt", ".kts",
        ".cpp", ".c", ".h", ".hpp", ".cc", ".go", ".rs", ".swift",
        ".rb", ".php", ".pl", ".sh", ".bash", ".zsh", ".ps1", ".psm1",
        ".bat", ".cmd", ".html", ".htm", ".cshtml", ".razor", ".css",
        ".scss", ".less", ".json", ".jsonc", ".json5", ".xml", ".xaml",
        ".svg", ".csproj", ".sln", ".props", ".targets", ".sql", ".graphql",
        ".gql", ".dockerfile", ".makefile", ".r", ".lua", ".dart", ".ex", ".exs",
    };

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public IDocumentViewer Create(ViewerViewModel viewModel) => new TextViewer();
}
