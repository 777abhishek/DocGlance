using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DocGlance.Services.Storage;
using DocGlance.Core.Models;
using DocGlance.Rendering.Document;
using DocGlance.Rendering.Pdf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DocGlance.Features.Viewer;

public sealed partial class ViewerPage
{
    // ── Page navigation ───────────────────────────────────────────────────────

    // Guard: prevents ThumbnailList.SelectedIndex assignment from re-firing ItemClick
    private bool _suppressThumbnailSync;
    private IReadOnlyList<PdfTextAnnotation> _pdfAnnotations = Array.Empty<PdfTextAnnotation>();

    private void SetOutlineItems(IReadOnlyList<OutlineItem> items)
    {
        _outline = items.ToList();
        OutlineTree.RootNodes.Clear();
        foreach (var item in _outline)
            OutlineTree.RootNodes.Add(BuildNode(item));

        bool showingOutline = OutlineTab.IsChecked == true;
        OutlineTree.Visibility = showingOutline && _outline.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        NoOutlineLabel.Visibility = showingOutline && _outline.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SetPdfAnnotations(IReadOnlyList<PdfTextAnnotation> annotations)
    {
        _pdfAnnotations = annotations;
        NotesList.ItemsSource = annotations
            .Where(annotation => annotation.Kind == PdfTextAnnotationKind.Note)
            .Select(annotation => new PdfNoteListItem(annotation))
            .ToList();
        if (NotesTab.IsChecked == true)
            UpdateNotesVisibility();
    }

    private async Task LoadDocxOutlineAsync(string filePath, CancellationToken cancellationToken)
    {
        var document = _docxDocument;
        if (document == null)
        {
            SetOutlineItems(Array.Empty<OutlineItem>());
            return;
        }

        try
        {
            var items = await Task.Run(() =>
            {
                var outline = DocxDocument.ReadOutline(filePath).ToList();
                var pending = FlattenOutline(outline).ToList();
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
                return outline;
            }, cancellationToken);
            if (ReferenceEquals(_docxDocument, document) && !cancellationToken.IsCancellationRequested)
                SetOutlineItems(items);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Infrastructure.Logging.Logger.Error($"[Sidebar] Could not read DOCX headings from {filePath}", ex);
            if (ReferenceEquals(_docxDocument, document))
                SetOutlineItems(Array.Empty<OutlineItem>());
        }
    }

    private async Task LoadPptxOutlineAsync(CancellationToken cancellationToken)
    {
        var document = _pptxDocument;
        if (document == null)
        {
            SetOutlineItems(Array.Empty<OutlineItem>());
            return;
        }

        try
        {
            var items = await Task.Run(() =>
            {
                var outline = new List<OutlineItem>(document.SlideCount);
                for (int slide = 0; slide < document.SlideCount; slide++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string title;
                    try
                    {
                        title = document.GetSlideTitle(slide).Trim();
                        if (title.Length == 0)
                        {
                            var textMap = document.GetSlideTextMap(slide);
                            title = document.GetSlideTextRange(slide, 0, Math.Min(textMap.GlyphCount, 256))
                                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(line => line.Trim())
                                .FirstOrDefault(line => line.Length > 0) ?? $"Slide {slide + 1}";
                        }
                    }
                    catch (Exception ex)
                    {
                        Infrastructure.Logging.Logger.Error($"[Sidebar] Could not read title for slide {slide + 1}", ex);
                        title = $"Slide {slide + 1}";
                    }
                    outline.Add(new OutlineItem { Title = title, Page = slide + 1, Level = 0 });
                }
                return outline;
            }, cancellationToken);
            if (ReferenceEquals(_pptxDocument, document) && !cancellationToken.IsCancellationRequested)
                SetOutlineItems(items);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Infrastructure.Logging.Logger.Error("[Sidebar] Could not load presentation slide titles", ex);
            if (ReferenceEquals(_pptxDocument, document))
                SetOutlineItems(Array.Empty<OutlineItem>());
        }
    }

    private static IEnumerable<OutlineItem> FlattenOutline(IEnumerable<OutlineItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in FlattenOutline(item.Children))
                yield return child;
        }
    }

    private static string NormalizeOutlineText(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    internal void GoToPage(int n)
    {
        ScrollToPage(n);
    }

    /// <summary>
    /// VIEWPORT ARCHITECTURE: Scroll to a specific page by calculating its offset in the document.
    /// For PPTX: render the requested slide instead.
    /// </summary>
    private void ScrollToPage(int pageNumber)
    {
        pageNumber = Math.Clamp(pageNumber, 1, _total);
        UpdateCurrentPage(pageNumber);

        // PPTX: render the requested slide
        if (_pptxDocument != null)
        {
            const double PptxPtToLp = 96.0 / 72.0;
            const double PptxPageGapLp = 12.0;
            const double VerticalPaddingLp = 20.0;
            var (_, slideHeightPt) = _pptxDocument.GetSlideSize(0);
            double slideHeightLp = slideHeightPt * PptxPtToLp;
            double pptxOffsetLp = VerticalPaddingLp +
                (pageNumber - 1) * (slideHeightLp + PptxPageGapLp);

            DocumentScrollViewer.ChangeView(
                null,
                pptxOffsetLp * DocumentScrollViewer.ZoomFactor,
                null,
                disableAnimation: true);
            return;
        }

        if (_docxDocument != null)
        {
            double docxOffsetLp = GetDocxPageTopLp(pageNumber - 1);
            DocumentScrollViewer.ChangeView(
                null,
                docxOffsetLp * DocumentScrollViewer.ZoomFactor,
                null,
                disableAnimation: false);
            return;
        }

        if (_spreadsheetDocument != null)
        {
            _ = SpreadsheetViewerControl.SelectSheetAsync(pageNumber - 1);
            return;
        }

        // PDF: scroll to page
        if (_pdfState == null) return;
        
        int pageIndex = pageNumber - 1;

        // Accumulate page heights in PDF points, add gaps — then scale by zoom
        // (the canvas extent is zoom-scaled in the viewport architecture).
        const double PtToLp   = 96.0 / 72.0;
        const double PageGapLp = 12.0;

        double offsetLp = 0;
        for (int i = 0; i < pageIndex && i < _pdfState.PageCount; i++)
        {
            var (_, pageHeightPt) = GetRotatedPageSize(i);
            offsetLp += pageHeightPt * PtToLp + PageGapLp;
        }
        offsetLp *= _zoom;

        // ChangeView takes layout pixels (same unit as Canvas size)
        DocumentScrollViewer.ChangeView(null, offsetLp, null, disableAnimation: true);
    }

    private void UpdateCurrentPage(int page)
    {
        page = Math.Clamp(page, 1, Math.Max(1, _total));
        int index = page - 1;
        int previousIndex = _page - 1;
        _page = page;
        Toolbar.UpdatePageInfo(_page, _total);
        SyncToViewModel();

        if (previousIndex >= 0 && previousIndex < Thumbnails.Count && previousIndex != index)
            Thumbnails[previousIndex].IsSelected = false;
        if (index >= 0 && index < Thumbnails.Count)
            Thumbnails[index].IsSelected = true;

        if (_pdfState != null)
        {
            EnsurePdfTextPageMap(index);
            _ = PrefetchThumbnailsAroundAsync(index, _pdfState, CancellationToken.None);
        }

        SyncThumbnailSelection(index);
    }

    private void SyncThumbnailSelection(int index)
    {
        _suppressThumbnailSync = true;
        try
        {
            if (index < 0 || index >= ThumbnailList.Items.Count)
                return;

            ThumbnailList.SelectedIndex = index;
            if (ThumbnailList.ContainerFromIndex(index) is FrameworkElement thumbnail)
            {
                thumbnail.StartBringIntoView(new BringIntoViewOptions
                {
                    AnimationDesired = true,
                });
            }
            else if (ThumbnailList.SelectedItem != null)
            {
                ThumbnailList.ScrollIntoView(ThumbnailList.SelectedItem);
            }
        }
        finally
        {
            _suppressThumbnailSync = false;
        }
    }

    // ── Scroll → page number + thumbnail sync ────────────────────────────────

    internal void WireScrollSync()
    {
        DocumentScrollViewer.ViewChanged -= OnScrollViewChanged;
        DocumentScrollViewer.ViewChanged += OnScrollViewChanged;
    }

    private void OnScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        // Viewport handler (ViewerPage.Viewport.cs) handles everything now.
        // This is intentionally empty — kept so the wire-up in WireScrollSync
        // doesn't crash, but the viewport's OnViewportViewChanged does the work.
    }

    // ── Thumbnail click ───────────────────────────────────────────────────────

    private void ThumbnailList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (_suppressThumbnailSync) return;
        if (e.ClickedItem is PageThumbnailItem item)
            GoToPage(item.PageNumber);
    }

    // ── Tab switching ─────────────────────────────────────────────────────────

    private void ThumbsTab_Click(object sender, RoutedEventArgs e)
    {
        ThumbsTab.IsChecked     = true;
        OutlineTab.IsChecked    = false;
        BookmarksTab.IsChecked  = false;
        NotesTab.IsChecked      = false;
        ThumbnailList.Visibility    = Visibility.Visible;
        OutlineTree.Visibility      = Visibility.Collapsed;
        NoOutlineLabel.Visibility   = Visibility.Collapsed;
        BookmarkList.Visibility     = Visibility.Collapsed;
        NoBookmarksLabel.Visibility = Visibility.Collapsed;
        NotesList.Visibility        = Visibility.Collapsed;
        NoNotesLabel.Visibility     = Visibility.Collapsed;
    }

    private void OutlineTab_Click(object sender, RoutedEventArgs e)
    {
        ThumbsTab.IsChecked     = false;
        OutlineTab.IsChecked    = true;
        BookmarksTab.IsChecked  = false;
        NotesTab.IsChecked      = false;
        ThumbnailList.Visibility    = Visibility.Collapsed;
        BookmarkList.Visibility     = Visibility.Collapsed;
        NoBookmarksLabel.Visibility = Visibility.Collapsed;
        NotesList.Visibility        = Visibility.Collapsed;
        NoNotesLabel.Visibility     = Visibility.Collapsed;

        bool has = _outline.Count > 0;
        OutlineTree.Visibility    = has ? Visibility.Visible   : Visibility.Collapsed;
        NoOutlineLabel.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BookmarksTab_Click(object sender, RoutedEventArgs e)
    {
        ThumbsTab.IsChecked     = false;
        OutlineTab.IsChecked    = false;
        BookmarksTab.IsChecked  = true;
        NotesTab.IsChecked      = false;
        ThumbnailList.Visibility  = Visibility.Collapsed;
        OutlineTree.Visibility    = Visibility.Collapsed;
        NoOutlineLabel.Visibility = Visibility.Collapsed;
        NotesList.Visibility      = Visibility.Collapsed;
        NoNotesLabel.Visibility   = Visibility.Collapsed;
        RefreshBookmarkList();
    }

    private void NotesTab_Click(object sender, RoutedEventArgs e)
    {
        ThumbsTab.IsChecked     = false;
        OutlineTab.IsChecked    = false;
        BookmarksTab.IsChecked  = false;
        NotesTab.IsChecked      = true;
        ThumbnailList.Visibility  = Visibility.Collapsed;
        OutlineTree.Visibility    = Visibility.Collapsed;
        NoOutlineLabel.Visibility = Visibility.Collapsed;
        BookmarkList.Visibility   = Visibility.Collapsed;
        NoBookmarksLabel.Visibility = Visibility.Collapsed;
        UpdateNotesVisibility();
    }

    private void UpdateNotesVisibility()
    {
        bool hasNotes = NotesList.Items.Count > 0;
        NotesList.Visibility = hasNotes ? Visibility.Visible : Visibility.Collapsed;
        NoNotesLabel.Visibility = hasNotes ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void NotesList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PdfNoteListItem item)
            return;
        GoToPage(item.PageNumber);
        var dialog = new ContentDialog
        {
            Title = $"Note - Page {item.PageNumber}",
            Content = item.Contents,
            CloseButtonText = "Close",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private static TreeViewNode BuildNode(OutlineItem item)
    {
        var node = new TreeViewNode
        {
            Content    = new OutlineNodeContent(item),
            IsExpanded = item.Level == 0 && item.Children.Count > 0,
        };
        foreach (var child in item.Children)
            node.Children.Add(BuildNode(child));
        return node;
    }

    private void OutlineTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TreeViewNode node &&
            node.Content is OutlineNodeContent c && c.Item.Page > 0)
            GoToPage(c.Item.Page);
    }

    // ── Bookmark list ─────────────────────────────────────────────────────────

    internal void RefreshBookmarkList()
    {
        if (_doc?.FilePath == null)
        {
            BookmarkList.Visibility     = Visibility.Collapsed;
            NoBookmarksLabel.Visibility = Visibility.Visible;
            return;
        }

        var bookmarks = BookmarkStore.GetForFile(_doc.FilePath)
                                     .Select(b => new BookmarkListItem(b))
                                     .ToList();

        if (bookmarks.Count == 0)
        {
            BookmarkList.ItemsSource    = null;
            BookmarkList.Visibility     = Visibility.Collapsed;
            NoBookmarksLabel.Visibility = Visibility.Visible;
        }
        else
        {
            BookmarkList.ItemsSource    = bookmarks;
            BookmarkList.Visibility     = Visibility.Visible;
            NoBookmarksLabel.Visibility = Visibility.Collapsed;
        }
    }

    private void BookmarkList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is BookmarkListItem item) GoToPage(item.PageNumber);
    }

    private void RemoveBookmark_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int page && _doc?.FilePath != null)
        {
            BookmarkStore.Remove(_doc.FilePath, page);
            RefreshBookmarkList();
        }
    }
}

// ── Supporting types ──────────────────────────────────────────────────────────

internal sealed class BookmarkListItem
{
    public BookmarkListItem(BookmarkItem b)
    {
        PageNumber  = b.PageNumber;
        Label       = b.Label;
        PageDisplay = $"Page {b.PageNumber}";
    }
    public int    PageNumber  { get; }
    public string Label       { get; }
    public string PageDisplay { get; }
}

internal sealed class OutlineNodeContent
{
    public OutlineItem Item { get; }
    public OutlineNodeContent(OutlineItem item) => Item = item;
    public override string ToString() => Item.Title;
}

internal sealed class PdfNoteListItem
{
    public int PageNumber { get; }
    public string PageDisplay { get; }
    public string Contents { get; }

    public PdfNoteListItem(PdfTextAnnotation annotation)
    {
        PageNumber = annotation.PageIndex + 1;
        PageDisplay = $"Page {PageNumber}";
        Contents = annotation.Contents;
    }
}
