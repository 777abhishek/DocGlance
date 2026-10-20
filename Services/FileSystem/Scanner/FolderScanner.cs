using DocGlance.Infrastructure.Logging;
using DocGlance.Core.Models;
using DocGlance.Common.Validators;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DocGlance.Services.FileSystem.Scanner;

public static class FolderScanner
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf",  ".docx", ".doc",  ".pptx", ".ppt",
            ".xlsx", ".xls",  ".csv",  ".txt",  ".md", ".markdown", ".mdx", ".mmd",
            ".log",  ".rtf",
            ".cs",   ".cpp",  ".py",   ".js",   ".ts",
            ".json", ".xml",  ".html", ".css",
            ".bmp", ".dds", ".gif", ".hdp", ".ico", ".jpeg", ".jpg", ".jxr",
            ".png", ".svg", ".svgz", ".tif", ".tiff", ".wdp",
        };

    public static Task<List<DocumentItem>> ScanFolderAsync(
        string folderPath, CancellationToken ct = default)
        => Task.Run(() =>
        {
            Logger.Log($"[SCAN] Starting: {folderPath}");
            var result = ScanCore(folderPath, ct);
            Logger.Log($"[SCAN] Done: {folderPath} — {result.Count} files");
            return result;
        }, ct);

    public static async Task<List<DocumentItem>> ScanFoldersAsync(
        IEnumerable<string> folderPaths,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var paths = folderPaths.ToList();
        if (paths.Count == 0) return new();

        var bag = new ConcurrentBag<DocumentItem>();
        int done = 0;

        await Task.Run(() => Parallel.ForEach(paths,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct },
            path =>
            {
                try { foreach (var item in ScanCore(path, ct)) bag.Add(item); }
                catch (OperationCanceledException) { throw; }
                catch { }
                finally { progress?.Report(Interlocked.Increment(ref done)); }
            }), ct);

        return bag.GroupBy(d => d.FilePath, StringComparer.OrdinalIgnoreCase)
                  .Select(g => g.First()).ToList();
    }

    // ── Core ──────────────────────────────────────────────────────────────────

    private static List<DocumentItem> ScanCore(string root, CancellationToken ct)
    {
        var results = new ConcurrentBag<DocumentItem>();
        if (!Directory.Exists(root)) return results.ToList();

        Parallel.ForEach(
            AllDirs(root, ct),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount / 2, CancellationToken = ct },
            dir =>
            {
                ct.ThrowIfCancellationRequested();
                ScanDir(dir, results, ct);
            });

        return results.ToList();
    }

    private static IEnumerable<string> AllDirs(string root, CancellationToken ct)
    {
        var bag = new ConcurrentBag<string> { root };
        try
        {
            foreach (var d in Directory.EnumerateDirectories(root, "*", new EnumerationOptions
            {
                RecurseSubdirectories  = true,
                IgnoreInaccessible     = true,
                ReturnSpecialDirectories = false,
                AttributesToSkip       = FileAttributes.System | FileAttributes.Hidden,
            }))
            {
                ct.ThrowIfCancellationRequested();
                bag.Add(d);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }
        return bag;
    }

    private static void ScanDir(string dir, ConcurrentBag<DocumentItem> results, CancellationToken ct)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(dir, "*.*", new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible    = true,
                AttributesToSkip      = FileAttributes.System | FileAttributes.Hidden,
            }))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var fi = new FileInfo(path);
                    
                    // SEC-05: Validate file path before adding to index
                    if (!PathValidator.IsValidFilePath(fi.FullName, out var reason))
                    {
                        Logger.Log($"[SCAN] Skipped invalid path: {fi.Name} (Reason: {reason})");
                        continue;
                    }
                    
                    if (SupportedExtensions.Contains(fi.Extension))
                        results.Add(DocumentItem.FromFileInfo(fi));
                }
                catch { }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }
    }
}
