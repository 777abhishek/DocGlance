using Microsoft.UI.Xaml.Media.Imaging;
using DocGlance.Core.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace DocGlance.Services.Thumbnails.Providers;

/// <summary>Fallback provider — uses Windows Shell SingleItem thumbnail.</summary>
public sealed class GenericThumbnailProvider : IThumbnailProvider
{
    public string[] SupportedExtensions => ["*"];

    public async Task<BitmapImage?> GetThumbnailAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            using var thumb = await file.GetThumbnailAsync(
                ThumbnailMode.SingleItem, 256,
                ThumbnailOptions.ResizeThumbnail | ThumbnailOptions.UseCurrentScale);
            if (thumb == null || thumb.Size == 0) return null;
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(thumb);
            return bmp;
        }
        catch { return null; }
    }
}
