using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace QuickView.Thumbnails;

/// <summary>
/// Disk-based PNG cache for thumbnails.
/// Cache key = MD5(filePath|lastWriteUtcTicks|fileSize).
/// H5: Automatic eviction when cache exceeds 500 MB, keeping most recently accessed files.
/// </summary>
public static class ThumbnailCache
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuickView", "thumbcache");

    // H5: Cache size thresholds
    private const long MaxCacheSizeBytes = 500 * 1024 * 1024; // 500 MB
    private const long TargetCacheSizeBytes = (long)(MaxCacheSizeBytes * 0.8); // 400 MB (80%)
    
    private static bool _evictionScheduled = false;
    private static readonly object _evictionLock = new();

    static ThumbnailCache()
    {
        try { Directory.CreateDirectory(Dir); } catch { }
        // H5: Check cache size on startup and schedule eviction if needed
        _ = Task.Run(() => CheckAndEvictIfNeededAsync());
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
            
            // H5: Update access time when cache entry is used
            try { File.SetLastAccessTime(cp, DateTime.UtcNow); } catch { }
            
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
            var cp = CachePath(filePath);
            using var fs = File.Create(cp);
            await stream.AsStreamForRead().CopyToAsync(fs);
            
            // H5: Update access time on save
            try { File.SetLastAccessTime(cp, DateTime.UtcNow); } catch { }
            
            // H5: Check if cache needs eviction after adding new entry
            _ = Task.Run(() => CheckAndEvictIfNeededAsync());
        }
        catch { }
    }

    /// <summary>
    /// Records a cache-presence marker for a BitmapImage so the next load
    /// short-circuits to the provider rather than re-generating.
    /// Full PNG re-encoding would require WriteableBitmap/Win2D — not warranted here.
    /// </summary>
    public static async Task SaveBitmapAsync(string filePath, BitmapImage bmp)
    {
        try
        {
            var cp = CachePath(filePath);
            if (!File.Exists(cp))
                await File.WriteAllBytesAsync(cp, []);
            try { File.SetLastAccessTime(cp, DateTime.UtcNow); } catch { }
            _ = Task.Run(() => CheckAndEvictIfNeededAsync());
        }
        catch { }
    }

    /// <summary>Removes the cache entry for a single file if present.</summary>
    public static void Purge(string filePath)
    {
        try
        {
            var cp = CachePath(filePath);
            if (File.Exists(cp)) File.Delete(cp);
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

    // H5: Eviction logic - removes least recently accessed files when cache exceeds threshold
    private static async Task CheckAndEvictIfNeededAsync()
    {
        // Prevent concurrent eviction operations
        lock (_evictionLock)
        {
            if (_evictionScheduled) return;
            _evictionScheduled = true;
        }

        try
        {
            await Task.Delay(1000); // Debounce rapid calls
            
            if (!Directory.Exists(Dir)) return;
            
            // Calculate total cache size
            var files = Directory.EnumerateFiles(Dir, "*.png")
                .Select(path => new FileInfo(path))
                .Where(fi => fi.Exists)
                .ToList();
            
            long totalSize = files.Sum(fi => fi.Length);
            
            // Only evict if we exceed the max threshold
            if (totalSize <= MaxCacheSizeBytes) return;
            
            // Sort by last access time (oldest first)
            var sortedFiles = files
                .OrderBy(fi => fi.LastAccessTime)
                .ToList();
            
            long currentSize = totalSize;
            int deletedCount = 0;
            
            // Delete oldest files until we reach 80% of max size
            foreach (var fi in sortedFiles)
            {
                if (currentSize <= TargetCacheSizeBytes) break;
                
                try
                {
                    long fileSize = fi.Length;
                    fi.Delete();
                    currentSize -= fileSize;
                    deletedCount++;
                }
                catch { }
            }
            
            if (deletedCount > 0)
            {
                QuickView.Infrastructure.Logging.Logger.Log(
                    $"[ThumbnailCache] Evicted {deletedCount} files, " +
                    $"freed {(totalSize - currentSize) / 1024 / 1024} MB, " +
                    $"cache now {currentSize / 1024 / 1024} MB");
            }
        }
        catch (Exception ex)
        {
            QuickView.Infrastructure.Logging.Logger.Error("ThumbnailCache eviction failed", ex);
        }
        finally
        {
            lock (_evictionLock)
            {
                _evictionScheduled = false;
            }
        }
    }

    private static string CachePath(string filePath)
    {
        var fi   = new FileInfo(filePath);
        var key  = $"{filePath}|{fi.LastWriteTimeUtc.Ticks}|{fi.Length}";
        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(Dir, hash + ".png");
    }
}
