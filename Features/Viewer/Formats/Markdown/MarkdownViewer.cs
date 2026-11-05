using DocGlance.Core.Models;
using DocGlance.Features.Viewer.Contracts;
using DocGlance.Infrastructure.Diagnostics;
using DocGlance.Infrastructure.Logging;
using DocGlance.Rendering.Shared;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using DocGlance.Features.Viewer.Formats.Images;

namespace DocGlance.Features.Viewer.Formats.Markdown;

public sealed class MarkdownViewer : UserControl, IDocumentViewer, ISupportsSearch
{
    private const int MaxRenderedCharacters = 1_000_000;
    private const int MaxRenderedBlocks = 12_000;
    private const int MaxSearchResults = 5_000;
    private const int MaxEmbeddedImages = 12;
    private readonly StackPanel _document = new() { Spacing = 8, MaxWidth = 1080 };
    private readonly ScrollViewer _scrollViewer;
    private readonly List<MarkdownRun> _runs = new();
    private readonly StringBuilder _searchableText = new();
    private readonly SemaphoreSlim _imageLoadSemaphore = new(2);
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseMathematics()
        .Build();
    private bool _matchCase;
    private int _embeddedImageCount;
    private CancellationTokenSource? _renderCancellation;
    private DocumentItem? _currentDocument;

    public MarkdownViewer()
    {
        _scrollViewer = new ScrollViewer
        {
            Content = _document,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(32, 24, 32, 32),
        };
        Content = _scrollViewer;
    }

    public FrameworkElement Control => this;
    public bool MatchCase { get => _matchCase; set => _matchCase = value; }

    public async Task LoadAsync(DocumentItem document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        string filePath = document.FilePath
            ?? throw new ArgumentException("The document has no file path.", nameof(document));

        _renderCancellation?.Cancel();
        _renderCancellation?.Dispose();
        _renderCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken renderToken = _renderCancellation.Token;

        var loaded = await Task.Run(
            () => TextFileReader.ReadCapped(filePath, 5_000_000),
            renderToken);
        renderToken.ThrowIfCancellationRequested();

        _currentDocument = document;
        ResetRenderedContent();
        int renderLength = Math.Min(loaded.Text.Length, MaxRenderedCharacters);
        if (Path.GetExtension(filePath).Equals(".mmd", StringComparison.OrdinalIgnoreCase) &&
            MermaidFlowchartRenderer.TryRender(
                loaded.Text[..renderLength],
                GetBrush("TextFillColorPrimaryBrush"),
                GetBrush("AccentFillColorDefaultBrush")) is FrameworkElement mermaidDiagram)
        {
            _document.Children.Add(mermaidDiagram);
            Logger.Log("[MarkdownViewer] Rendered native Mermaid flowchart");
            DocumentOpenTrace.For(document)?.MarkFirstContentRendered();
            return;
        }

        var parsed = await Task.Run(
            () => global::Markdig.Markdown.Parse(loaded.Text[..renderLength], _pipeline),
            renderToken);
        renderToken.ThrowIfCancellationRequested();

        int renderedBlocks = RenderBlocks(parsed, 0);
        bool hasLimitNotice = renderLength < loaded.Text.Length || loaded.Truncated;
        if (hasLimitNotice)
        {
            AddNotice("Preview limited to keep memory use low. Open the file in a text editor to view the full source.");
        }

        if (renderedBlocks > 0 || hasLimitNotice)
            DocumentOpenTrace.For(document)?.MarkFirstContentRendered();
        else
            DocumentOpenTrace.For(document)?.Mark("ContentPublished", "EmptyMarkdown");
        Logger.Log(
            $"[MarkdownViewer] Rendered {renderedBlocks:N0} blocks and {renderLength:N0} characters");
    }

    public Task CleanupAsync()
    {
        _renderCancellation?.Cancel();
        _renderCancellation?.Dispose();
        _renderCancellation = null;
        _currentDocument = null;
        ResetRenderedContent();
        return Task.CompletedTask;
    }

    public Task<SearchResult[]> SearchAsync(string query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(query))
            return Task.FromResult(Array.Empty<SearchResult>());

        var results = new List<SearchResult>();
        StringComparison comparison = _matchCase
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        string text = _searchableText.ToString();
        int offset = 0;
        while (offset <= text.Length - query.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = text.IndexOf(query, offset, comparison);
            if (index < 0)
                break;

            results.Add(new SearchResult(
                text.Substring(index, query.Length),
                new SearchLocation[] { new TextRangeLocation(index, query.Length) }));
            if (results.Count >= MaxSearchResults)
                break;
            offset = index + Math.Max(1, query.Length);
        }

        return Task.FromResult(results.ToArray());
    }

    public void Highlight(SearchResult result)
    {
        ClearHighlight();
        if (result.Locations.Count == 0 ||
            result.Locations[0] is not TextRangeLocation range)
            return;

        int end = range.Start + range.Length;
        foreach (MarkdownRun item in _runs)
        {
            if (item.End <= range.Start || item.Start >= end)
                continue;

            item.Run.Foreground = GetBrush("SystemFillColorCriticalBrush");
            item.Run.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            item.Host.StartBringIntoView();
        }
    }

    public void ClearHighlight()
    {
        foreach (MarkdownRun item in _runs)
        {
            item.Run.Foreground = item.OriginalForeground;
            item.Run.FontWeight = item.OriginalFontWeight;
        }
    }

    private int RenderBlocks(ContainerBlock parent, int renderedBlocks)
    {
        foreach (MdBlock block in parent)
        {
            if (renderedBlocks >= MaxRenderedBlocks)
            {
                AddNotice("The preview reached its block limit to keep memory use low.");
                break;
            }

            if (_searchableText.Length > 0)
                _searchableText.Append('\n');

            switch (block)
            {
                case HeadingBlock heading:
                    AddRichTextBlock(
                        heading.Inline,
                        new Thickness(0, heading.Level == 1 ? 18 : 12, 0, 2),
                        GetHeadingSize(heading.Level),
                        Microsoft.UI.Text.FontWeights.SemiBold);
                    break;

                case ParagraphBlock paragraph:
                    AddRichTextBlock(paragraph.Inline, new Thickness(0, 2, 0, 6), 16);
                    break;

                case ListBlock list:
                    renderedBlocks = RenderList(list, renderedBlocks);
                    break;

                case QuoteBlock quote:
                    AddQuote(quote);
                    break;

                case Markdig.Extensions.Tables.Table table:
                    AddTable(table);
                    break;

                case Markdig.Extensions.Mathematics.MathBlock math:
                    AddCodeBlock(math.Lines.ToString(), "math");
                    break;

                case FencedCodeBlock fencedCode:
                    if (fencedCode.Info?.ToString().Trim().Equals(
                            "mermaid", StringComparison.OrdinalIgnoreCase) == true &&
                        MermaidFlowchartRenderer.TryRender(
                            fencedCode.Lines.ToString(), GetBrush("TextFillColorPrimaryBrush"),
                            GetBrush("AccentFillColorDefaultBrush")) is FrameworkElement diagram)
                    {
                        _document.Children.Add(diagram);
                        break;
                    }
                    AddCodeBlock(fencedCode.Lines.ToString(), fencedCode.Info?.ToString());
                    break;

                case CodeBlock code:
                    AddCodeBlock(code.Lines.ToString(), null);
                    break;

                case ThematicBreakBlock:
                    _document.Children.Add(new Border
                    {
                        Height = 1,
                        Margin = new Thickness(0, 12, 0, 12),
                        Background = GetBrush("DividerStrokeColorDefaultBrush"),
                    });
                    break;

                case HtmlBlock html:
                    AddCodeBlock(html.Lines.ToString(), "HTML");
                    break;

                case ContainerBlock container:
                    renderedBlocks = RenderBlocks(container, renderedBlocks);
                    break;
            }

            renderedBlocks++;
        }

        return renderedBlocks;
    }

    private int RenderList(ListBlock list, int renderedBlocks)
    {
        int.TryParse(list.OrderedStart, out int itemNumber);
        foreach (MdBlock block in list)
        {
            if (block is not ListItemBlock item)
                continue;

            var itemGrid = new Grid { ColumnSpacing = 10, Margin = new Thickness(16, 1, 0, 2) };
            itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            itemGrid.Children.Add(new TextBlock
            {
                Text = list.IsOrdered ? $"{itemNumber++}." : "\u2022",
                FontSize = 16,
                Foreground = GetBrush("TextFillColorSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Top,
            });
            var itemContent = new StackPanel { Spacing = 4 };
            Grid.SetColumn(itemContent, 1);
            itemGrid.Children.Add(itemContent);
            _document.Children.Add(itemGrid);

            foreach (MdBlock child in item)
            {
                if (child is ParagraphBlock paragraph)
                {
                    itemContent.Children.Add(CreateRichTextBlock(paragraph.Inline, 15));
                }
                else if (child is ListBlock nested)
                {
                    RenderNestedList(nested, itemContent, 1);
                }
                else if (child is CodeBlock code)
                {
                    itemContent.Children.Add(CreateCodeBlock(code.Lines.ToString()));
                }
            }

            renderedBlocks++;
            if (renderedBlocks >= MaxRenderedBlocks)
                break;
        }

        return renderedBlocks;
    }

    private void RenderNestedList(ListBlock list, Panel parent, int depth)
    {
        int.TryParse(list.OrderedStart, out int number);
        foreach (MdBlock block in list)
        {
            if (block is not ListItemBlock item)
                continue;

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(depth * 16, 1, 0, 1),
            };
            row.Children.Add(new TextBlock
            {
                Text = list.IsOrdered ? $"{number++}." : "\u2022",
                Foreground = GetBrush("TextFillColorSecondaryBrush"),
            });
            var content = new StackPanel { Spacing = 4 };
            foreach (MdBlock child in item)
            {
                if (child is ParagraphBlock paragraph)
                    content.Children.Add(CreateRichTextBlock(paragraph.Inline, 15));
                else if (child is ListBlock nested)
                    RenderNestedList(nested, content, depth + 1);
                else if (child is CodeBlock code)
                    content.Children.Add(CreateCodeBlock(code.Lines.ToString()));
            }
            row.Children.Add(content);
            parent.Children.Add(row);
        }
    }

    private void AddQuote(QuoteBlock quote)
    {
        var quoteContent = new StackPanel { Spacing = 4 };
        foreach (MdBlock block in quote)
        {
            if (block is ParagraphBlock paragraph)
                quoteContent.Children.Add(CreateRichTextBlock(paragraph.Inline, 15));
            else if (block is CodeBlock code)
                quoteContent.Children.Add(CreateCodeBlock(code.Lines.ToString()));
        }

        _document.Children.Add(new Border
        {
            BorderBrush = GetBrush("AccentFillColorDefaultBrush"),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(14, 8, 10, 8),
            Margin = new Thickness(4, 4, 0, 8),
            Background = GetBrush("LayerFillColorAltBrush"),
            Child = quoteContent,
        });
    }

    private void AddTable(Markdig.Extensions.Tables.Table table)
    {
        var rows = new List<Markdig.Extensions.Tables.TableRow>();
        foreach (MdBlock block in table)
        {
            if (block is Markdig.Extensions.Tables.TableRow row)
                rows.Add(row);
        }

        if (rows.Count == 0)
            return;

        int columns = 0;
        foreach (MdBlock cell in rows[0])
            columns++;
        if (columns == 0)
            return;

        var grid = new Grid
        {
            Margin = new Thickness(0, 6, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        for (int col = 0; col < columns; col++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int colIndex = 0;
            foreach (MdBlock block in rows[rowIndex])
            {
                if (block is not Markdig.Extensions.Tables.TableCell cell)
                    continue;

                var cellContent = new StackPanel { MinWidth = 90, MaxWidth = 480 };
                foreach (MdBlock child in cell)
                {
                    if (child is ParagraphBlock paragraph)
                        cellContent.Children.Add(CreateRichTextBlock(paragraph.Inline, 14));
                }

                var border = new Border
                {
                    BorderBrush = GetBrush("CardStrokeColorDefaultBrush"),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(10, 7, 10, 7),
                    Background = rowIndex == 0
                        ? GetBrush("LayerFillColorAltBrush")
                        : new SolidColorBrush(Colors.Transparent),
                    Child = cellContent,
                };
                Grid.SetRow(border, rowIndex);
                Grid.SetColumn(border, colIndex++);
                grid.Children.Add(border);
            }
        }

        _document.Children.Add(new ScrollViewer
        {
            Content = grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
    }

    private void AddRichTextBlock(
        ContainerInline? inlines,
        Thickness margin,
        double fontSize,
        Windows.UI.Text.FontWeight? fontWeight = null)
    {
        var block = CreateRichTextBlock(inlines, fontSize, fontWeight);
        block.Margin = margin;
        _document.Children.Add(block);
    }

    private RichTextBlock CreateRichTextBlock(
        ContainerInline? inlines,
        double fontSize,
        Windows.UI.Text.FontWeight? fontWeight = null)
    {
        var block = new RichTextBlock
        {
            FontSize = fontSize,
            Foreground = GetBrush("TextFillColorPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Margin = new Thickness(0),
        };
        var paragraph = new Paragraph { FontSize = fontSize, FontWeight = fontWeight ?? Microsoft.UI.Text.FontWeights.Normal };
        if (inlines != null)
            AppendInlines(inlines, paragraph.Inlines, block, null);
        block.Blocks.Add(paragraph);
        return block;
    }

    private void AppendInlines(
        ContainerInline container,
        InlineCollection target,
        RichTextBlock host,
        Windows.UI.Text.FontWeight? inheritedWeight)
    {
        for (MdInline? inline = container.FirstChild; inline != null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    AddRun(literal.Content.ToString(), target, host, inheritedWeight);
                    break;

                case CodeInline code:
                    var codeRun = new Run
                    {
                        Text = code.Content.ToString(),
                        FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
                        Foreground = GetBrush("AccentTextFillColorPrimaryBrush"),
                    };
                    RegisterRun(codeRun, host);
                    target.Add(codeRun);
                    break;

                case Markdig.Extensions.Mathematics.MathInline math:
                    AddRun(math.Content.ToString(), target, host, inheritedWeight, italic: true);
                    break;

                case LineBreakInline:
                    target.Add(new LineBreak());
                    break;

                case LinkInline link when link.IsImage:
                    AddImageInline(link, target);
                    break;

                case LinkInline link:
                    var hyperlink = new Hyperlink();
                    if (link.Url is string url &&
                        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
                        (uri.Scheme == Uri.UriSchemeHttp ||
                         uri.Scheme == Uri.UriSchemeHttps ||
                         uri.Scheme == Uri.UriSchemeMailto))
                    {
                        hyperlink.NavigateUri = uri;
                    }
                    AppendInlines(link, hyperlink.Inlines, host, inheritedWeight);
                    target.Add(hyperlink);
                    break;

                case EmphasisInline emphasis:
                    Span span = emphasis.DelimiterCount >= 2
                        ? new Bold()
                        : emphasis.DelimiterChar == '~'
                            ? new Span()
                            : new Italic();
                    AppendInlines(emphasis, span.Inlines, host,
                        emphasis.DelimiterCount >= 2
                            ? Microsoft.UI.Text.FontWeights.SemiBold
                            : inheritedWeight);
                    target.Add(span);
                    break;

                case ContainerInline nested:
                    AppendInlines(nested, target, host, inheritedWeight);
                    break;
            }
        }
    }

    private void AddImageInline(LinkInline link, InlineCollection target)
    {
        string alt = GetInlineText(link);
        string? path = ResolveLocalImage(link.Url);
        if (path == null || _embeddedImageCount >= MaxEmbeddedImages)
        {
            if (!string.IsNullOrEmpty(alt))
                AddRun($"[{alt}]", target, null, null);
            return;
        }
        _embeddedImageCount++;

        var image = new Image
        {
            Width = 640,
            Height = 360,
            MaxWidth = 640,
            MaxHeight = 360,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(image, alt);
        target.Add(new InlineUIContainer { Child = image });
        _ = LoadInlineImageAsync(image, path, _renderCancellation?.Token ?? CancellationToken.None);
    }

    private async Task LoadInlineImageAsync(Image image, string path, CancellationToken cancellationToken)
    {
        try
        {
            await _imageLoadSemaphore.WaitAsync(cancellationToken);
            try
            {
                LoadedImageSource loaded = await ImageSourceLoader.LoadAsync(
                    path, decodeWidth: 960, cancellationToken);
                image.Source = loaded.Source;
            }
            finally
            {
                _imageLoadSemaphore.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Error($"[MarkdownViewer] Failed to load embedded image: {path}", ex);
        }
    }

    private void AddRun(
        string text,
        InlineCollection target,
        RichTextBlock? host,
        Windows.UI.Text.FontWeight? fontWeight,
        bool italic = false)
    {
        if (text.Length == 0)
            return;

        var run = new Run
        {
            Text = text,
            FontWeight = fontWeight ?? Microsoft.UI.Text.FontWeights.Normal,
            FontStyle = italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
        };
        if (host != null)
        {
            int start = _searchableText.Length;
            _searchableText.Append(text);
            _runs.Add(new MarkdownRun(run, host, start, start + text.Length, run.Foreground, run.FontWeight));
        }
        target.Add(run);
    }

    private void RegisterRun(Run run, RichTextBlock host)
    {
        int start = _searchableText.Length;
        _searchableText.Append(run.Text);
        _runs.Add(new MarkdownRun(run, host, start, start + run.Text.Length, run.Foreground, run.FontWeight));
    }

    private Border CreateCodeBlock(string text)
    {
        var code = new RichTextBlock
        {
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Foreground = GetBrush("TextFillColorPrimaryBrush"),
        };
        var codeText = new Run { Text = text.TrimEnd('\r', '\n') };
        RegisterRun(codeText, code);
        code.Blocks.Add(new Paragraph { Inlines = { codeText } });
        return new Border
        {
            Background = GetBrush("LayerFillColorAltBrush"),
            BorderBrush = GetBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 3, 0, 10),
            Child = code,
        };
    }

    private void AddCodeBlock(string text, string? language)
    {
        var codeBorder = CreateCodeBlock(text);
        if (!string.IsNullOrWhiteSpace(language))
            codeBorder.Tag = language.Trim();
        _document.Children.Add(codeBorder);
    }

    private void AddNotice(string message)
    {
        _document.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 12),
            Foreground = GetBrush("TextFillColorSecondaryBrush"),
        });
    }

    private void ResetRenderedContent()
    {
        _document.Children.Clear();
        _runs.Clear();
        _searchableText.Clear();
        _embeddedImageCount = 0;
        _scrollViewer.ChangeView(0, 0, 1, disableAnimation: true);
    }

    private string? ResolveLocalImage(string? url)
    {
        if (_currentDocument?.FilePath is not string markdownPath ||
            string.IsNullOrWhiteSpace(url) ||
            Uri.TryCreate(url, UriKind.Absolute, out _))
            return null;

        try
        {
            string decodedUrl = Uri.UnescapeDataString(url.Split('#', '?')[0]);
            string path = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(markdownPath) ?? string.Empty,
                decodedUrl.Replace('/', Path.DirectorySeparatorChar)));
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex)
        {
            Logger.Error($"[MarkdownViewer] Invalid embedded image path: {url}", ex);
            return null;
        }
    }

    private static string GetInlineText(ContainerInline inline)
    {
        var text = new StringBuilder();
        for (MdInline? child = inline.FirstChild; child != null; child = child.NextSibling)
        {
            if (child is LiteralInline literal)
                text.Append(literal.Content.ToString());
        }
        return text.ToString();
    }

    private static double GetHeadingSize(int level) => level switch
    {
        1 => 32,
        2 => 27,
        3 => 23,
        4 => 20,
        5 => 18,
        _ => 16,
    };

    private static SolidColorBrush GetBrush(string resourceKey)
    {
        if (Application.Current.Resources.TryGetValue(resourceKey, out object? value) &&
            value is SolidColorBrush brush)
            return brush;
        return new SolidColorBrush(Colors.Gray);
    }

    private sealed record MarkdownRun(
        Run Run,
        RichTextBlock Host,
        int Start,
        int End,
        Brush? OriginalForeground,
        Windows.UI.Text.FontWeight OriginalFontWeight);
}

public sealed class MarkdownViewerFactory : IDocumentViewerFactory
{
    private static readonly IReadOnlyCollection<string> Extensions =
        new[] { ".md", ".markdown", ".mdx", ".mmd" };

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public IDocumentViewer Create(ViewerViewModel viewModel) => new MarkdownViewer();
}
