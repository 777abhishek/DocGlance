using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using QuickView.Core.Models;

namespace QuickView.UI.Controls;

public sealed partial class PageThumbnailSidebar : UserControl
{
    public event EventHandler<int>? PageSelected;

    public ObservableCollection<PageThumbnailItem> PageItems { get; } = new();

    public PageThumbnailSidebar()
    {
        InitializeComponent();
    }

    public void LoadPages(int pageCount)
    {
        PageItems.Clear();
        for (int i = 1; i <= pageCount; i++)
        {
            PageItems.Add(new PageThumbnailItem
            {
                PageNumber = i,
                PageLabel = $"Page {i}"
            });
        }
    }

    public void SetThumbnail(int pageNumber, Microsoft.UI.Xaml.Media.ImageSource thumbnail)
    {
        if (pageNumber > 0 && pageNumber <= PageItems.Count)
        {
            PageItems[pageNumber - 1].Thumbnail = thumbnail;
        }
    }

    public void Clear()
    {
        PageItems.Clear();
    }

    private void ThumbnailButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is int pageNumber)
        {
            PageSelected?.Invoke(this, pageNumber);
        }
    }
}
