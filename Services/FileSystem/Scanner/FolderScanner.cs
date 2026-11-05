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

    public static Task<FolderScanResult> ScanFolderAsync(
        StorageFolder root, CancellationToken ct = default)
    {
        string rootPath = Path.GetFullPath(root.Path);
        return ScanDirectoryAsync(rootPath, ct);
    }

    public static Task<FolderScanResult> ScanDirectoryAsync(
        string rootPath, CancellationToken ct = default)
    {
        Logger.Log($"[SCAN] Starting: {rootPath}");
        var results = new List<DocumentItem>();
        bool isComplete = true;

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
            };

            foreach (string path in Directory.EnumerateFiles(rootPath, "*", options))
            {
                ct.ThrowIfCancellationRequested();
                if (!IsSupportedFilePath(path))
                    continue;

                try
                {
                    DocumentItem? item = ReadDocument(path, rootPath);
                    if (item != null)
                        results.Add(item);
                }
                catch (Exception ex)
                {
                    isComplete = false;
                    Logger.Warn($"[SCAN] Skipped file '{path}': {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            isComplete = false;
            Logger.Warn($"[SCAN] Failed to enumerate '{rootPath}': {ex.Message}");
        }

        Logger.Log($"[SCAN] Done: {rootPath} — {results.Count} files (complete: {isComplete})");
        return Task.FromResult(new FolderScanResult(results, isComplete));
    }

    public static bool IsSupportedFilePath(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path));

    public static Task<DocumentItem?> ReadDocumentAsync(string filePath, string rootPath)
        => Task.FromResult(ReadDocument(filePath, rootPath));

    private static DocumentItem? ReadDocument(string filePath, string rootPath)
    {
        if (!IsSupportedFilePath(filePath))
            return null;

        try
        {
            var attributes = File.GetAttributes(filePath);
            if ((attributes & (FileAttributes.Hidden | FileAttributes.System |
                               FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                return null;

            if (!PathValidator.IsValidFilePathWithinRoot(filePath, rootPath, out var reason))
            {
                Logger.Log($"[SCAN] Skipped invalid path: {filePath} (Reason: {reason})");
                return null;
            }

            var info = new FileInfo(filePath);
            DateTime lastWriteTime = info.LastWriteTime;
            long fileSize = info.Length;
            return new DocumentItem
            {
                Title = info.Name,
                FilePath = info.FullName,
                FolderPath = info.DirectoryName ?? string.Empty,
                FileType = info.Extension.TrimStart('.').ToUpperInvariant(),
                FileSizeBytes = fileSize,
                FileSize = FileSizeFormatter.Format(fileSize),
                LastWriteTime = lastWriteTime,
                LastModified = lastWriteTime.ToString("MMM dd, yyyy"),
            };
        }
        catch (Exception ex)
        {
            Logger.Warn($"[SCAN] Skipped file '{filePath}': {ex.Message}");
            return null;
        }
    }
}

public sealed record FolderScanResult(List<DocumentItem> Items, bool IsComplete);
