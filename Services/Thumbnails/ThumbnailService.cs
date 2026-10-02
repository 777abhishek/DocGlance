using Microsoft.UI.Xaml.Media.Imaging;
using QuickView.Core.Interfaces;
using QuickView.Infrastructure.Logging;
using QuickView.Services.Thumbnails.Providers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Services.Thumbnails;

/// <summary>
/// Orchestrates thumbnail generation by routing requests through registered
/// <see cref="IThumbnailProvider"/> implementations.
/// Falls back to <see cref="GenericThumbnailProvider"/> when no specific provider matches.
/// All results are cached via <see cref="ThumbnailCache"/>.
/// </summary>
public static class ThumbnailService
{
    private static readonly SemaphoreSlim _sem = new(6, 6);

    // Ordered list: specific providers first, generic fallback last.
    private static readonly IReadOnlyList<IThumbnailProvider> _providers =
    [
        new PdfThumbnailProvider(),
        new OfficeThumbnailProvider(),
        new GenericThumbnailProvider(),   // wildcard "*" — always matches
    ];

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a thumbnail for <paramref name="filePath"/>, using the cache where possible.
    /// </summary>
    public static async Task<BitmapImage?> GetThumbnailAsync(
        string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath)) return null;

        await _sem.WaitAsync(ct);
        try
        {
            // 1. Cache hit
            var cached = await ThumbnailCache.TryLoadAsync(filePath);
            if (cached != null) return cached;

            // 2. Find the best provider
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            var provider = _providers.FirstOrDefault(p =>
                p.SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) 
                ?? _providers.Last(); // GenericThumbnailProvider

            // 3. Generate
            var bmp = await provider.GetThumbnailAsync(filePath, ct);
            if (bmp == null) return null;

            // 4. Persist to disk cache asynchronously (fire-and-forget)
            _ = ThumbnailCache.SaveBitmapAsync(filePath, bmp);

            return bmp;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            Logger.Log($"[ThumbnailService] {Path.GetFileName(filePath)}: {ex.Message}");
            return null;
        }
        finally { _sem.Release(); }
    }

    /// <summary>
    /// Returns true if any non-generic provider supports this extension.
    /// Used to decide whether to bother requesting a thumbnail.
    /// </summary>
    public static bool SupportsThumbnail(string extensionWithDot)
    {
        string ext = extensionWithDot.ToLowerInvariant();
        return _providers
            .Where(p => !p.SupportedExtensions.Contains("*"))
            .Any(p => p.SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Removes cached thumbnail entries for all files under <paramref name="folderRoot"/>.
    /// </summary>
    public static void PurgeCacheForFolder(string folderRoot)
    {
        _ = Task.Run(() =>
        {
            try
            {
                var files = Directory.EnumerateFiles(
                    folderRoot, "*.*",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible    = true,
                    });

                foreach (var filePath in files)
                    ThumbnailCache.Purge(filePath);
            }
            catch { }
        });
    }
}
