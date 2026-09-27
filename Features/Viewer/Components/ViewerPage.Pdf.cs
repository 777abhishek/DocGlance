using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuickView.Controls;
using QuickView.Infrastructure.Logging;
using QuickView.Models;
using QuickView.Rendering.Pdf;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Features.Viewer;

public sealed partial class ViewerPage
{
    private float DisplayScale => (float)(XamlRoot?.RasterizationScale ?? 1.0);
    private float NaturalZoom => 96f / PdfRenderer.RenderDpi;

    // Track which pages have been rendered to avoid double-rendering
    private readonly HashSet<int> _renderedPages = new();
    private readonly HashSet<int> _renderedThumbs = new();
    private CancellationTokenSource? _prefetchCts;
    private int _prefetchGeneration;

    private async Task LoadDocumentAsync(DocumentItem doc)
    {
        _doc = doc;
        _cts?.Cancel();
        _prefetchCts?.Cancel();
        _reRenderCts?.Cancel();
        _searchCts?.Cancel();
        _pdfState?.Dispose();
        _pdfState = null;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        PagesPanel.Children.Clear();
        Thumbnails.Clear();
        ThumbnailList.ItemsSource = Thumbnails;
        _outline.Clear();
        OutlineTree.RootNodes.Clear();

        // Reset rotation and fit mode for each new document
        _rotation = 0;
        _fitMode  = FitMode.None;
        FitWidthButton.IsChecked = false;
        FitPageButton.IsChecked  = false;
        _renderedPages.Clear();
        _renderedThumbs.Clear();

        _page = 1; _total = 1;
        PageNumberBox.Text  = "1";
        TotalPagesText.Text = "/ 1";

        // Reset viewer visibility to PDF mode for each new load
        ShowPdfViewer();

        string ext = System.IO.Path.GetExtension(doc.FilePath ?? "").ToLowerInvariant();

        if (ext == ".pdf")
            await LoadPdfAsync(doc.FilePath ?? string.Empty, ct);
        else if (IsMarkdownFile(ext))
            await LoadMarkdownFileAsync(doc.FilePath ?? string.Empty, ct);
        else if (IsCodeFile(ext))
            await LoadCodeFileAsync(doc.FilePath ?? string.Empty, ct);
        else if (IsTextFile(ext))
            await LoadTextFileAsync(doc.FilePath ?? string.Empty, ct);
        else
            ShowInfoCard(doc);
    }

    private async Task LoadPdfAsync(string filePath, CancellationToken ct)
    {
        ShowLoading(true);
        try
        {
            // 1. Open PDF with PDFium & pre-cache page sizes instantaneously
            var opened = await PdfRenderer.OpenDocumentAsync(filePath, ct);
            if (ct.IsCancellationRequested)
            {
                opened.Document?.Dispose();
                return;
            }
            if (!opened.Ok || opened.Document == null)
            {
                ShowErrorCard(opened.Error ?? "Cannot open PDF with PDFium.");
                return;
            }

            _pdfState?.Dispose();
            _pdfState = opened.Document;
            _total = _pdfState.PageCount;

            TotalPagesText.Text = $"/ {_total}";
            PageNumberBox.Text  = "1";
            _page = 1;

            // 2. Load outline from the open document (same PDFium session)
            var outlineState = _pdfState;
            _ = Task.Run(() => LoadOutline(outlineState), ct);

            // Auto-hide the sidebar for single-page documents
            if (_total == 1)
            {
                SidebarButton.IsChecked  = false;
                SidebarCard.Visibility   = Visibility.Collapsed;
                SidebarColumn.Width      = new Microsoft.UI.Xaml.GridLength(0);
            }
            else
            {
                SidebarButton.IsChecked  = true;
                SidebarCard.Visibility   = Visibility.Visible;
                SidebarColumn.Width      = new Microsoft.UI.Xaml.GridLength(240);
            }

            // 3. Create lightweight placeholder UI for ALL pages + thumbnails (instant)
            const double thumbLogicalW = 200.0;
            for (int i = 0; i < _total; i++)
            {
                if (ct.IsCancellationRequested) return;
                var (ptW, ptH) = _pdfState.GetPageSize(i);
                double aspect = ptH / Math.Max(1.0, ptW);

                // Physical pixels at RenderDpi for main view
                double physW = Math.Round(ptW * PdfRenderer.RenderDpi / 96.0);
                double physH = Math.Round(ptH * PdfRenderer.RenderDpi / 96.0);

                // Clean white page placeholder (no heavy active ProgressRing controls)
                var pageBorder = new Border
                {
                    Background   = new SolidColorBrush(Colors.White),
                    CornerRadius = new CornerRadius(4),
                    Tag          = (physW, physH),
                    Child = new Grid
                    {
                        Width  = physW,
                        Height = physH,
                    }
                };
                PagesPanel.Children.Add(pageBorder);

                // Thumbnail item
                Thumbnails.Add(new PageThumbnailItem
                {
                    PageNumber = i + 1,
                    PageLabel  = $"{i + 1}",
                    BoxHeight  = thumbLogicalW * aspect,
                    IsSelected = i == 0,
                });
            }

            // Apply 100% zoom immediately so ScrollViewer never jumps
            _zoom = NaturalZoom;
            DocumentScrollViewer.ChangeView(null, null, NaturalZoom, disableAnimation: true);
            ZoomLabel.Text = "100%";
            WireScrollSync();

            // 4. PROGRESSIVE LOADING: Render first page immediately (<15ms)
            await RenderPageAsync(_pdfState, 0, ct);

            if (ct.IsCancellationRequested) return;

            ShowLoading(false); // Interactive immediately
            DocumentScrollViewer.UpdateLayout();
            ReapplyFitMode();

            // Background prefetching: nearby pages first
            _ = PrefetchPagesAroundAsync(0, _pdfState, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            ShowErrorCard($"Cannot open PDF\n{ex.Message}");
        }
        finally { ShowLoading(false); }
    }

    internal void PrefetchPagesAround(int centerPage)
    {
        if (_pdfState == null || _doc == null) return;
        _prefetchCts?.Cancel();
        _prefetchCts?.Dispose();
        _prefetchCts = CancellationTokenSource.CreateLinkedTokenSource(
            _cts?.Token ?? CancellationToken.None);
        _ = PrefetchPagesAroundAsync(centerPage, _pdfState, _prefetchCts.Token);
    }

    private async Task PrefetchPagesAroundAsync(
        int centerPage, PdfDocumentState pdfState, CancellationToken ct)
    {
        int generation = Interlocked.Increment(ref _prefetchGeneration);

        // Keep ±2 pages around centerPage (fast, low memory)
        int first = Math.Max(0, centerPage - 2);
        int last  = Math.Min(pdfState.PageCount - 1, centerPage + 2);

        // Eviction window: keep rendered bitmaps only for ±6 pages
        int evictFirst = Math.Max(0, centerPage - 6);
        int evictLast  = Math.Min(pdfState.PageCount - 1, centerPage + 6);

        try
        {
            // Evict pages far outside the window
            DispatcherQueue.TryEnqueue(() =>
            {
                for (int i = 0; i < PagesPanel.Children.Count; i++)
                {
                    if (i >= evictFirst && i <= evictLast) continue;
                    if (PagesPanel.Children[i] is not Border border) continue;
                    if (border.Child is not Grid grid || grid.Children.Count < 2) continue; // already placeholder

                    double physW = 0, physH = 0;
                    if (border.Tag is (double ow, double oh)) { physW = ow; physH = oh; }

                    border.Child = new Grid
                    {
                        Width  = physW > 0 ? physW : 100,
                        Height = physH > 0 ? physH : 140,
                    };
                    _renderedPages.Remove(i);
                }
            });

            // Prioritized order: centerPage first, then closest neighbours
            var queue = new List<int> { centerPage };
            for (int offset = 1; offset <= 2; offset++)
            {
                if (centerPage + offset <= last) queue.Add(centerPage + offset);
                if (centerPage - offset >= first) queue.Add(centerPage - offset);
            }

            foreach (int pageIndex in queue)
            {
                ct.ThrowIfCancellationRequested();
                if (generation != _prefetchGeneration) return;
                await RenderPageAsync(pdfState, pageIndex, ct);
            }

            // Once main pages are done, prefetch visible thumbnails if sidebar is visible
            if (SidebarCard.Visibility == Visibility.Visible && generation == _prefetchGeneration)
            {
                _ = PrefetchThumbnailsAroundAsync(centerPage, pdfState, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task PrefetchThumbnailsAroundAsync(
        int centerPage, PdfDocumentState pdfState, CancellationToken ct)
    {
        if (SidebarCard.Visibility != Visibility.Visible) return;

        int first = Math.Max(0, centerPage - 4);
        int last  = Math.Min(pdfState.PageCount - 1, centerPage + 4);

        var queue = new List<int> { centerPage };
        for (int offset = 1; offset <= 4; offset++)
        {
            if (centerPage + offset <= last) queue.Add(centerPage + offset);
            if (centerPage - offset >= first) queue.Add(centerPage - offset);
        }

        foreach (int pageIndex in queue)
        {
            ct.ThrowIfCancellationRequested();
            await RenderThumbnailAsync(pdfState, pageIndex, ct);
        }
    }

    /// <summary>Renders a single page in the main view using PDFium</summary>
    private async Task RenderPageAsync(PdfDocumentState pdfState, int pageIdx, CancellationToken ct)
    {
        if (!_renderedPages.Add(pageIdx)) return;

        try
        {
            var bmp = await PdfRenderer.RenderPageAsync(pdfState, pageIdx, PdfRenderer.RenderDpi, ct);
            if (bmp == null || ct.IsCancellationRequested)
            {
                _renderedPages.Remove(pageIdx);
                return;
            }

            var (ptW, ptH) = pdfState.GetPageSize(pageIdx);
            double physW = Math.Round(ptW * PdfRenderer.RenderDpi / 96.0);
            double physH = Math.Round(ptH * PdfRenderer.RenderDpi / 96.0);

            // Update UI on dispatcher thread
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!ReferenceEquals(_pdfState, pdfState)) return;
                if (pageIdx >= PagesPanel.Children.Count) return;
                if (PagesPanel.Children[pageIdx] is not Border border) return;

                var pageGrid = new Grid
                {
                    Width               = physW,
                    Height              = physH,
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                pageGrid.Children.Add(new Image
                {
                    Source  = bmp,
                    Stretch = Stretch.None,
                    Width   = physW,
                    Height  = physH,
                });
                pageGrid.Children.Add(CreateHighlightCanvas(pageIdx, physW, physH));
                border.Child = pageGrid;
                DrawHighlights(pageIdx);
            });
        }
        catch (Exception ex)
        {
            _renderedPages.Remove(pageIdx);
            Logger.Log($"[PDF ERROR] Page {pageIdx}: {ex.Message}");
        }
    }

    /// <summary>Renders a single thumbnail using PDFium</summary>
    private async Task RenderThumbnailAsync(PdfDocumentState pdfState, int pageIdx, CancellationToken ct)
    {
        if (!_renderedThumbs.Add(pageIdx)) return;

        try
        {
            var bmp = await PdfRenderer.RenderPageAsync(pdfState, pageIdx, PdfRenderer.ThumbnailDpi, ct);
            if (bmp == null || ct.IsCancellationRequested)
            {
                _renderedThumbs.Remove(pageIdx);
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (!ReferenceEquals(_pdfState, pdfState)) return;
                if (pageIdx < Thumbnails.Count)
                    Thumbnails[pageIdx].Thumbnail = bmp;
            });
        }
        catch (Exception ex)
        {
            _renderedThumbs.Remove(pageIdx);
            Logger.Log($"[PDF ERROR] Thumbnail {pageIdx}: {ex.Message}");
        }
    }

    private void ShowInfoCard(DocumentItem doc)
    {
        _total = 1;
        TotalPagesText.Text = "/ 1";
        PageNumberBox.Text  = "1";

        Thumbnails.Add(new PageThumbnailItem
        {
            PageNumber = 1, PageLabel = "1", BoxHeight = 200, IsSelected = true,
        });
        ThumbnailList.ItemsSource = Thumbnails;

        var card = new ViewerInfoCard(doc);
        card.OpenRequested += (_, _) => OpenExternalButton_Click(card, new RoutedEventArgs());
        PagesPanel.Children.Add(card);
    }

    private void ShowErrorCard(string message)
    {
        var card = new Border
        {
            Width           = 420,
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(12),
            Padding         = new Thickness(32),
            Background      = ResBrush("CardBackgroundFillColorDefaultBrush"),
            BorderBrush     = ResBrush("CardStrokeColorDefaultBrush"),
        };
        var stack = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new FontIcon
        {
            Glyph = "\uEA39", FontSize = 48,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text          = message,
            TextWrapping  = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
        });
        card.Child = stack;
        PagesPanel.Children.Add(card);
    }
}
