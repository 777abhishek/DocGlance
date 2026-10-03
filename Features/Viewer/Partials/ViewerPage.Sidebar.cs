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

    internal void GoToPage(int n)
    {
        n = Math.Clamp(n, 1, _total);
        int idx = n - 1;
        UpdateCurrentPage(n);
        PrefetchPagesAround(idx);

        // ── Scroll the main canvas to the target page ─────────────────────────
        // Compute the page's vertical offset in content coordinates (physical px)
        // by summing up heights of preceding pages using the Tag tuple.
        // Then suppress scroll-sync so the event doesn't flicker the page number
        // back to the previous value while ChangeView animates.
        if (idx >= 0 && idx < PagesPanel.Children.Count)
        {
            double targetY = GetPageScrollOffset(idx);

            _suppressScrollSync = true;
            DocumentScrollViewer.ChangeView(null, targetY, null, disableAnimation: true);
            _ = DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => _suppressScrollSync = false);
        }

    }

    private double GetPageContentTop(int pageIndex)
    {
        double top = PagesPanel.Margin.Top;
        for (int i = 0; i < pageIndex && i < PagesPanel.Children.Count; i++)
        {
            if (PagesPanel.Children[i] is Border page && page.Tag is (double _, double height))
                top += height + PagesPanel.Spacing;
            else if (PagesPanel.Children[i] is FrameworkElement element)
                top += element.ActualHeight + PagesPanel.Spacing;
        }
        return top;
    }

    private double GetPageScrollOffset(int pageIndex)
    {
        double zoom = Math.Max(0.01, DocumentScrollViewer.ZoomFactor);
        try
        {
            var page = PagesPanel.Children[pageIndex] as FrameworkElement;
            if (page != null && page.ActualHeight > 0)
            {
                double pageTop = page.TransformToVisual(DocumentScrollViewer)
                    .TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
                double targetViewportTop = PagesPanel.Margin.Top;
                return Math.Max(0, DocumentScrollViewer.VerticalOffset
                    + (pageTop - targetViewportTop) / zoom);
            }
        }
        catch (Exception) { }

        return GetPageContentTop(pageIndex);
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

        if (index >= 0 && index < ThumbnailList.Items.Count)
        {
            ThumbnailList.SelectedIndex = index;
            if (ThumbnailList.SelectedItem != null)
                ThumbnailList.ScrollIntoView(ThumbnailList.SelectedItem);
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
        if (_suppressScrollSync) return;
        if (PagesPanel.Children.Count == 0) return;

        // Rune's PageAt() uses page rectangles in one document coordinate
        // system. Here the ScrollViewer is allowed to zoom a physical-pixel
        // canvas, so reading VerticalOffset against the stored page sizes can
        // drift. Transform each page into the viewport instead; this remains
        // correct at every zoom and display scale.
        double readingPoint = DocumentScrollViewer.ViewportHeight * 0.35;
        int best = Math.Clamp(_page - 1, 0, PagesPanel.Children.Count - 1);
        bool found = false;

        // Fast path: check near the current page first (O(1) in typical scrolling)
        int searchStart = Math.Max(0, best - 2);
        int searchEnd   = Math.Min(PagesPanel.Children.Count - 1, best + 2);

        for (int i = searchStart; i <= searchEnd; i++)
        {
            if (PagesPanel.Children[i] is not Border border || border.ActualHeight <= 0)
                continue;

            try
            {
                var transform = border.TransformToVisual(DocumentScrollViewer);
                double top = transform.TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
                double bottom = top + border.ActualHeight;

                if (top <= readingPoint && readingPoint <= bottom)
                {
                    best = i;
                    found = true;
                    break;
                }
            }
            catch { }
        }

        // Fallback: full scan if user jumped/dragged scrollbar
        if (!found)
        {
            double lastTop = double.NegativeInfinity;
            for (int i = 0; i < PagesPanel.Children.Count; i++)
            {
                if (PagesPanel.Children[i] is not Border border || border.ActualHeight <= 0)
                    continue;

                try
                {
                    var transform = border.TransformToVisual(DocumentScrollViewer);
                    double top = transform.TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
                    double bottom = top + border.ActualHeight;

                    if (top <= readingPoint && top >= lastTop)
                    {
                        best = i;
                        lastTop = top;
                    }

                    if (top <= readingPoint && readingPoint <= bottom)
                    {
                        best = i;
                        break;
                    }
                }
                catch { }
            }
        }

        int newPage = best + 1;
        if (newPage != _page || _page.ToString() != newPage.ToString())
        {
            UpdateCurrentPage(newPage);
            PrefetchPagesAround(best);
        }
    }

    // ── Thumbnail click ───────────────────────────────────────────────────────

    private void ThumbnailList_ItemClick(object sender, ItemClickEventArgs e)
    {
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
