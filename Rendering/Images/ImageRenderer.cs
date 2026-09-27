using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using QuickView.Core.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace QuickView.Rendering.Images;

public sealed class ImageRenderer : IFileRenderer
{
    public string[] SupportedExtensions =>
        [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff"];

    public async Task<UIElement?> RenderAsync(string filePath, CancellationToken ct = default)
    {
        var file = await StorageFile.GetFileFromPathAsync(filePath);
        using var stream = await file.OpenReadAsync();
        var bmp = new BitmapImage();
        await bmp.SetSourceAsync(stream);
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            Content = new Image
            {
                Source  = bmp,
                Stretch = Stretch.None,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            },
        };
    }
}
