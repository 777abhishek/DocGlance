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

    // Guard: prevents scroll-sync from firing while GoToPage is animating,
    // which would cause the page number to flicker back to the previous value.
    private bool _suppressScrollSync;

    // Guard: prevents ThumbnailList.SelectedIndex assignment from re-firing ItemClick
    private bool _suppressThumbnailSync;

    internal void GoToPage(int n)
    {
        n = Math.Clamp(n, 1, _total);
        int idx = n - 1;
        UpdateCurrentPage(n);
        PrefetchPagesAround(idx);

        if (idx >= 0 && idx < PagesPanel.Children.Count)
        {
            // VerticalOffset is in content coordinates (physical px, pre-zoom).
            // GetPageContentTop sums Tag.physH values in the same space — exact match.
            double targetY = GetPageContentTop(idx);
            _suppressScrollSync = true;
            DocumentScrollViewer.ChangeView(null, targetY, null, disableAnimation: true);
            _ = DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => _suppressScrollSync = false);
        }
    }

    // Returns the Y position of a page in content coordinates (physical px).
    // This matches DocumentScrollViewer.VerticalOffset directly.
    private double GetPageContentTop(int pageIndex)
    {
        double top = PagesPanel.Margin.Top;
        for (int i = 0; i < pageIndex && i < PagesPanel.Children.Count; i++)
        {
            if (PagesPanel.Children[i] is Border b && b.Tag is (double _, double ph))
                top += ph + PagesPanel.Spacing;
            else if (PagesPanel.Children[i] is FrameworkElement el && el.ActualHeight > 0)
                top += el.ActualHeight + PagesPanel.Spacing;
        }
        return top;
    }

    private void UpdateCurrentPage(int page)
    {
        page = Math.Clamp(page, 1, Math.Max(1, _total));
        int index = page - 1;
        _page = page;
        Toolbar.UpdatePageInfo(_page, _total);
        SyncToViewModel();

        for (int i = 0; i < Thumbnails.Count; i++)
            Thumbnails[i].IsSelected = i == index;

        // Guard prevents SelectedIndex → ItemClick → GoToPage re-entry
        _suppressThumbnailSync = true;
        if (index >= 0 && index < ThumbnailList.Items.Count)
        {
            ThumbnailList.SelectedIndex = index;
            if (ThumbnailList.SelectedItem != null)
                ThumbnailList.ScrollIntoView(ThumbnailList.SelectedItem);
        }
        _suppressThumbnailSync = false;
    }

    // ── Scroll → page number + thumbnail sync ────────────────────────────────

    internal void WireScrollSync()
    {
        DocumentScrollViewer.ViewChanged -= OnScrollViewChanged;
        DocumentScrollViewer.ViewChanged += OnScrollViewChanged;
    }

    private void OnScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_suppressScrollSync) return;
        if (PagesPanel.Children.Count == 0) return;

        // VerticalOffset and GetPageContentTop are both in content coordinates
        // (physical px, unscaled). Compare them directly — no zoom math needed.
        // "Current" page = first page whose bottom is below (scrollTop + 1px),
        // meaning its top has entered the viewport from above.
        double scrollTop = DocumentScrollViewer.VerticalOffset;

        double cumulative = PagesPanel.Margin.Top;
        int best = 0;

        for (int i = 0; i < PagesPanel.Children.Count; i++)
        {
            if (PagesPanel.Children[i] is not Border border) continue;

            double pageH = 0;
            if (border.Tag is (double _, double ph)) pageH = ph;
            else if (border.ActualHeight > 0)         pageH = border.ActualHeight;
            if (pageH <= 0) continue;

            double pageBottom = cumulative + pageH;

            // Once we find a page whose bottom is past the scroll position,
            // that page is at least partially visible at the top — it's current.
            if (pageBottom > scrollTop)
            {
                best = i;
                break;
            }

            cumulative = pageBottom + PagesPanel.Spacing;
            best = i;
        }

        int newPage = best + 1;
        if (newPage != _page)
        {
            UpdateCurrentPage(newPage);
            PrefetchPagesAround(best);
        }
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
