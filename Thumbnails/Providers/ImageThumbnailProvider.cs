using Microsoft.UI.Xaml.Media.Imaging;
using QuickView.Core.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace QuickView.Thumbnails.Providers;

public sealed class ImageThumbnailProvider : IThumbnailProvider
{
    public string[] SupportedExtensions =>
        [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff", ".heic"];

    public async Task<BitmapImage?> GetThumbnailAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            using var thumb = await file.GetThumbnailAsync(
                ThumbnailMode.PicturesView, 512,
                ThumbnailOptions.ResizeThumbnail | ThumbnailOptions.UseCurrentScale);
            if (thumb == null || thumb.Size == 0) return null;
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(thumb);
            return bmp;
        }
        catch { return null; }
    }
}
