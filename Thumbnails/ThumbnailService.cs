using Microsoft.UI.Xaml.Media.Imaging;
using QuickView.Infrastructure.Logging;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace QuickView.Thumbnails;

public static class ThumbnailService
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuickView", "thumbcache");

    private static readonly SemaphoreSlim _sem = new(6, 6);

    static ThumbnailService()
    {
        try { Directory.CreateDirectory(CacheDir); } catch { }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public static async Task<BitmapImage?> GetThumbnailAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            string ext = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
            var (mode, size) = ext switch
            {
                "JPG" or "JPEG" or "PNG" or "BMP" or "GIF"
                    or "WEBP" or "TIFF" or "HEIC" => (ThumbnailMode.PicturesView, (uint)512),
                "MP4" or "AVI" or "MKV" or "MOV"
                    or "WMV"  or "WEBM"            => (ThumbnailMode.VideosView,   (uint)512),
                _                                  => (ThumbnailMode.SingleItem,   (uint)256),
            };
            return await GetShellThumbnailAsync(filePath, mode, size);
        }
        catch { return null; }
    }

    public static bool SupportsThumbnail(string extensionWithDot)
    {
        string ext = extensionWithDot.TrimStart('.').ToUpperInvariant();
        return ext is "PDF"
            or "JPG" or "JPEG" or "PNG" or "BMP" or "GIF" or "WEBP" or "TIFF"
            or "MP4" or "AVI"  or "MKV" or "MOV" or "WMV"
            or "DOCX" or "PPTX" or "XLSX";
    }

    public static void PurgeCacheForFolder(string folderRoot)
    {
        _ = Task.Run(() =>
        {
            try { foreach (var f in Directory.EnumerateFiles(CacheDir, "*.png"))
                    try { File.Delete(f); } catch { } }
            catch { }
        });
    }

    // ── Shell thumbnail ───────────────────────────────────────────────────────

    private static async Task<BitmapImage?> GetShellThumbnailAsync(
        string filePath, ThumbnailMode mode, uint size)
    {
        await _sem.WaitAsync();
        try
        {
            var cached = await TryLoadCacheAsync(filePath);
            if (cached != null) return cached;

            var file  = await StorageFile.GetFileFromPathAsync(filePath);
            using var thumb = await file.GetThumbnailAsync(
                mode, size,
                ThumbnailOptions.ResizeThumbnail | ThumbnailOptions.UseCurrentScale);

            if (thumb == null || thumb.Size == 0) return null;

            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(thumb);
            thumb.Seek(0);
            _ = SaveCacheAsync(filePath, thumb);
            return bmp;
        }
        catch (Exception ex)
        {
            Logger.Log($"[THUMB] {Path.GetFileName(filePath)}: {ex.Message}");
            return null;
        }
        finally { _sem.Release(); }
    }

    // ── Cache ─────────────────────────────────────────────────────────────────

    private static string CachePath(string filePath)
    {
        try
        {
            var fi   = new FileInfo(filePath);
            var key  = $"{filePath}|{fi.LastWriteTimeUtc.Ticks}|{fi.Length}";
            var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(key)));
            return Path.Combine(CacheDir, hash + ".png");
        }
        catch { return Path.Combine(CacheDir, "invalid.png"); }
    }

    private static async Task<BitmapImage?> TryLoadCacheAsync(string filePath)
    {
        try
        {
            var cp = CachePath(filePath);
            if (!File.Exists(cp)) return null;
            if (new FileInfo(filePath).LastWriteTimeUtc > new FileInfo(cp).LastWriteTimeUtc)
            {
                try { File.Delete(cp); } catch { }
                return null;
            }
            var bmp = new BitmapImage();
            using var fs  = File.OpenRead(cp);
            using var ras = fs.AsRandomAccessStream();
            await bmp.SetSourceAsync(ras);
            return bmp;
        }
        catch { return null; }
    }

    private static async Task SaveCacheAsync(string filePath, IRandomAccessStream stream)
    {
        try
        {
            stream.Seek(0);
            using var fs = File.Create(CachePath(filePath));
            await stream.AsStreamForRead().CopyToAsync(fs);
        }
        catch { }
    }
}
