using DocGlance.Core.Models;
using DocGlance.Core.Enums;
using DocGlance.Features.Viewer.Contracts;
using DocGlance.Infrastructure.Logging;
using DocGlance.Interop.DeckCraft;
using DocGlance.Rendering.Pptx;
using DocGlance.Rendering.Pdf.Models;
using DocGlance.Rendering.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;

namespace DocGlance.Features.Viewer.Formats.Pptx;

public sealed partial class PptxViewer :
    UserControl,
    IDocumentViewer,
    ISupportsZoom,
    ISupportsSearch,
    ISupportsThumbnails,
    ISupportsOutline,
    ISupportsRotation
{
    private const double PointsToLayoutPixels = 96.0 / 72.0;
    private const double SlideGap = 12.0;
    private const double VerticalPadding = 20.0;
    private readonly Dictionary<int, Image> _slideCache = new();
    private readonly Dictionary<int, double> _slideScale = new();
    private readonly ObservableCollection<PageThumbnailItem> _thumbnails = new();
    private IReadOnlyList<PptxSearchMatch> _searchMatches = Array.Empty<PptxSearchMatch>();
    private SearchResult[] _searchResults = Array.Empty<SearchResult>();
    private IReadOnlyList<OutlineItem> _outline = Array.Empty<OutlineItem>();
    private DispatcherTimer? _renderDebounceTimer;
    private CancellationTokenSource? _renderCts;
    private CancellationTokenSource? _documentCts;
    private double _slideWidthLp;
    private double _slideHeightLp;
    private double _slideStrideLp;
    private double _zoom = 1.0;
    private double _rotation;
    private double _naturalZoom = 1.0;
    private FitMode _fitMode;
    private int _currentSlide;
    private int _activeSearchIndex = -1;

    public FrameworkElement Control => this;
    public PptxDocument? Document { get; private set; }
    public ScrollViewer ScrollViewer => DocumentScrollViewer;
    public Canvas Canvas => ViewportCanvas;
    public IReadOnlyList<PageThumbnailItem> Thumbnails => _thumbnails;
    public IReadOnlyList<OutlineItem> Outline => _outline;
    public double CurrentRotation => _rotation;
    public double CurrentZoom => _zoom;
    public FitMode CurrentFitMode => _fitMode;
    public bool MatchCase { get; set; }
    public event EventHandler<double>? ZoomChanged;
    public event EventHandler? FitModeChanged;
    public event EventHandler<int>? PageChanged;

    public PptxViewer()
    {
        InitializeComponent();
        DocumentScrollViewer.ViewChanged += OnViewportChanged;
    }

    public async Task LoadAsync(DocumentItem document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        await CleanupAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (!DeckCraftNative.IsAvailable)
            throw new InvalidOperationException("PPTX renderer not available.");

        Document = await PptxDocument.OpenAsync(document.FilePath ?? string.Empty, cancellationToken)
            ?? throw new InvalidOperationException("Cannot open presentation.");
        _documentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _currentSlide = 0;
        _zoom = 1.0;
        _rotation = 0;
        _fitMode = FitMode.None;
        DocumentScrollViewer.ChangeView(0, 0, 1.0f, disableAnimation: true);
        InitializeSlideGeometry();
        CreateThumbnailPlaceholders();
        await LoadOutlineAsync(document.FilePath ?? string.Empty, cancellationToken);
        DocumentScrollViewer.UpdateLayout();
    }

    public Task CleanupAsync()
    {
        // In-flight render and thumbnail tasks may still register against these tokens.
        _documentCts?.Cancel();
        _documentCts = null;
        _renderDebounceTimer?.Stop();
        _renderCts?.Cancel();
        _renderCts = null;

        foreach (Image image in _slideCache.Values)
        {
            ViewportCanvas.Children.Remove(image);
            image.Source = null;
        }
        _slideCache.Clear();
        _slideScale.Clear();
        SearchHighlightCanvas.Children.Clear();
        _thumbnails.Clear();
        _searchMatches = Array.Empty<PptxSearchMatch>();
        _searchResults = Array.Empty<SearchResult>();
        _activeSearchIndex = -1;
        _outline = Array.Empty<OutlineItem>();

        PptxDocument? document = Document;
        Document = null;
        document?.Dispose();
        _slideWidthLp = 0;
        _slideHeightLp = 0;
        _slideStrideLp = 0;
        _currentSlide = 0;
        _zoom = 1.0;
        _naturalZoom = 1.0;
        _fitMode = FitMode.None;
        return Task.CompletedTask;
    }

    public void SetRotation(double angle)
    {
        double rotation = PageRotation.Normalize(angle);
        if (Math.Abs(rotation - _rotation) < 0.001)
            return;

        _rotation = rotation;
        InitializeSlideGeometry();
        foreach (PageThumbnailItem thumbnail in _thumbnails)
            thumbnail.RotationAngle = rotation;
        foreach (var (index, image) in _slideCache)
            PositionSlideImage(index, image);
        RefreshSearchHighlights();
        DocumentScrollViewer.UpdateLayout();
        NavigateToPage(_currentSlide + 1);
        RenderViewportAsync().SafeFireAndForget("PptxRotationRender");
    }

    public void SetZoom(double zoom)
    {
        if (zoom <= 0 || !double.IsFinite(zoom))
            throw new ArgumentOutOfRangeException(nameof(zoom));
        _fitMode = FitMode.None;
        DocumentScrollViewer.ChangeView(null, null, (float)zoom, disableAnimation: false);
    }

    public void SetFitMode(FitMode mode)
    {
        _fitMode = mode;
        FitModeChanged?.Invoke(this, EventArgs.Empty);
        if (mode == FitMode.None)
            return;
        ApplyFitMode();
    }

    public void ApplyFitMode()
    {
        DocumentScrollViewer.UpdateLayout();
        if (_slideWidthLp <= 0 || _slideHeightLp <= 0)
            return;

        const double sidePadding = 28.0;
        double availableWidth = DocumentScrollViewer.ViewportWidth - sidePadding * 2;
        if (availableWidth <= 0)
            return;

        double targetZoom = availableWidth / _slideWidthLp;
        if (_fitMode == FitMode.FitPage)
        {
            const double verticalPadding = 16.0;
            double availableHeight = DocumentScrollViewer.ViewportHeight - verticalPadding * 2;
            if (availableHeight <= 0)
                return;
            targetZoom = Math.Min(targetZoom, availableHeight / _slideHeightLp);
        }

        _naturalZoom = targetZoom;
        SetZoomCore(targetZoom);
    }

    public async Task<SearchResult[]> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        PptxDocument? document = Document;
        if (document == null || string.IsNullOrWhiteSpace(query))
        {
            _searchMatches = Array.Empty<PptxSearchMatch>();
            _searchResults = Array.Empty<SearchResult>();
            _activeSearchIndex = -1;
            SearchHighlightCanvas.Children.Clear();
            return _searchResults;
        }

        bool matchCase = MatchCase;
        IReadOnlyList<PptxSearchMatch> matches = await Task.Run(
            () => document.SearchText(query, matchCase),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(Document, document))
            throw new OperationCanceledException(
                "The presentation changed during search.",
                cancellationToken);

        _searchMatches = matches;
        _searchResults = matches
            .Select(match => new SearchResult(
                string.Empty,
                match.Rects.Select(rect => (SearchLocation)new PageRegionLocation(
                    match.SlideIndex,
                    rect.X,
                    rect.Y,
                    rect.Width,
                    rect.Height)).ToArray()))
            .ToArray();
        _activeSearchIndex = -1;
        return _searchResults;
    }

    public void Highlight(SearchResult result)
    {
        int index = Array.FindIndex(_searchResults, candidate => ReferenceEquals(candidate, result));
        if (index < 0)
            return;

        _activeSearchIndex = index;
        RefreshSearchHighlights();
        PptxSearchMatch match = _searchMatches[index];
        if (match.Rects.Count == 0)
            return;

        var (widthPt, heightPt) = Document!.GetSlideSize(match.SlideIndex);
        PptxSearchRect rect = match.Rects[0];
        Rect rotatedRect = PageRotation.RotateRect(
            new Rect(
                rect.X * PointsToLayoutPixels,
                rect.Y * PointsToLayoutPixels,
                rect.Width * PointsToLayoutPixels,
                rect.Height * PointsToLayoutPixels),
            widthPt * PointsToLayoutPixels,
            heightPt * PointsToLayoutPixels,
            _rotation);
        double hitTop = GetSlideTopLp(match.SlideIndex) + rotatedRect.Y;
        double targetOffset = Math.Max(
            0,
            hitTop * DocumentScrollViewer.ZoomFactor - DocumentScrollViewer.ViewportHeight / 2);
        DocumentScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: true);
        SetCurrentSlide(match.SlideIndex);
    }

    public void ClearHighlight()
    {
        _searchMatches = Array.Empty<PptxSearchMatch>();
        _searchResults = Array.Empty<SearchResult>();
        _activeSearchIndex = -1;
        SearchHighlightCanvas.Children.Clear();
    }

    public void NavigateTo(OutlineItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        NavigateToPage(item.Page);
    }

    public void NavigateToPage(int pageNumber)
    {
        if (Document == null || pageNumber < 1 || pageNumber > Document.SlideCount)
            return;

        DocumentScrollViewer.ChangeView(
            null,
            GetSlideTopLp(pageNumber - 1) * DocumentScrollViewer.ZoomFactor,
            null,
            disableAnimation: true);
        SetCurrentSlide(pageNumber - 1);
    }

    public double GetSlideTopLp(int slideIndex) =>
        slideIndex >= 0 && Document != null && slideIndex < Document.SlideCount
            ? VerticalPadding + slideIndex * _slideStrideLp
            : VerticalPadding;

    public double GetSlideHeightLp(int slideIndex) => Document != null &&
        (uint)slideIndex < (uint)Document.SlideCount
            ? _slideHeightLp
            : 0;

    public int FindSlide(double canvasY)
    {
        if (Document == null || Document.SlideCount == 0)
            return -1;
        int index = (int)Math.Floor((canvasY - VerticalPadding) / _slideStrideLp);
        return (uint)index < (uint)Document.SlideCount ? index : -1;
    }

    public async Task RenderViewportAsync(CancellationToken cancellationToken = default)
    {
        PptxDocument? document = Document;
        if (document == null || _slideStrideLp <= 0)
            return;

        try
        {
            double zoom = Math.Max(0.01, DocumentScrollViewer.ZoomFactor);
            double visibleTop = DocumentScrollViewer.VerticalOffset / zoom;
            double visibleBottom = visibleTop + DocumentScrollViewer.ViewportHeight / zoom;
            int firstSlide = Math.Max(0,
                (int)Math.Floor((visibleTop - VerticalPadding) / _slideStrideLp) - 1);
            int lastSlide = Math.Min(document.SlideCount - 1,
                (int)Math.Floor((visibleBottom - VerticalPadding) / _slideStrideLp) + 1);
            float displayScale = (float)(XamlRoot?.RasterizationScale ?? 1.0);
            var (widthPt, heightPt) = document.GetSlideSize(0);
            double renderScale = RenderBudget.OfficeScale(_zoom, displayScale, widthPt, heightPt);

            for (int i = firstSlide; i <= lastSlide; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool needsRender = !_slideCache.ContainsKey(i) ||
                    !_slideScale.TryGetValue(i, out double cachedScale) ||
                    RenderBudget.NeedsRerenderScale(cachedScale, renderScale);
                if (!needsRender)
                    continue;

                WriteableBitmap? bitmap = await document.RenderSlideAsync(i, renderScale, cancellationToken);
                if (bitmap == null)
                    continue;

                var image = new Image
                {
                    Source = bitmap,
                    Width = widthPt * PointsToLayoutPixels,
                    Height = heightPt * PointsToLayoutPixels,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                    UseLayoutRounding = true,
                };
                PositionSlideImage(i, image);
                Canvas.SetZIndex(image, 0);
                if (_slideCache.TryGetValue(i, out Image? oldImage))
                {
                    ViewportCanvas.Children.Remove(oldImage);
                    oldImage.Source = null;
                }
                ViewportCanvas.Children.Add(image);
                _slideCache[i] = image;
                _slideScale[i] = renderScale;
            }

            int[] staleSlides = _slideCache.Keys
                .Where(index => index < firstSlide || index > lastSlide)
                .ToArray();
            foreach (int index in staleSlides)
            {
                Image image = _slideCache[index];
                ViewportCanvas.Children.Remove(image);
                image.Source = null;
                _slideCache.Remove(index);
                _slideScale.Remove(index);
            }
            RefreshSearchHighlights();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error("PPTX viewer viewport render failed", ex);
        }
    }

    public void StartThumbnailLoading()
    {
        if (Document is { } document && _documentCts is { } documentCts)
            LoadThumbnailsAsync(document, documentCts.Token)
                .SafeFireAndForget("PptxThumbnails");
    }

    private void SetZoomCore(double zoom)
    {
        DocumentScrollViewer.ChangeView(null, null, (float)zoom, disableAnimation: false);
    }

    private void InitializeSlideGeometry()
    {
        PptxDocument document = Document!;
        if (document.SlideCount == 0)
            throw new InvalidOperationException("The presentation contains no slides.");

        var (width, height) = document.GetSlideSize(0);
        (_slideWidthLp, _slideHeightLp) = PageRotation.GetSize(
            width * PointsToLayoutPixels, height * PointsToLayoutPixels, _rotation);
        _slideStrideLp = _slideHeightLp + SlideGap;
        ViewportCanvas.Width = _slideWidthLp;
        ViewportCanvas.Height = VerticalPadding * 2 +
            document.SlideCount * _slideHeightLp +
            Math.Max(0, document.SlideCount - 1) * SlideGap;
        SearchHighlightCanvas.Width = ViewportCanvas.Width;
        SearchHighlightCanvas.Height = ViewportCanvas.Height;
        Canvas.SetZIndex(SearchHighlightCanvas, 10);
    }

    private void PositionSlideImage(int slideIndex, Image image)
    {
        var (widthPt, heightPt) = Document!.GetSlideSize(slideIndex);
        double width = widthPt * PointsToLayoutPixels;
        double height = heightPt * PointsToLayoutPixels;
        (double rotatedWidth, double rotatedHeight) =
            PageRotation.GetSize(width, height, _rotation);
        image.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        image.RenderTransform = new Microsoft.UI.Xaml.Media.RotateTransform { Angle = _rotation };
        Canvas.SetLeft(image, (rotatedWidth - width) / 2);
        Canvas.SetTop(image, GetSlideTopLp(slideIndex) + (rotatedHeight - height) / 2);
    }

    private void CreateThumbnailPlaceholders()
    {
        PptxDocument document = Document!;
        _thumbnails.Clear();
        for (int i = 0; i < document.SlideCount; i++)
        {
            _thumbnails.Add(new PageThumbnailItem
            {
                PageNumber = i + 1,
                PageLabel = $"Slide {i + 1}",
                IsSelected = i == 0,
                BoxHeight = _slideWidthLp > 0 ? 200 * _slideHeightLp / _slideWidthLp : 200 * 0.5625,
                RotationAngle = _rotation,
            });
        }
    }

    private void OnViewportChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (Document == null)
            return;

        double zoom = DocumentScrollViewer.ZoomFactor;
        if (Math.Abs(zoom - _zoom) > 0.001)
        {
            _zoom = zoom;
            ZoomChanged?.Invoke(this, zoom);
        }

        _renderDebounceTimer?.Stop();
        _renderCts?.Cancel();
        if (e.IsIntermediate)
            return;

        UpdateCurrentSlide();
        RefreshSearchHighlights();
        _renderDebounceTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        _renderDebounceTimer.Tick -= OnRenderDebounceTimerTick;
        _renderDebounceTimer.Tick += OnRenderDebounceTimerTick;
        _renderDebounceTimer.Start();
    }

    private void OnRenderDebounceTimerTick(object? sender, object e)
    {
        _renderDebounceTimer?.Stop();
        _renderCts?.Cancel();
        _renderCts = new CancellationTokenSource();
        RenderViewportAsync(_renderCts.Token).SafeFireAndForget("PptxViewportRender");
    }

    private void UpdateCurrentSlide()
    {
        if (Document == null || _slideStrideLp <= 0)
            return;
        double zoom = Math.Max(0.01, DocumentScrollViewer.ZoomFactor);
        double centerY =
            (DocumentScrollViewer.VerticalOffset + DocumentScrollViewer.ViewportHeight / 2) / zoom;
        int slide = Math.Clamp(
            (int)Math.Floor((centerY - VerticalPadding + _slideHeightLp / 2) / _slideStrideLp),
            0,
            Document.SlideCount - 1);
        SetCurrentSlide(slide);
    }

    private void SetCurrentSlide(int index)
    {
        if (Document == null || (uint)index >= (uint)Document.SlideCount)
            return;
        if (_currentSlide == index)
            return;
        _currentSlide = index;
        if (index < _thumbnails.Count)
        {
            for (int i = 0; i < _thumbnails.Count; i++)
                _thumbnails[i].IsSelected = i == index;
        }
        PageChanged?.Invoke(this, index + 1);
    }

    private void RefreshSearchHighlights()
    {
        SearchHighlightCanvas.Children.Clear();
        if (Document == null || _searchMatches.Count == 0)
            return;

        double zoom = Math.Max(0.01, DocumentScrollViewer.ZoomFactor);
        double visibleTop = DocumentScrollViewer.VerticalOffset / zoom;
        double visibleBottom = visibleTop + DocumentScrollViewer.ViewportHeight / zoom;
        int firstVisible = Math.Max(0,
            (int)Math.Floor((visibleTop - VerticalPadding) / _slideStrideLp));
        int lastVisible = Math.Min(Document.SlideCount - 1,
            (int)Math.Floor((visibleBottom - VerticalPadding) / _slideStrideLp));

        for (int matchIndex = 0; matchIndex < _searchMatches.Count; matchIndex++)
        {
            PptxSearchMatch match = _searchMatches[matchIndex];
            if (match.SlideIndex < firstVisible || match.SlideIndex > lastVisible)
                continue;
            bool active = matchIndex == _activeSearchIndex;
            foreach (PptxSearchRect rect in match.Rects)
            {
                var highlight = new Border
                {
                    Width = Math.Max(2, rect.Width * PointsToLayoutPixels),
                    Height = Math.Max(2, rect.Height * PointsToLayoutPixels),
                    CornerRadius = new CornerRadius(2),
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(
                        active ? (byte)145 : (byte)105,
                        255,
                        active ? (byte)105 : (byte)224,
                        active ? (byte)97 : (byte)102)),
                    BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(
                        active ? (byte)190 : (byte)145,
                        active ? (byte)210 : (byte)205,
                        active ? (byte)70 : (byte)160,
                        active ? (byte)65 : (byte)40)),
                    BorderThickness = new Thickness(active ? 1 : 0.5),
                    IsHitTestVisible = false,
                };
                var (widthPt, heightPt) = Document.GetSlideSize(match.SlideIndex);
                Rect rotatedRect = PageRotation.RotateRect(
                    new Rect(
                        rect.X * PointsToLayoutPixels,
                        rect.Y * PointsToLayoutPixels,
                        rect.Width * PointsToLayoutPixels,
                        rect.Height * PointsToLayoutPixels),
                    widthPt * PointsToLayoutPixels,
                    heightPt * PointsToLayoutPixels,
                    _rotation);
                highlight.Width = Math.Max(2, rotatedRect.Width);
                highlight.Height = Math.Max(2, rotatedRect.Height);
                Canvas.SetLeft(highlight, rotatedRect.X);
                Canvas.SetTop(
                    highlight,
                    GetSlideTopLp(match.SlideIndex) + rotatedRect.Y);
                SearchHighlightCanvas.Children.Add(highlight);
            }
        }
    }

    private async Task LoadOutlineAsync(string filePath, CancellationToken cancellationToken)
    {
        PptxDocument? document = Document;
        if (document == null)
            return;

        try
        {
            var outline = await Task.Run(() =>
            {
                var items = new List<OutlineItem>(document.SlideCount);
                for (int slide = 0; slide < document.SlideCount; slide++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string title = document.GetSlideTitle(slide).Trim();
                    if (title.Length == 0)
                    {
                        PdfPageTextMap textMap = document.GetSlideTextMap(slide);
                        title = document.GetSlideTextRange(slide, 0, Math.Min(textMap.GlyphCount, 256))
                            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(line => line.Trim())
                            .FirstOrDefault(line => line.Length > 0) ?? $"Slide {slide + 1}";
                    }
                    items.Add(new OutlineItem { Title = title, Page = slide + 1, Level = 0 });
                }
                return items;
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(Document, document))
                throw new OperationCanceledException(
                    "The presentation changed while loading its outline.",
                    cancellationToken);
            _outline = outline;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load presentation slide titles", ex);
            _outline = Array.Empty<OutlineItem>();
        }
    }

    private async Task LoadThumbnailsAsync(PptxDocument document, CancellationToken cancellationToken)
    {
        for (int i = 0; i < document.SlideCount && i < 50; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                return;
            try
            {
                WriteableBitmap? thumbnail = await document.RenderSlideAsync(
                    i, RenderBudget.ThumbnailOfficeScale, cancellationToken);
                if (thumbnail != null &&
                    ReferenceEquals(Document, document) &&
                    i < _thumbnails.Count)
                    _thumbnails[i].Thumbnail = thumbnail;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.Error($"LoadPptxThumbnails: slide {i}", ex);
            }
        }
    }
}

public sealed class PptxViewerFactory : IDocumentViewerFactory
{
    private static readonly IReadOnlyCollection<string> Extensions =
        new[] { ".pptx", ".pptm", ".ppsx" };

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public IDocumentViewer Create(ViewerViewModel viewModel) => new PptxViewer();
}
