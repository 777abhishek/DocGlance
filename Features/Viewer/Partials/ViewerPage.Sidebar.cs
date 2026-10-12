using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuickView.Services.Storage;
using QuickView.Core.Models;
using QuickView.Rendering.Pdf;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickView.Features.Viewer;

public sealed partial class ViewerPage
{
    // ── Page navigation ───────────────────────────────────────────────────────

    // Guard: prevents ThumbnailList.SelectedIndex assignment from re-firing ItemClick
    private bool _suppressThumbnailSync;

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
        ThumbnailList.Visibility    = Visibility.Visible;
        OutlineTree.Visibility      = Visibility.Collapsed;
        NoOutlineLabel.Visibility   = Visibility.Collapsed;
        BookmarkList.Visibility     = Visibility.Collapsed;
        NoBookmarksLabel.Visibility = Visibility.Collapsed;
    }

    private void OutlineTab_Click(object sender, RoutedEventArgs e)
    {
        ThumbsTab.IsChecked     = false;
        OutlineTab.IsChecked    = true;
        BookmarksTab.IsChecked  = false;
        ThumbnailList.Visibility    = Visibility.Collapsed;
        BookmarkList.Visibility     = Visibility.Collapsed;
        NoBookmarksLabel.Visibility = Visibility.Collapsed;

        bool has = _outline.Count > 0;
        OutlineTree.Visibility    = has ? Visibility.Visible   : Visibility.Collapsed;
        NoOutlineLabel.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BookmarksTab_Click(object sender, RoutedEventArgs e)
    {
        ThumbsTab.IsChecked     = false;
        OutlineTab.IsChecked    = false;
        BookmarksTab.IsChecked  = true;
        ThumbnailList.Visibility  = Visibility.Collapsed;
        OutlineTree.Visibility    = Visibility.Collapsed;
        NoOutlineLabel.Visibility = Visibility.Collapsed;
        RefreshBookmarkList();
    }

    // ── Outline (TOC) population ──────────────────────────────────────────────

    internal void LoadOutline(PdfDocumentState? state)
    {
        if (state == null) return;
        var items = PdfOutlineReader.Read(state);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(_pdfState, state)) return;
            _outline = items;
            OutlineTree.RootNodes.Clear();
            foreach (var item in _outline)
                OutlineTree.RootNodes.Add(BuildNode(item));
        });
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
