using DocGlance.Core.Models;
using DocGlance.Features.Viewer.Components;
using DocGlance.Features.Viewer.Contracts;
using DocGlance.Infrastructure.Diagnostics;
using DocGlance.Rendering.Shared;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Text;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace DocGlance.Features.Viewer.Formats.Text;

public sealed partial class TextViewer : UserControl, IDocumentViewer, ISupportsSearch, ISupportsTextSelection
{
    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".vb", ".fs", ".csx", ".js", ".mjs", ".cjs", ".jsx", ".ts", ".tsx",
        ".py", ".pyw", ".java", ".kt", ".kts", ".cpp", ".c", ".h", ".hpp", ".cc",
        ".go", ".rs", ".swift", ".rb", ".php", ".pl", ".sh", ".bash", ".zsh",
        ".ps1", ".psm1", ".bat", ".cmd", ".cshtml", ".razor",
        ".css", ".scss", ".less", ".json", ".jsonc", ".json5", ".xml", ".xaml",
        ".sql", ".graphql", ".gql", ".r", ".lua", ".dart", ".ex", ".exs",
    };

    private static readonly HashSet<string> CodeKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "async", "await", "bool", "break", "byte", "case", "catch",
        "char", "class", "const", "continue", "def", "delegate", "do", "double",
        "elif", "else", "enum", "export", "extends", "false", "final", "finally",
        "float", "for", "foreach", "from", "func", "function", "get", "if", "implements",
        "import", "in", "int", "interface", "let", "long", "module", "namespace", "new",
        "null", "of", "override", "package", "private", "protected", "public", "return",
        "set", "short", "static", "string", "struct", "super", "switch", "this", "throw",
        "throws", "true", "try", "type", "var", "void", "while", "with", "yield",
    };

    private const int MaxHighlightedCharacters = 1_000_000;
    private const int MaxSyntaxRuns = 30_000;

    private SearchState? _searchState;
    private string _text = string.Empty;
    private bool _hasSyntaxHighlighting;

    public TextViewer()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) =>
        {
            if (_hasSyntaxHighlighting)
                ApplySyntaxHighlighting(_text);
        };
    }

    public event EventHandler? SelectionChanged;
    public FrameworkElement Control => this;
    public RichEditBox TextControl => PlainTextBlock;
    public TextBlock LineNumbers => LineNumberBlock;
    public string SelectedText => PlainTextBlock.Document.Selection.Text;
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
        PlainTextBlock.AcceptsReturn = true;
        PlainTextBlock.IsReadOnly = false;
        try
        {
            PlainTextBlock.Document.SetText(TextSetOptions.None, text);
            PlainTextBlock.Document.GetText(TextGetOptions.None, out _text);
            ApplyBaseTextFormat(_text.Length);
            _hasSyntaxHighlighting =
                CodeExtensions.Contains(System.IO.Path.GetExtension(filePath)) &&
                _text.Length <= MaxHighlightedCharacters;
            if (_hasSyntaxHighlighting)
                ApplySyntaxHighlighting(_text);
        }
        finally
        {
            PlainTextBlock.IsReadOnly = true;
        }

        LineNumberBlock.FontFamily = new FontFamily("Cascadia Code, Cascadia Mono, Consolas, Courier New");
        LineNumberBlock.FontSize = 13;
        LineNumberBlock.LineHeight = 22;
        GenerateLineNumbers(lineCount);
        ClearHighlight();
        DocumentOpenTrace.For(document)?.MarkFirstContentRendered();
        Infrastructure.Logging.Logger.Log(
            $"[TextViewer] {lineCount:N0} lines, {loaded.FileBytes / 1024.0:F1} KB");
    }

    public Task CleanupAsync()
    {
        ClearHighlight();
        _text = string.Empty;
        _hasSyntaxHighlighting = false;
        PlainTextBlock.IsReadOnly = false;
        try
        {
            PlainTextBlock.Document.SetText(TextSetOptions.None, string.Empty);
        }
        finally
        {
            PlainTextBlock.IsReadOnly = true;
        }

        LineNumberBlock.Text = string.Empty;
        return Task.CompletedTask;
    }

    public Task<SearchResult[]> SearchAsync(string query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _searchState = SearchHighlighter.FindInText(_text, query, MatchCase);
        var results = new SearchResult[_searchState.Count];
        for (int i = 0; i < results.Length; i++)
        {
            SearchHit hit = _searchState.Hits[i];
            results[i] = new SearchResult(
                _text.Substring(hit.CharOffset, hit.Length),
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
                    new SolidColorBrush(Color.FromArgb(180, 255, 140, 0));
                PlainTextBlock.Document.Selection.SetRange(
                    range.Start,
                    range.Start + range.Length);
                return;
            }
        }
    }

    public void ClearHighlight()
    {
        _searchState = null;
        if (PlainTextBlock != null)
            PlainTextBlock.Document.Selection.SetRange(0, 0);
    }

    public Task<string?> GetSelectedTextAsync() =>
        Task.FromResult<string?>(string.IsNullOrWhiteSpace(SelectedText)
            ? null
            : SelectedText);

    public Task CopySelectionAsync()
    {
        CopySelectedTextToClipboard();
        return Task.CompletedTask;
    }

    private void CopySelectedTextToClipboard()
    {
        string selectedText = SelectedText;
        if (string.IsNullOrEmpty(selectedText))
            return;

        try
        {
            var package = new DataPackage();
            package.SetText(selectedText);
            Clipboard.SetContent(package);
            Clipboard.Flush();
        }
        catch (Exception ex)
        {
            Infrastructure.Logging.Logger.Error("[TextViewer] Failed to copy selected text", ex);
        }
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
            _text.Substring(hit.CharOffset, hit.Length),
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

    private void ApplyBaseTextFormat(int length)
    {
        if (length == 0)
            return;

        var format = PlainTextBlock.Document.GetRange(0, length).CharacterFormat;
        format.Size = (float)PlainTextBlock.FontSize;
    }

    private void ApplySyntaxHighlighting(string text)
    {
        bool isLightTheme = ActualTheme == ElementTheme.Light;
        Color keywordColor = isLightTheme
            ? Color.FromArgb(255, 0, 0, 255)
            : Color.FromArgb(255, 86, 156, 214);
        Color stringColor = isLightTheme
            ? Color.FromArgb(255, 163, 21, 21)
            : Color.FromArgb(255, 206, 145, 120);
        Color commentColor = isLightTheme
            ? Color.FromArgb(255, 0, 128, 0)
            : Color.FromArgb(255, 106, 153, 85);
        Color numberColor = isLightTheme
            ? Color.FromArgb(255, 9, 134, 88)
            : Color.FromArgb(255, 181, 206, 168);
        int syntaxRuns = 0;
        int index = 0;

        while (index < text.Length && syntaxRuns < MaxSyntaxRuns)
        {
            int start = index;
            char current = text[index];

            if (current == '/' && index + 1 < text.Length &&
                (text[index + 1] == '/' || text[index + 1] == '*'))
            {
                bool blockComment = text[index + 1] == '*';
                index += 2;
                if (blockComment)
                {
                    while (index + 1 < text.Length &&
                           !(text[index] == '*' && text[index + 1] == '/'))
                        index++;
                    index = Math.Min(text.Length, index + 2);
                }
                else
                {
                    while (index < text.Length && text[index] is not '\n' and not '\r')
                        index++;
                }

                ApplyForeground(start, index, commentColor);
                syntaxRuns++;
                continue;
            }

            if (current == '#' && IsLinePrefixWhitespace(text, index))
            {
                while (index < text.Length && text[index] is not '\n' and not '\r')
                    index++;
                ApplyForeground(start, index, commentColor);
                syntaxRuns++;
                continue;
            }

            if (current is '\'' or '"' or '`')
            {
                char quote = current;
                bool tripleQuoted = quote != '`' && index + 2 < text.Length &&
                                    text[index + 1] == quote && text[index + 2] == quote;
                index += tripleQuoted ? 3 : 1;
                while (index < text.Length)
                {
                    if (text[index] == '\\')
                    {
                        index = Math.Min(text.Length, index + 2);
                        continue;
                    }

                    if (tripleQuoted)
                    {
                        if (index + 2 < text.Length &&
                            text[index] == quote &&
                            text[index + 1] == quote &&
                            text[index + 2] == quote)
                        {
                            index += 3;
                            break;
                        }
                    }
                    else if (text[index] == quote)
                    {
                        index++;
                        break;
                    }

                    index++;
                }

                ApplyForeground(start, index, stringColor);
                syntaxRuns++;
                continue;
            }

            if (char.IsDigit(current))
            {
                index++;
                while (index < text.Length &&
                       (char.IsLetterOrDigit(text[index]) || text[index] is '.' or '_'))
                    index++;
                ApplyForeground(start, index, numberColor);
                syntaxRuns++;
                continue;
            }

            if (char.IsLetter(current) || current == '_')
            {
                index++;
                while (index < text.Length &&
                       (char.IsLetterOrDigit(text[index]) || text[index] == '_'))
                    index++;
                if (CodeKeywords.Contains(text.Substring(start, index - start)))
                {
                    ApplyForeground(start, index, keywordColor);
                    syntaxRuns++;
                }
                continue;
            }

            index++;
        }
    }

    private void ApplyForeground(int start, int end, Color color)
    {
        if (end > start)
            PlainTextBlock.Document.GetRange(start, end).CharacterFormat.ForegroundColor = color;
    }

    private static bool IsLinePrefixWhitespace(string text, int index)
    {
        while (index > 0 && text[index - 1] != '\n')
        {
            if (!char.IsWhiteSpace(text[index - 1]))
                return false;
            index--;
        }

        return true;
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
        ".editorconfig", ".htaccess",
        ".cs", ".vb", ".fs", ".csx", ".js", ".mjs", ".cjs", ".jsx",
        ".ts", ".tsx", ".py", ".pyw", ".java", ".kt", ".kts",
        ".cpp", ".c", ".h", ".hpp", ".cc", ".go", ".rs", ".swift",
        ".rb", ".php", ".pl", ".sh", ".bash", ".zsh", ".ps1", ".psm1",
        ".bat", ".cmd", ".html", ".htm", ".cshtml", ".razor", ".css",
        ".scss", ".less", ".json", ".jsonc", ".json5", ".xml", ".xaml",
        ".csproj", ".sln", ".props", ".targets", ".sql", ".graphql",
        ".gql", ".dockerfile", ".makefile", ".r", ".lua", ".dart", ".ex", ".exs",
    };

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public IDocumentViewer Create(ViewerViewModel viewModel) => new TextViewer();
}
