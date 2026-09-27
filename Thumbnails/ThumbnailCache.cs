using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace QuickView.Thumbnails;

/// <summary>
/// Disk-based PNG cache for thumbnails.
/// Cache key = MD5(filePath|lastWriteUtcTicks|fileSize).
/// </summary>
public static class ThumbnailCache
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuickView", "thumbcache");

    static ThumbnailCache()
    {
        try { Directory.CreateDirectory(Dir); } catch { }
    }

    public static async Task<BitmapImage?> TryLoadAsync(string filePath)
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

    public static async Task SaveAsync(string filePath, IRandomAccessStream stream)
    {
        try
        {
            stream.Seek(0);
            using var fs = File.Create(CachePath(filePath));
            await stream.AsStreamForRead().CopyToAsync(fs);
        }
        catch { }
    }

    public static void PurgeAll()
    {
        _ = Task.Run(() =>
        {
            try { foreach (var f in Directory.EnumerateFiles(Dir, "*.png"))
                    try { File.Delete(f); } catch { } }
            catch { }
        });
    }

    private static string CachePath(string filePath)
    {
        var fi   = new FileInfo(filePath);
        var key  = $"{filePath}|{fi.LastWriteTimeUtc.Ticks}|{fi.Length}";
        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(Dir, hash + ".png");
    }
}
