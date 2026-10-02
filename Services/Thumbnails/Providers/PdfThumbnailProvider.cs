using Microsoft.UI.Xaml.Media.Imaging;
using QuickView.Core.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace QuickView.Services.Thumbnails.Providers;

public sealed class PdfThumbnailProvider : IThumbnailProvider
{
    public string[] SupportedExtensions => [".pdf"];

    public async Task<BitmapImage?> GetThumbnailAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath);

            // Fast path: Windows Shell thumbnail cache
            try
            {
                using var thumb = await file.GetThumbnailAsync(
                    ThumbnailMode.DocumentsView, 300,
                    ThumbnailOptions.ResizeThumbnail | ThumbnailOptions.UseCurrentScale);
                if (thumb != null && thumb.Size > 0)
                {
                    var bmp = new BitmapImage();
                    await bmp.SetSourceAsync(thumb);
                    return bmp;
                }
            }
            catch { }

            // Fallback: render first page with Windows.Data.Pdf for standalone shell thumbnail
            var pdfDoc = await PdfDocument.LoadFromFileAsync(file);
            if (pdfDoc.PageCount == 0) return null;

            using var page = pdfDoc.GetPage(0);
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, new PdfPageRenderOptions
            {
                DestinationWidth = 200,
            });

            if (ct.IsCancellationRequested) return null;
            stream.Seek(0);

            var fallbackBmp = new BitmapImage();
            await fallbackBmp.SetSourceAsync(stream);
            return fallbackBmp;
        }
        catch { return null; }
    }
}
