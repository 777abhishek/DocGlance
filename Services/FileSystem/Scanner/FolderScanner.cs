using DocGlance.Infrastructure.Logging;
using DocGlance.Core.Models;
using DocGlance.Common.Formatters;
using DocGlance.Common.Validators;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using FileAttributes = System.IO.FileAttributes;

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

    public static async Task<List<DocumentItem>> ScanFolderAsync(
        StorageFolder root, CancellationToken ct = default)
    {
        Logger.Log($"[SCAN] Starting: {root.Path}");
        var results = new List<DocumentItem>();
        var pending = new Stack<StorageFolder>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            StorageFolder folder = pending.Pop();

            try
            {
                foreach (StorageFile file in await folder.GetFilesAsync())
                {
                    ct.ThrowIfCancellationRequested();
                    if (!SupportedExtensions.Contains(Path.GetExtension(file.Name)))
                        continue;

                    try
                    {
                        var attributes = File.GetAttributes(file.Path);
                        if ((attributes & FileAttributes.Hidden) != 0 ||
                            (attributes & FileAttributes.System) != 0)
                            continue;

                        if (!PathValidator.IsValidFilePathWithinRoot(file.Path, root.Path, out var reason))
                        {
                            Logger.Log($"[SCAN] Skipped invalid path: {file.Name} (Reason: {reason})");
                            continue;
                        }

                        var properties = await file.GetBasicPropertiesAsync();
                        DateTime lastWriteTime = properties.DateModified.DateTime;
                        long fileSize = checked((long)properties.Size);
                        results.Add(new DocumentItem
                        {
                            Title = file.Name,
                            FilePath = file.Path,
                            FolderPath = folder.Path,
                            FileType = Path.GetExtension(file.Name).TrimStart('.').ToUpperInvariant(),
                            FileSizeBytes = fileSize,
                            FileSize = FileSizeFormatter.Format(fileSize),
                            LastWriteTime = lastWriteTime,
                            LastModified = lastWriteTime.ToString("MMM dd, yyyy"),
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"[SCAN] Skipped file '{file.Path}': {ex.Message}");
                    }
                }

                foreach (StorageFolder child in await folder.GetFoldersAsync())
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(child.Path);
                        if ((attributes & FileAttributes.Hidden) == 0 &&
                            (attributes & FileAttributes.System) == 0)
                            pending.Push(child);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"[SCAN] Skipped folder '{child.Path}': {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Logger.Warn($"[SCAN] Skipped folder '{folder.Path}': {ex.Message}");
            }
        }

        Logger.Log($"[SCAN] Done: {root.Path} — {results.Count} files");
        return results;
    }
}
