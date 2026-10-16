using DocGlance.Core.Models;
using DocGlance.Features.Viewer.Contracts;
using DocGlance.Infrastructure.Logging;
using DocGlance.Interop.WordCraft;
using DocGlance.Rendering.Docx;
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
using Windows.UI;

namespace DocGlance.Features.Viewer.Formats.Docx;

public sealed partial class DocxViewer :
    UserControl,
    IDocumentViewer,
    ISupportsSearch,
    ISupportsThumbnails,
    ISupportsOutline
{
    private readonly Dictionary<int, Image> _pageCache = new();
    private readonly Dictionary<int, double> _pageScale = new();
    private readonly ObservableCollection<PageThumbnailItem> _thumbnails = new();
    private IReadOnlyList<DocxDocument.DocxSearchMatch> _searchMatches =
        Array.Empty<DocxDocument.DocxSearchMatch>();
    private SearchResult[] _searchResults = Array.Empty<SearchResult>();
    private IReadOnlyList<OutlineItem> _outline = Array.Empty<OutlineItem>();
    private int _activeSearchIndex = -1;
    private double[] _pageTopLp = Array.Empty<double>();
    private double[] _pageWidthLp = Array.Empty<double>();
    private double[] _pageHeightLp = Array.Empty<double>();
    private DispatcherTimer? _renderDebounceTimer;
    private CancellationTokenSource? _renderCts;
    private CancellationTokenSource? _documentCts;
    private double _zoom = 1.0;
    private int _currentPage;

    public FrameworkElement Control => this;
    public DocxDocument? Document { get; private set; }
    public IReadOnlyList<PageThumbnailItem> Thumbnails => _thumbnails;
    public IReadOnlyList<OutlineItem> Outline => _outline;
    public ScrollViewer ScrollViewer => DocumentScrollViewer;
    public Canvas Canvas => ViewportCanvas;
    public Canvas Highlights => SearchHighlightCanvas;
    public event EventHandler<int>? PageChanged;
    public event EventHandler<double>? ZoomChanged;
    public bool MatchCase { get; set; }

    public DocxViewer()
    {
        InitializeComponent();
        DocumentScrollViewer.ViewChanged += OnViewportChanged;
    }

    public async Task LoadAsync(DocumentItem document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        await CleanupAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (!WordCraftNative.IsAvailable)
            throw new InvalidOperationException(
                "WordCraft DOCX renderer not available. Ensure wordcraft_ffi.dll is present.");

        Document = await DocxDocument.OpenAsync(document.FilePath ?? string.Empty, cancellationToken)
            ?? throw new InvalidOperationException(
                "Cannot open document — file may be corrupt or encrypted.");
        _documentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _currentPage = 0;
        _zoom = 1.0;
        DocumentScrollViewer.ChangeView(0, 0, 1.0f, disableAnimation: true);

        InitializePageGeometry();
        CreateThumbnailPlaceholders();
        await LoadOutlineAsync(document.FilePath ?? string.Empty, cancellationToken);
        DocumentScrollViewer.UpdateLayout();
    }

    public Task CleanupAsync()
    {
        _documentCts?.Cancel();
        _documentCts?.Dispose();
        _documentCts = null;
        _renderDebounceTimer?.Stop();
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = null;

        foreach (Image image in _pageCache.Values)
        {
            ViewportCanvas.Children.Remove(image);
            image.Source = null;
        }
        _pageCache.Clear();
        _pageScale.Clear();
        SearchHighlightCanvas.Children.Clear();
        _thumbnails.Clear();
        _searchMatches = Array.Empty<DocxDocument.DocxSearchMatch>();
        _searchResults = Array.Empty<SearchResult>();
        _activeSearchIndex = -1;
        _outline = Array.Empty<OutlineItem>();

        DocxDocument? document = Document;
        Document = null;
        document?.Dispose();
        _pageTopLp = Array.Empty<double>();
        _pageWidthLp = Array.Empty<double>();
        _pageHeightLp = Array.Empty<double>();
        _currentPage = 0;
        return Task.CompletedTask;
    }

    public void StartThumbnailLoading()
    {
        if (Document is { } document && _documentCts is { } documentCts)
            LoadThumbnailsAsync(document, documentCts.Token)
                .SafeFireAndForget("DocxThumbnails");
    }

    public double GetPageTopLp(int pageIndex) =>
        (uint)pageIndex < (uint)_pageTopLp.Length ? _pageTopLp[pageIndex] : 0;

    public double GetPageHeightLp(int pageIndex) =>
        (uint)pageIndex < (uint)_pageHeightLp.Length ? _pageHeightLp[pageIndex] : 0;

    public (int firstPage, int lastPage) GetVisiblePageRange()
    {
        DocxDocument? document = Document;
        if (document == null || _pageTopLp.Length != document.PageCount)
            return (-1, -1);

        double zoom = Math.Max(0.01, DocumentScrollViewer.ZoomFactor);
        double visibleTop = DocumentScrollViewer.VerticalOffset / zoom;
        double visibleBottom =
            (DocumentScrollViewer.VerticalOffset + DocumentScrollViewer.ViewportHeight) / zoom;
        int firstPage = -1;
        int lastPage = -1;

        for (int i = 0; i < document.PageCount; i++)
        {
            double pageTop = _pageTopLp[i];
            double pageBottom = pageTop + _pageHeightLp[i];
            if (pageBottom >= visibleTop && pageTop <= visibleBottom)
            {
                if (firstPage < 0)
                    firstPage = i;
                lastPage = i;
            }
        }

        return (firstPage, lastPage);
    }

    public int FindPage(double canvasY)
    {
        if (Document == null)
            return -1;

        int nearestPage = -1;
        double nearestDistance = double.MaxValue;
        for (int i = 0; i < _pageTopLp.Length; i++)
        {
            double top = _pageTopLp[i];
            double bottom = top + _pageHeightLp[i];
            if (canvasY >= top && canvasY <= bottom)
                return i;

            double distance = canvasY < top ? top - canvasY : canvasY - bottom;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestPage = i;
            }
        }

        return nearestPage;
    }

    public void SetZoom(double zoom)
    {
        if (zoom <= 0 || !double.IsFinite(zoom))
            throw new ArgumentOutOfRangeException(nameof(zoom));

        DocumentScrollViewer.ChangeView(null, null, (float)zoom, disableAnimation: false);
    }

    public async Task<SearchResult[]> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        DocxDocument? document = Document;
        if (document == null || string.IsNullOrWhiteSpace(query))
        {
            _searchMatches = Array.Empty<DocxDocument.DocxSearchMatch>();
            _searchResults = Array.Empty<SearchResult>();
            _activeSearchIndex = -1;
            SearchHighlightCanvas.Children.Clear();
            return _searchResults;
        }

        bool matchCase = MatchCase;
        IReadOnlyList<DocxDocument.DocxSearchMatch> matches = await Task.Run(
            () => document.SearchText(query, matchCase),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(Document, document))
            throw new OperationCanceledException("The DOCX document changed during search.", cancellationToken);

        _searchMatches = matches;
        _searchResults = matches
            .Select(match => new SearchResult(
                string.Empty,
                match.Rects.Select(rect => (SearchLocation)new PageRegionLocation(
                    rect.PageIndex,
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
        DocxDocument.DocxMatch? hit = _searchMatches[index].Rects.FirstOrDefault();
        if (hit is not { } match)
            return;

        const double ptToLp = 96.0 / 72.0;
        double targetOffset = Math.Max(
            0,
            (GetPageTopLp(match.PageIndex) + match.Y * ptToLp) *
            DocumentScrollViewer.ZoomFactor - DocumentScrollViewer.ViewportHeight / 2);
        DocumentScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: true);
        _currentPage = match.PageIndex;
        PageChanged?.Invoke(this, match.PageIndex + 1);
    }

    public void ClearHighlight()
    {
        _searchMatches = Array.Empty<DocxDocument.DocxSearchMatch>();
        _searchResults = Array.Empty<SearchResult>();
        _activeSearchIndex = -1;
        SearchHighlightCanvas.Children.Clear();
    }

    public void NavigateTo(OutlineItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Document == null || item.Page < 1 || item.Page > Document.PageCount)
            return;

        double targetOffset = GetPageTopLp(item.Page - 1) * DocumentScrollViewer.ZoomFactor;
        DocumentScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: true);
        _currentPage = item.Page - 1;
        PageChanged?.Invoke(this, item.Page);
    }

    public async Task RenderViewportAsync(CancellationToken cancellationToken = default)
    {
        DocxDocument? document = Document;
        if (document == null)
            return;

        try
        {
            var (firstPage, lastPage) = GetVisiblePageRange();
            if (firstPage < 0)
                return;

            int renderStart = Math.Max(0, firstPage - 1);
            int renderEnd = Math.Min(document.PageCount - 1, lastPage + 1);
            float displayScale = (float)(XamlRoot?.RasterizationScale ?? 1.0);

            for (int i = 0; i < document.PageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (i < renderStart || i > renderEnd)
                    continue;

                var (widthPt, heightPt) = document.GetPageSize(i);
                double renderScale = RenderBudget.OfficeScale(
                    _zoom, displayScale, widthPt, heightPt);
                bool needsRender = !_pageCache.ContainsKey(i) ||
                    !_pageScale.TryGetValue(i, out double cachedScale) ||
                    RenderBudget.NeedsRerenderScale(cachedScale, renderScale);
                if (!needsRender)
                    continue;

                WriteableBitmap? bitmap = await document.RenderPageAsync(i, renderScale, cancellationToken);
                if (bitmap == null)
                    continue;

                var image = new Image
                {
                    Source = bitmap,
                    Width = _pageWidthLp[i],
                    Height = _pageHeightLp[i],
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill,
                    UseLayoutRounding = true,
                };
                Canvas.SetLeft(image, 0);
                Canvas.SetTop(image, _pageTopLp[i]);
                Canvas.SetZIndex(image, 0);

                if (_pageCache.TryGetValue(i, out Image? oldImage))
                {
                    ViewportCanvas.Children.Remove(oldImage);
                    oldImage.Source = null;
                }
                ViewportCanvas.Children.Add(image);
                _pageCache[i] = image;
                _pageScale[i] = renderScale;
            }

            int[] toRemove = _pageCache.Keys
                .Where(index => index < renderStart || index > renderEnd)
                .ToArray();
            foreach (int index in toRemove)
            {
                Image image = _pageCache[index];
                ViewportCanvas.Children.Remove(image);
                image.Source = null;
                _pageCache.Remove(index);
                _pageScale.Remove(index);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error("RenderDocxViewportAsync error", ex);
        }
    }

    private void InitializePageGeometry()
    {
        DocxDocument document = Document!;
        const double ptToLp = 96.0 / 72.0;
        _pageTopLp = new double[document.PageCount];
        _pageWidthLp = new double[document.PageCount];
        _pageHeightLp = new double[document.PageCount];
        double totalHeight = 0;
        double maxWidth = 0;

        for (int i = 0; i < document.PageCount; i++)
        {
            var (width, height) = document.GetPageSize(i);
            _pageWidthLp[i] = width * ptToLp;
            _pageHeightLp[i] = height * ptToLp;
            _pageTopLp[i] = totalHeight;
            totalHeight += _pageHeightLp[i];
            if (i < document.PageCount - 1)
                totalHeight += 12;
            maxWidth = Math.Max(maxWidth, _pageWidthLp[i]);
        }

        ViewportCanvas.Width = maxWidth;
        ViewportCanvas.Height = totalHeight;
        SearchHighlightCanvas.Width = maxWidth;
        SearchHighlightCanvas.Height = totalHeight;
        Canvas.SetZIndex(SearchHighlightCanvas, 10);
    }

    private void CreateThumbnailPlaceholders()
    {
        DocxDocument document = Document!;
        _thumbnails.Clear();
        for (int i = 0; i < document.PageCount; i++)
        {
            var (width, height) = document.GetPageSize(i);
            _thumbnails.Add(new PageThumbnailItem
            {
                PageNumber = i + 1,
                PageLabel = $"{i + 1}",
                IsSelected = i == 0,
                BoxHeight = width > 0 ? 154 * height / width : 154 * 1.414,
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

        if (!e.IsIntermediate)
        {
            UpdateCurrentPage();
            RefreshSearchHighlights();
            _renderDebounceTimer ??= new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200),
            };
            _renderDebounceTimer.Tick -= OnRenderDebounceTimerTick;
            _renderDebounceTimer.Tick += OnRenderDebounceTimerTick;
            _renderDebounceTimer.Start();
        }
    }

    private void OnRenderDebounceTimerTick(object? sender, object e)
    {
        _renderDebounceTimer?.Stop();
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = new CancellationTokenSource();
        RenderViewportAsync(_renderCts.Token).SafeFireAndForget("DocxViewportRender");
    }

    private void UpdateCurrentPage()
    {
        DocxDocument? document = Document;
        if (document == null || _pageTopLp.Length != document.PageCount)
            return;

        double zoom = Math.Max(0.01, DocumentScrollViewer.ZoomFactor);
        double centerY =
            (DocumentScrollViewer.VerticalOffset + DocumentScrollViewer.ViewportHeight / 2) / zoom;
        double nearestDistance = double.MaxValue;
        int currentPage = 0;
        for (int i = 0; i < document.PageCount; i++)
        {
            double pageTop = _pageTopLp[i];
            double pageBottom = pageTop + _pageHeightLp[i];
            if (centerY >= pageTop && centerY <= pageBottom)
            {
                currentPage = i;
                break;
            }
            double distance = Math.Abs(centerY - (pageTop + _pageHeightLp[i] / 2));
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                currentPage = i;
            }
        }

        if (_currentPage != currentPage)
        {
            _currentPage = currentPage;
            PageChanged?.Invoke(this, currentPage + 1);
        }
    }

    private void RefreshSearchHighlights()
    {
        SearchHighlightCanvas.Children.Clear();
        if (Document == null || _searchMatches.Count == 0)
            return;

        const double ptToLp = 96.0 / 72.0;
        var (firstVisible, lastVisible) = GetVisiblePageRange();
        if (firstVisible < 0)
            return;

        for (int matchIndex = 0; matchIndex < _searchMatches.Count; matchIndex++)
        {
            bool active = matchIndex == _activeSearchIndex;
            foreach (DocxDocument.DocxMatch rect in _searchMatches[matchIndex].Rects)
            {
                if (rect.PageIndex < firstVisible || rect.PageIndex > lastVisible)
                    continue;

                var highlight = new Border
                {
                    Width = Math.Max(2, rect.Width * ptToLp),
                    Height = Math.Max(2, rect.Height * ptToLp),
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
                Canvas.SetLeft(highlight, rect.X * ptToLp);
                Canvas.SetTop(highlight, GetPageTopLp(rect.PageIndex) + rect.Y * ptToLp);
                SearchHighlightCanvas.Children.Add(highlight);
            }
        }
    }

    private async Task LoadOutlineAsync(string filePath, CancellationToken cancellationToken)
    {
        DocxDocument? document = Document;
        if (document == null)
            return;

        try
        {
            var outline = await Task.Run(() =>
            {
                var items = DocxDocument.ReadOutline(filePath).ToList();
                var pending = FlattenOutline(items).ToList();
                var unresolved = new HashSet<OutlineItem>(pending);
                var normalizedTitles = pending
                    .Select(item => (Item: item, Title: NormalizeOutlineText(item.Title)))
                    .Where(entry => entry.Title.Length > 0)
                    .ToList();

                for (int page = 0; page < document.PageCount && unresolved.Count > 0; page++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string pageText = NormalizeOutlineText(document.ExtractPageText(page));
                    foreach (var (item, title) in normalizedTitles)
                    {
                        if (unresolved.Contains(item) && pageText.Contains(title, StringComparison.Ordinal))
                        {
                            item.Page = page + 1;
                            unresolved.Remove(item);
                        }
                    }
                }
                return items;
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(Document, document))
                throw new OperationCanceledException(
                    "The DOCX document changed while loading its outline.",
                    cancellationToken);

            _outline = outline;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error($"[Sidebar] Could not read DOCX headings from {filePath}", ex);
            _outline = Array.Empty<OutlineItem>();
        }
    }

    private static IEnumerable<OutlineItem> FlattenOutline(IEnumerable<OutlineItem> items)
    {
        foreach (OutlineItem item in items)
        {
            yield return item;
            foreach (OutlineItem child in FlattenOutline(item.Children))
                yield return child;
        }
    }

    private static string NormalizeOutlineText(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private async Task LoadThumbnailsAsync(DocxDocument document, CancellationToken cancellationToken)
    {
        for (int i = 0; i < document.PageCount && i < 50; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                return;
            try
            {
                WriteableBitmap? thumbnail = await document.RenderPageAsync(
                    i, RenderBudget.ThumbnailOfficeScale, cancellationToken);
                if (thumbnail == null)
                    continue;
                if (ReferenceEquals(Document, document) && i < _thumbnails.Count)
                    _thumbnails[i].Thumbnail = thumbnail;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.Error($"LoadDocxThumbnails: page {i}", ex);
            }
        }
    }
}

public sealed class DocxViewerFactory : IDocumentViewerFactory
{
    private static readonly IReadOnlyCollection<string> Extensions = new[] { ".docx", ".doc" };

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public IDocumentViewer Create(ViewerViewModel viewModel) => new DocxViewer();
}
