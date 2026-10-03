using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuickView.Services.Documents;
using QuickView.UI.Controls;
using QuickView.Infrastructure.Logging;
using QuickView.Core.Models;
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

    private CancellationTokenSource? _prefetchCts;
    private int _prefetchGeneration;

    // ── PdfViewerService (Phase 1 foundation) ─────────────────────────────
    private IPdfViewerService _pdfService = new PdfViewerService();

    private async Task LoadDocumentAsync(DocumentItem doc)
    {
        _doc = doc;
        _cts?.Cancel();
        _prefetchCts?.Cancel();
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

        _rotation = 0;
        _fitMode  = FitMode.None;
        Toolbar.IsFitWidthChecked = false;
        Toolbar.IsFitPageChecked = false;

        _page = 1; _total = 1;
        Toolbar.UpdatePageInfo(1, 1);

        // Sync reset state to ViewModel
        ViewModel.Reset();
        ViewModel.Document = doc;

        ShowPdfViewer();

        string ext = System.IO.Path.GetExtension(doc.FilePath ?? "").ToLowerInvariant();

        if (ext == ".pdf")
            await LoadPdfAsync(doc.FilePath ?? string.Empty, ct);
        else if (IsCodeFile(ext))
            await LoadCodeFileAsync(doc.FilePath ?? string.Empty, ct);
        else
        {
            // Try RendererRegistry (Office etc.) before falling back to info card
            var renderer = QuickView.Rendering.RendererRegistry.GetRenderer(ext);
            if (renderer != null)
                await LoadViaRendererAsync(renderer, doc.FilePath ?? string.Empty, ct);
            else
                ShowInfoCard(doc);
        }
    }

    private async Task LoadPdfAsync(string filePath, CancellationToken ct)
    {
        ShowLoading(true);
        try
        {
            // Delegate PDF loading to service (Phase 2: LoadDocument extraction)
            var result = await _pdfService.LoadDocumentAsync(
                filePath,
                ShowPasswordDialogAsync, // UI callback for password prompt
                ct);

            if (!result.Success)
            {
                ShowErrorCard(result.ErrorMessage ?? "Cannot open PDF");
                return;
            }

            _pdfState?.Dispose();
            _pdfState = result.Document;
            _total = result.PageCount;
            
            if (_pdfState == null)
            {
                ShowErrorCard("Failed to load PDF document");
                return;
            }

            Toolbar.UpdatePageInfo(_page, _total);
            _page = 1;

            // Sync pagination to ViewModel
            ViewModel.TotalPages  = _total;
            ViewModel.CurrentPage = _page;

            // 2. Load outline from the open document (same PDFium session)
            var outlineState = _pdfState;
            _ = Task.Run(() => LoadOutline(outlineState), ct);

            // Auto-hide the sidebar for single-page documents
            if (_total == 1)
            {
                Toolbar.SidebarIsChecked = false;
                SidebarCard.Visibility   = Visibility.Collapsed;
                SidebarColumn.Width      = new Microsoft.UI.Xaml.GridLength(0);
                ViewModel.SidebarOpen    = false;
            }
            else
            {
                Toolbar.SidebarIsChecked = true;
                SidebarCard.Visibility   = Visibility.Visible;
                SidebarColumn.Width      = new Microsoft.UI.Xaml.GridLength(240);
                ViewModel.SidebarOpen    = true;
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
            ViewModel.NaturalZoom = NaturalZoom;
            ViewModel.Zoom        = _zoom;
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

    // ── F5: Password dialog ───────────────────────────────────────────────────

    private async Task<string?> ShowPasswordDialogAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Password Required",
            Content = CreatePasswordDialogContent(out var passwordBox),
            PrimaryButtonText = "Open",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? passwordBox.Password : null;
    }

    private StackPanel CreatePasswordDialogContent(out PasswordBox passwordBox)
    {
        var stack = new StackPanel { Spacing = 12 };
        
        stack.Children.Add(new TextBlock
        {
            Text = "This PDF is password protected. Enter the password to open it:",
            TextWrapping = TextWrapping.Wrap
        });

        passwordBox = new PasswordBox
        {
            PlaceholderText = "Enter password",
            Width = 300
        };
        stack.Children.Add(passwordBox);

        return stack;
    }

    // ──────────────────────────────────────────────────────────────────────────

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

        // Render ±5 pages around center — enough that normal scrolling never
        // shows a blank page while the next render is queued.
        int first = Math.Max(0, centerPage - 5);
        int last  = Math.Min(pdfState.PageCount - 1, centerPage + 5);

        // Eviction window: ±30 pages.
        // Large enough that fast scrolling never evicts a page the user will
        // immediately scroll back to. At 384 DPI a letter page is ~8 MB;
        // 60 pages × 8 MB = ~480 MB — acceptable on an 8 GB machine.
        // For machines with less RAM this could be tuned down to ±15.
        int evictFirst = Math.Max(0, centerPage - 30);
        int evictLast  = Math.Min(pdfState.PageCount - 1, centerPage + 30);

        // Build render queue outside try so it's accessible throughout
        var queue = new List<int> { centerPage };
        for (int offset = 1; offset <= 5; offset++)
        {
            if (centerPage + offset <= last) queue.Add(centerPage + offset);
            if (centerPage - offset >= first) queue.Add(centerPage - offset);
        }

        try
        {
            // STEP 1 — Evict the XAML placeholders synchronously for pages about
            // to be rendered, so the HashSet check in the service passes cleanly.
            // Only evict pages actually in our render window.
            foreach (int pageIndex in queue)
                _pdfService.EvictPage(pageIndex);

            // STEP 2 — Render the ±5 window. Eviction already cleared the HashSet
            // so every page in the queue will actually render.
            foreach (int pageIndex in queue)
            {
                ct.ThrowIfCancellationRequested();
                if (generation != _prefetchGeneration) return;
                await RenderPageAsync(pdfState, pageIndex, ct);
            }

            // STEP 3 — Evict distant pages from XAML and service cache AFTER
            // rendering is done. Skip if a newer prefetch already started.
            if (generation != _prefetchGeneration) return;

            DispatcherQueue.TryEnqueue(() =>
            {
                for (int i = 0; i < PagesPanel.Children.Count; i++)
                {
                    if (i >= evictFirst && i <= evictLast) continue;
                    if (PagesPanel.Children[i] is not Border border) continue;
                    if (border.Child is not Grid grid || grid.Children.Count < 2) continue;

                    double physW = 0, physH = 0;
                    if (border.Tag is (double ow, double oh)) { physW = ow; physH = oh; }

                    border.Child = new Grid
                    {
                        Width  = physW > 0 ? physW : 100,
                        Height = physH > 0 ? physH : 140,
                    };
                    _pdfService.EvictPage(i);
                }

                // Evict thumbnails outside ±40 pages
                int thumbFirst = Math.Max(0, centerPage - 40);
                int thumbLast  = Math.Min(pdfState.PageCount - 1, centerPage + 40);
                for (int i = 0; i < Thumbnails.Count; i++)
                {
                    if (i >= thumbFirst && i <= thumbLast) continue;
                    if (_pdfService.IsThumbnailRendered(i))
                    {
                        Thumbnails[i].Thumbnail = null;
                        _pdfService.EvictThumbnail(i);
                    }
                }
            });

            // STEP 3 — Thumbnails after pages (lower priority)
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

    /// <summary>Renders a single page at full quality and applies it to the UI.</summary>
    private async Task RenderPageAsync(PdfDocumentState pdfState, int pageIdx, CancellationToken ct)
    {
        var (ptW, ptH) = pdfState.GetPageSize(pageIdx);
        double physW = Math.Round(ptW * PdfRenderer.RenderDpi / 96.0);
        double physH = Math.Round(ptH * PdfRenderer.RenderDpi / 96.0);

        var bmp = await _pdfService.RenderPageProgressiveAsync(pageIdx, ct: ct);
        if (bmp == null || ct.IsCancellationRequested) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(_pdfState, pdfState)) return;
            // Stretch.Uniform preserves aspect ratio; Stretch.None is 1:1 pixel.
            // We want exactly physW×physH at 1:1 — no stretching whatsoever.
            ApplyPageVisual(pageIdx, pdfState, bmp, physW, physH, Stretch.None);
            DrawHighlights(pageIdx);
        });
    }

    private void ApplyPageVisual(
        int pageIdx, PdfDocumentState pdfState,
        Microsoft.UI.Xaml.Media.ImageSource bmp,
        double physW, double physH, Stretch stretch)
    {
        if (pageIdx >= PagesPanel.Children.Count) return;
        if (PagesPanel.Children[pageIdx] is not Border border) return;

        if (border.Child is Grid existing &&
            existing.Children.Count > 0 &&
            existing.Children[0] is Image img)
        {
            img.Source  = bmp;
            img.Width   = physW;
            img.Height  = physH;
            img.Stretch = stretch;
            existing.Width  = physW;
            existing.Height = physH;
            return;
        }

        var pageGrid = new Grid
        {
            Width               = physW,
            Height              = physH,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        pageGrid.Children.Add(new Image
        {
            Source  = bmp,
            Stretch = stretch,
            Width   = physW,
            Height  = physH,
        });
        pageGrid.Children.Add(CreateHighlightCanvas(pageIdx, physW, physH));
        var textOverlay = new PdfTextSelectionOverlay
        {
            Width               = physW,
            Height              = physH,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
        };
        textOverlay.SetPage(pdfState, pageIdx, physW, physH);
        pageGrid.Children.Add(textOverlay);
        border.Child = pageGrid;
    }

    /// <summary>Renders a single thumbnail via the service</summary>
    private async Task RenderThumbnailAsync(PdfDocumentState pdfState, int pageIdx, CancellationToken ct)
    {
        var bmp = await _pdfService.RenderThumbnailAsync(pageIdx, ct);
        if (bmp == null || ct.IsCancellationRequested) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(_pdfState, pdfState)) return;
            if (pageIdx < Thumbnails.Count)
                Thumbnails[pageIdx].Thumbnail = bmp;
        });
    }

    private void ShowInfoCard(DocumentItem doc)
    {
        _total = 1;
        Toolbar.UpdatePageInfo(1, 1);
        // PageNumberBox synced via UpdatePageInfo;

        Thumbnails.Add(new PageThumbnailItem
        {
            PageNumber = 1, PageLabel = "1", BoxHeight = 200, IsSelected = true,
        });
        ThumbnailList.ItemsSource = Thumbnails;

        var card = new ViewerInfoCard(doc);
        card.OpenRequested += (_, _) => OpenExternalButton_Click(card, EventArgs.Empty);
        PagesPanel.Children.Add(card);
    }

    // ── A1: Renderer Registry integration ─────────────────────────────────────

    private async Task LoadViaRendererAsync(QuickView.Core.Interfaces.IFileRenderer renderer,
                                           string filePath, CancellationToken ct)
    {
        try
        {
            // PERF-05: Clean up previous renderer before loading new one
            if (_currentRenderer != null && _currentRenderedElement != null)
            {
                try
                {
                    await _currentRenderer.CleanupAsync(_currentRenderedElement);
                }
                catch
                {
                    // Swallow cleanup exceptions
                }
                _currentRenderer = null;
                _currentRenderedElement = null;
            }

            ShowLoading(true);
            var element = await renderer.RenderAsync(filePath, ct);
            ShowLoading(false);

            if (ct.IsCancellationRequested || element == null)
                return;

            // Track for cleanup later
            _currentRenderer = renderer;
            _currentRenderedElement = element;

            // Hide all non-PDF viewers but KEEP DocumentScrollViewer visible —
            // PagesPanel lives inside it, so collapsing it would hide the content.
            PlainTextViewerGrid.Visibility  = Visibility.Collapsed;
            TextViewerScroll.Visibility     = Visibility.Collapsed;
            MarkdownViewerHost.Visibility   = Visibility.Collapsed;
            DocumentScrollViewer.Visibility = Visibility.Visible;
            
            // Place the rendered element directly into PagesPanel
            PagesPanel.Children.Clear();
            PagesPanel.Children.Add(element);
            
            // Hide toolbar buttons not applicable to generic renderer view
            ZoomPill.Visibility        = Visibility.Collapsed;
            Toolbar.WordWrapVisibility = Visibility.Collapsed;
            Toolbar.SidebarButtonVisibility = Visibility.Collapsed;
            SidebarCard.Visibility     = Visibility.Collapsed;
            SidebarColumn.Width        = new GridLength(0);
            Toolbar.FitWidthButtonVisibility = Visibility.Collapsed;
            Toolbar.FitPageButtonVisibility = Visibility.Collapsed;
            
            _total = 1;
            Toolbar.UpdatePageInfo(1, 1);
            // PageNumberBox synced via UpdatePageInfo;
        }
        catch (System.Exception ex)
        {
            ShowLoading(false);
            QuickView.Infrastructure.Logging.Logger.Error($"Renderer failed for {filePath}", ex);
            ShowErrorCard($"Failed to load file: {ex.Message}");
        }
    }

    private void ShowErrorCard(string message)
    {
        ViewModel.ErrorMessage = message;
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
