using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocGlance.Core.Models;
using DocGlance.Features.Viewer.Contracts;
using DocGlance.Core.Enums;
using DocGlance.Features.Viewer.Components;
using DocGlance.Rendering.Pdf.Models;
using DocGlance.Rendering.Pdf;
using Windows.UI;

namespace DocGlance.Features.Viewer.Formats.Pdf;

public sealed partial class PdfViewer : UserControl, IDocumentViewer, ISupportsZoom, ISupportsSearch, ISupportsThumbnails, ISupportsOutline
{
    private IPdfViewerService _pdfService = new PdfViewerService();
    private FitMode _currentFitMode = FitMode.None;
    private double _currentZoom = 1.0;
    private IReadOnlyList<PdfSearchResult> _pdfSearchResults = Array.Empty<PdfSearchResult>();
    private SearchResult[] _searchResults = Array.Empty<SearchResult>();
    private int _activeSearchIndex = -1;
    private IReadOnlyList<PdfTextAnnotation> _annotations = Array.Empty<PdfTextAnnotation>();
    private readonly ObservableCollection<PageThumbnailItem> _thumbnails = new();
    private CancellationTokenSource? _thumbnailPrefetchCts;
    private Task? _thumbnailPrefetchTask;
    private IReadOnlyList<OutlineItem> _outline = Array.Empty<OutlineItem>();
    private readonly List<Rectangle> _selectionOverlays = new();
    private readonly Dictionary<int, PdfPageTextMap> _textPageMaps = new();
    private readonly HashSet<int> _textPageMapsLoading = new();
    private CancellationTokenSource? _textMapCts;
    private int _textMapGeneration;
    private const int TextMapCacheCapacity = 12;
    private PdfDocumentState? _documentState;

    public PdfViewer()
    {
        InitializeComponent();
        DocumentScrollViewer.ViewChanged += (_, _) =>
        {
            double zoom = DocumentScrollViewer.ZoomFactor;
            if (Math.Abs(zoom - _currentZoom) < 0.001)
                return;

            _currentZoom = zoom;
            ZoomChanged?.Invoke(this, zoom);
        };
    }

    public event EventHandler<double>? ZoomChanged;
    public event EventHandler<int>? PageNavigationRequested;
    public event EventHandler<int>? OutlineNavigationRequested;
    internal event EventHandler<int>? TextPageMapLoaded;
    public double CurrentZoom => _currentZoom;
    public FitMode CurrentFitMode => _currentFitMode;
    public bool MatchCase { get; set; }
    internal double PdfRotation { get; set; }
    internal PdfViewportState? ViewportState { get; set; }

    internal PdfDocumentState? DocumentState
    {
        get => _documentState;
        set
        {
            if (ReferenceEquals(_documentState, value))
                return;

            _documentState = value;
            ResetTextPageMaps();
        }
    }
    internal int SelectionPageIndex { get; set; } = -1;

    internal void EnsureTextPageMap(int pageIndex)
    {
        PdfDocumentState? state = DocumentState;
        if (state == null || state.ManagedPdfDocument != null ||
            (uint)pageIndex >= (uint)state.PageCount ||
            _textPageMaps.ContainsKey(pageIndex) ||
            !_textPageMapsLoading.Add(pageIndex))
            return;

        _ = LoadTextPageMapAsync(state, pageIndex, _textMapGeneration, _textMapCts?.Token ?? default);
    }

    internal bool TryGetTextPageMap(int pageIndex, out PdfPageTextMap map) =>
        _textPageMaps.TryGetValue(pageIndex, out map!);
    internal bool HasTextPageMap(int pageIndex) => _textPageMaps.ContainsKey(pageIndex);

    internal IReadOnlyList<PdfSearchRect> GetSelectionRects(int pageIndex, int anchor, int focus)
    {
        if (!TryGetTextPageMap(pageIndex, out PdfPageTextMap map))
            return Array.Empty<PdfSearchRect>();

        int start = Math.Min(anchor, focus);
        int end = Math.Max(anchor, focus) + 1;
        return map.GetSelectionRects(start, end);
    }

    internal async Task<string?> GetSelectedTextAsync(
        int pageIndex,
        int anchor,
        int focus,
        CancellationToken cancellationToken)
    {
        PdfDocumentState? state = DocumentState;
        int start = Math.Min(anchor, focus);
        int end = Math.Max(anchor, focus) + 1;
        if (state == null || pageIndex < 0 || start < 0 || end <= start)
            return null;

        int generation = state.Generation;
        string text = await PdfTextService.GetTextRangeAsync(
            state, pageIndex, start, end, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(DocumentState, state) || state.Generation != generation)
            return null;

        return text;
    }

    private async Task LoadTextPageMapAsync(
        PdfDocumentState state,
        int pageIndex,
        int generation,
        CancellationToken cancellationToken)
    {
        try
        {
            PdfPageTextMap map = await PdfTextService.GetPageTextMapAsync(
                state, pageIndex, cancellationToken);
            if (!ReferenceEquals(DocumentState, state) || generation != _textMapGeneration)
                return;

            _textPageMaps[pageIndex] = map;
            TrimTextPageMaps();
            TextPageMapLoaded?.Invoke(this, pageIndex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Infrastructure.Logging.Logger.Error(
                $"[PDFSelection] Failed to load text geometry for page {pageIndex + 1}", ex);
        }
        finally
        {
            if (generation == _textMapGeneration)
                _textPageMapsLoading.Remove(pageIndex);
        }
    }

    private void TrimTextPageMaps()
    {
        if (ViewportState == null)
            return;

        int currentPage = ViewportState.CurrentPageIndex;
        while (_textPageMaps.Count > TextMapCacheCapacity)
        {
            int pageToRemove = _textPageMaps.Keys
                .Where(page => page != currentPage && page != SelectionPageIndex)
                .OrderByDescending(page => Math.Abs(page - currentPage))
                .FirstOrDefault(-1);
            if (pageToRemove < 0)
                break;
            _textPageMaps.Remove(pageToRemove);
        }
    }

    internal void ResetTextPageMaps()
    {
        _textMapCts?.Cancel();
        _textMapCts?.Dispose();
        _textMapCts = DocumentState == null
            ? null
            : new CancellationTokenSource();
        _textMapGeneration++;
        _textPageMaps.Clear();
        _textPageMapsLoading.Clear();
        SelectionPageIndex = -1;
    }

    internal void SetAnnotations(IReadOnlyList<PdfTextAnnotation> annotations)
    {
        _annotations = annotations ?? throw new ArgumentNullException(nameof(annotations));
        DrawAnnotationOverlays();
    }

    internal void SetTextSelection(int pageIndex, IReadOnlyList<PdfSearchRect> rectangles)
    {
        ClearTextSelection();
        if (DocumentState == null ||
            (uint)pageIndex >= (uint)DocumentState.PageCount ||
            rectangles.Count == 0)
            return;

        const double pointToLayoutPixel = 96.0 / 72.0;
        double pageTop = GetPageTopLp(pageIndex);
        var fill = new SolidColorBrush(Color.FromArgb(105, 0, 120, 215));
        foreach (PdfSearchRect rect in rectangles)
        {
            var overlay = new Rectangle
            {
                Width = rect.Width * pointToLayoutPixel,
                Height = rect.Height * pointToLayoutPixel,
                Fill = fill,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(overlay, rect.X * pointToLayoutPixel);
            Canvas.SetTop(overlay, pageTop + rect.Y * pointToLayoutPixel);
            PdfSelectionCanvas.Children.Add(overlay);
            _selectionOverlays.Add(overlay);
        }
    }

    internal void ClearTextSelection()
    {
        foreach (Rectangle overlay in _selectionOverlays)
            PdfSelectionCanvas.Children.Remove(overlay);
        _selectionOverlays.Clear();
    }

    private void DrawAnnotationOverlays()
    {
        PdfAnnotationCanvas.Children.Clear();
        if (DocumentState == null || _annotations.Count == 0)
            return;

        const double pointToLayoutPixel = 96.0 / 72.0;
        foreach (PdfTextAnnotation annotation in _annotations)
        {
            if ((uint)annotation.PageIndex >= (uint)DocumentState.PageCount)
                continue;

            double pageTop = GetPageTopLp(annotation.PageIndex);
            foreach (PdfSearchRect rect in annotation.Rectangles)
            {
                double x = rect.X * pointToLayoutPixel;
                double y = pageTop + rect.Y * pointToLayoutPixel;
                double width = rect.Width * pointToLayoutPixel;
                double height = rect.Height * pointToLayoutPixel;
                if (width <= 0 || height <= 0)
                    continue;

                Color color = Color.FromArgb(
                    (byte)Math.Round(annotation.Opacity * 255),
                    (byte)(annotation.ColorArgb >> 16),
                    (byte)(annotation.ColorArgb >> 8),
                    (byte)annotation.ColorArgb);

                if (annotation.Kind == PdfTextAnnotationKind.Ink)
                {
                    var stroke = new Polyline
                    {
                        Stroke = new SolidColorBrush(color),
                        StrokeThickness = Math.Max(0.5, annotation.Width * pointToLayoutPixel),
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        IsHitTestVisible = false,
                    };
                    foreach (var point in annotation.Points)
                        stroke.Points.Add(new Windows.Foundation.Point(
                            point.X * pointToLayoutPixel,
                            pageTop + point.Y * pointToLayoutPixel));
                    PdfAnnotationCanvas.Children.Add(stroke);
                    break;
                }

                if (annotation.Kind == PdfTextAnnotationKind.Note)
                {
                    var noteIcon = new FontIcon
                    {
                        Glyph = "\uE70B",
                        FontSize = 13,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 120, 0)),
                    };
                    Canvas.SetLeft(noteIcon, x);
                    Canvas.SetTop(noteIcon, y);
                    PdfAnnotationCanvas.Children.Add(noteIcon);
                    continue;
                }

                if (annotation.Kind == PdfTextAnnotationKind.FreeText)
                {
                    var text = new TextBlock
                    {
                        Text = annotation.Contents,
                        Width = width,
                        Height = height,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(color),
                        IsHitTestVisible = false,
                    };
                    Canvas.SetLeft(text, x);
                    Canvas.SetTop(text, y);
                    PdfAnnotationCanvas.Children.Add(text);
                    continue;
                }

                double lineHeight = Math.Clamp(height * 0.09, 1.25, 2.5);
                var mark = new Rectangle
                {
                    Width = width,
                    Height = annotation.Kind == PdfTextAnnotationKind.Highlight ? height : lineHeight,
                    IsHitTestVisible = false,
                    Fill = new SolidColorBrush(color),
                };
                Canvas.SetLeft(mark, x);
                Canvas.SetTop(mark, annotation.Kind switch
                {
                    PdfTextAnnotationKind.Underline => y + height - lineHeight,
                    PdfTextAnnotationKind.Strikeout => y + (height - lineHeight) / 2,
                    _ => y,
                });
                PdfAnnotationCanvas.Children.Add(mark);
            }
        }
    }

    internal void RefreshAnnotationOverlays() => DrawAnnotationOverlays();

    public FrameworkElement Control => this;
    public IReadOnlyList<PageThumbnailItem> Thumbnails => _thumbnails;
    public IReadOnlyList<OutlineItem> Outline => _outline;

    public void NavigateTo(OutlineItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Page > 0)
            OutlineNavigationRequested?.Invoke(this, item.Page);
    }

    internal async Task<IReadOnlyList<OutlineItem>> LoadOutlineAsync(
        CancellationToken cancellationToken)
    {
        PdfDocumentState state = DocumentState ??
            throw new InvalidOperationException("No PDF document is loaded.");
        IReadOnlyList<OutlineItem> outline =
            await PdfAnnotationService.GetOutlineAsync(state, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(DocumentState, state))
            throw new OperationCanceledException("The PDF document changed while loading its outline.");

        _outline = outline;
        return outline;
    }

    internal async Task<IReadOnlyList<PdfTextAnnotation>> LoadAnnotationsAsync(
        CancellationToken cancellationToken)
    {
        PdfDocumentState state = DocumentState ??
            throw new InvalidOperationException("No PDF document is loaded.");
        IReadOnlyList<PdfTextAnnotation> annotations =
            await PdfAnnotationService.GetAnnotationsAsync(state, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(DocumentState, state))
            throw new OperationCanceledException("The PDF document changed while loading annotations.");

        return annotations;
    }

    public void SetZoom(double zoom)
    {
        double targetZoom = Math.Clamp(zoom, 0.1, 6.0);
        DocumentScrollViewer.ChangeView(null, null, (float)targetZoom, disableAnimation: false);
    }

    public void SetFitMode(FitMode mode) => _currentFitMode = mode;

    internal void ApplyFitWidth()
    {
        if (DocumentState == null || ViewportState == null)
            return;

        DocumentScrollViewer.UpdateLayout();
        var (pageWidthPoints, _) = GetRotatedPageSize(0);
        if (pageWidthPoints <= 0)
            return;

        const double sidePadding = 28.0;
        double availableWidth = DocumentScrollViewer.ViewportWidth - sidePadding * 2;
        if (availableWidth > 0)
            SetZoom(availableWidth / (pageWidthPoints * (96.0 / 72.0)));
    }

    internal void ApplyFitPage()
    {
        if (DocumentState == null || ViewportState == null)
            return;

        DocumentScrollViewer.UpdateLayout();
        var (pageWidthPoints, pageHeightPoints) = GetRotatedPageSize(0);
        if (pageWidthPoints <= 0 || pageHeightPoints <= 0)
            return;

        const double sidePadding = 28.0;
        const double topPadding = 16.0;
        double availableWidth = DocumentScrollViewer.ViewportWidth - sidePadding * 2;
        double availableHeight = DocumentScrollViewer.ViewportHeight - topPadding * 2;
        if (availableWidth <= 0 || availableHeight <= 0)
            return;

        double width = pageWidthPoints * (96.0 / 72.0);
        double height = pageHeightPoints * (96.0 / 72.0);
        SetZoom(Math.Min(availableWidth / width, availableHeight / height));
    }

    public async Task<SearchResult[]> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (DocumentState == null || string.IsNullOrWhiteSpace(query))
        {
            _pdfSearchResults = Array.Empty<PdfSearchResult>();
            _searchResults = Array.Empty<SearchResult>();
            _activeSearchIndex = -1;
            return _searchResults;
        }

        IReadOnlyList<PdfSearchResult> nativeResults = await PdfTextService.SearchAsync(
            DocumentState,
            query,
            MatchCase,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        _pdfSearchResults = nativeResults;
        _searchResults = nativeResults.Select(result =>
        {
            var locations = new List<SearchLocation>(result.Rects.Count + 1)
            {
                new TextRangeLocation(result.CharIndex, result.Length),
            };
            locations.AddRange(result.Rects.Select(rect => new PageRegionLocation(
                result.PageIndex,
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height)));
            return new SearchResult(string.Empty, locations);
        }).ToArray();
        _activeSearchIndex = -1;
        return _searchResults;
    }

    public void Highlight(SearchResult result)
    {
        int index = Array.FindIndex(_searchResults, candidate => ReferenceEquals(candidate, result));
        if (index < 0)
            return;

        _activeSearchIndex = index;
        RefreshHighlights();

        PdfSearchResult hit = _pdfSearchResults[index];
        double pageTopLp = GetPageOffsetInDocument(hit.PageIndex) * (96.0 / 72.0);
        double hitTopLp = hit.Rects.Count > 0 ? hit.Rects[0].Y * (96.0 / 72.0) : 0;
        double targetOffset = Math.Max(
            0,
            (pageTopLp + hitTopLp) * CurrentZoom - DocumentScrollViewer.ViewportHeight / 2);
        DocumentScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: true);
        PageNavigationRequested?.Invoke(this, hit.PageIndex + 1);
    }

    public void ClearHighlight()
    {
        _pdfSearchResults = Array.Empty<PdfSearchResult>();
        _searchResults = Array.Empty<SearchResult>();
        _activeSearchIndex = -1;
        SearchHighlightCanvas.Children.Clear();
    }

    internal void RefreshHighlights()
    {
        SearchHighlightCanvas.Children.Clear();
        if (DocumentState == null || ViewportState == null || _pdfSearchResults.Count == 0)
            return;

        var visiblePages = ViewportState.GetVisiblePages(DocumentState);
        for (int resultIndex = 0; resultIndex < _pdfSearchResults.Count; resultIndex++)
        {
            PdfSearchResult result = _pdfSearchResults[resultIndex];
            if (!visiblePages.Any(page => page.PageIndex == result.PageIndex))
                continue;

            bool active = resultIndex == _activeSearchIndex;
            double pageOffsetY = GetPageOffsetInDocument(result.PageIndex);
            foreach (PdfSearchRect rect in result.Rects)
            {
                var highlight = new Border
                {
                    Width = Math.Max(2, rect.Width * (96.0 / 72.0)),
                    Height = Math.Max(2, rect.Height * (96.0 / 72.0)),
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(active
                        ? Color.FromArgb(145, 255, 105, 97)
                        : Color.FromArgb(105, 255, 224, 102)),
                    BorderBrush = new SolidColorBrush(active
                        ? Color.FromArgb(190, 210, 70, 65)
                        : Color.FromArgb(145, 205, 160, 40)),
                    BorderThickness = new Thickness(active ? 1 : 0.5),
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(highlight, rect.X * (96.0 / 72.0));
                Canvas.SetTop(highlight, (pageOffsetY + rect.Y) * (96.0 / 72.0));
                Canvas.SetZIndex(highlight, 10);
                SearchHighlightCanvas.Children.Add(highlight);
            }
        }
    }

    private double GetPageOffsetInDocument(int pageIndex)
    {
        if (DocumentState == null || pageIndex <= 0)
            return 0;

        const double pageGapPt = 12.0 * 72.0 / 96.0;
        double offset = 0;
        for (int i = 0; i < pageIndex && i < DocumentState.PageCount; i++)
        {
            var (_, pageHeight) = DocumentState.GetPageSize(i);
            offset += pageHeight + pageGapPt;
        }
        return offset;
    }

    public async Task LoadAsync(DocumentItem document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(document.FilePath))
            throw new ArgumentException("The document has no file path.", nameof(document));

        await StopThumbnailPrefetchAsync();
        PdfLoadResult result = await _pdfService.LoadDocumentAsync(
            document.FilePath,
            ShowPasswordDialogAsync,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (!result.Success || result.Document == null)
            throw new InvalidOperationException(result.ErrorMessage ?? "Cannot open PDF.");

        DocumentState = result.Document;
        CreateThumbnailItems(result.Document);
    }

    internal void CreateThumbnailItems(PdfDocumentState documentState)
    {
        _thumbnails.Clear();
        const double thumbnailLogicalWidth = 200.0;
        for (int pageIndex = 0; pageIndex < documentState.PageCount; pageIndex++)
        {
            var (pageWidth, pageHeight) = documentState.GetPageSize(pageIndex);
            _thumbnails.Add(new PageThumbnailItem
            {
                PageNumber = pageIndex + 1,
                PageLabel = $"{pageIndex + 1}",
                BoxHeight = thumbnailLogicalWidth * pageHeight / Math.Max(1.0, pageWidth),
                IsSelected = pageIndex == 0,
            });
        }
    }

    internal Task PrefetchThumbnailsAroundAsync(
        int centerPage,
        bool sidebarVisible,
        CancellationToken cancellationToken)
    {
        PdfDocumentState? documentState = DocumentState;
        if (!sidebarVisible || documentState == null)
            return Task.CompletedTask;

        CancelThumbnailPrefetch();
        var prefetchCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _thumbnailPrefetchCts = prefetchCts;
        Task task = PrefetchThumbnailsCoreAsync(centerPage, documentState, prefetchCts);
        _thumbnailPrefetchTask = task;
        return task;
    }

    private async Task PrefetchThumbnailsCoreAsync(
        int centerPage,
        PdfDocumentState documentState,
        CancellationTokenSource prefetchCts)
    {
        CancellationToken token = prefetchCts.Token;
        try
        {
            int firstPage = Math.Max(0, centerPage - 8);
            int lastPage = Math.Min(documentState.PageCount - 1, centerPage + 8);
            var pageQueue = new List<int> { centerPage };
            for (int offset = 1; offset <= 8; offset++)
            {
                if (centerPage + offset <= lastPage)
                    pageQueue.Add(centerPage + offset);
                if (centerPage - offset >= firstPage)
                    pageQueue.Add(centerPage - offset);
            }

            foreach (int pageIndex in pageQueue)
            {
                token.ThrowIfCancellationRequested();
                WriteableBitmap? bitmap = await _pdfService.RenderThumbnailAsync(pageIndex, token);
                if (bitmap == null || token.IsCancellationRequested)
                    continue;

                DispatcherQueue.TryEnqueue(() =>
                {
                    if (ReferenceEquals(DocumentState, documentState) && pageIndex < _thumbnails.Count)
                        _thumbnails[pageIndex].Thumbnail = bitmap;
                });
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Infrastructure.Logging.Logger.Error("[PDFThumbnail] Thumbnail prefetch failed", ex);
        }
        finally
        {
            prefetchCts.Dispose();
        }
    }

    private void CancelThumbnailPrefetch()
    {
        _thumbnailPrefetchCts?.Cancel();
        _thumbnailPrefetchCts = null;
    }

    private async Task StopThumbnailPrefetchAsync()
    {
        Task? prefetchTask = _thumbnailPrefetchTask;
        CancelThumbnailPrefetch();
        if (prefetchTask != null)
            await prefetchTask;
        if (ReferenceEquals(_thumbnailPrefetchTask, prefetchTask))
            _thumbnailPrefetchTask = null;
    }

    internal Task<PdfLoadResult> ReplaceDocumentFromPathAsync(
        string filePath,
        CancellationToken cancellationToken) =>
        _pdfService.ReplaceDocumentFromPathAsync(filePath, cancellationToken);

    internal void ResetDocumentService()
    {
        if (_pdfService is IDisposable disposableService)
            disposableService.Dispose();

        _pdfService = new PdfViewerService();
        DocumentState = null;
    }

    public async Task CleanupAsync()
    {
        CleanupViewportRendering();
        await StopThumbnailPrefetchAsync();
        ResetTextPageMaps();
        _annotations = Array.Empty<PdfTextAnnotation>();
        _outline = Array.Empty<OutlineItem>();
        _thumbnails.Clear();
        ClearTextSelection();
        ResetDocumentService();
    }

    private async Task<string?> ShowPasswordDialogAsync(string? errorMessage)
    {
        var dialog = new ContentDialog
        {
            Title = "Password Required",
            Content = CreatePasswordDialogContent(errorMessage, out var passwordBox),
            PrimaryButtonText = "Open",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        ViewerPopupStyle.Apply(dialog);

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? passwordBox.Password : null;
    }

    private static StackPanel CreatePasswordDialogContent(
        string? errorMessage,
        out PasswordBox passwordBox)
    {
        var stack = new StackPanel { Spacing = 12 };

        if (!string.IsNullOrWhiteSpace(errorMessage) &&
            !errorMessage.Contains("password-protected", StringComparison.OrdinalIgnoreCase))
        {
            stack.Children.Add(new TextBlock
            {
                Text = errorMessage,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Colors.IndianRed),
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = "This PDF is password protected. Enter the password to open it:",
            TextWrapping = TextWrapping.Wrap,
        });

        passwordBox = new PasswordBox
        {
            PlaceholderText = "Enter password",
            Width = 300,
        };
        stack.Children.Add(passwordBox);
        return stack;
    }

    public ScrollViewer DocumentScrollViewerControl => DocumentScrollViewer;
    public Canvas PdfViewportCanvasControl => PdfViewportCanvas;
    public Image ViewportImageControl => ViewportImage;
    public Canvas SearchHighlightCanvasControl => SearchHighlightCanvas;
    public Canvas PdfAnnotationCanvasControl => PdfAnnotationCanvas;
    public Canvas PdfSelectionCanvasControl => PdfSelectionCanvas;
}

public sealed class PdfViewerFactory : IDocumentViewerFactory
{
    private static readonly IReadOnlyCollection<string> Extensions = new[] { ".pdf" };

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public IDocumentViewer Create(ViewerViewModel viewModel) => new PdfViewer();
}
