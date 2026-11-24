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
    private const int ScanBatchSize = 64;

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
        => ScanFolderAsync(root, onBatch: null, ct: ct);

    public static Task<FolderScanResult> ScanFolderAsync(
        StorageFolder root,
        Action<IReadOnlyList<DocumentItem>>? onBatch,
        CancellationToken ct = default)
    {
        string rootPath = Path.GetFullPath(root.Path);
        return ScanDirectoryAsync(rootPath, onBatch, ct);
    }

    public static Task<FolderScanResult> ScanDirectoryAsync(
        string rootPath, CancellationToken ct = default)
        => ScanDirectoryAsync(rootPath, onBatch: null, ct: ct);

    public static Task<FolderScanResult> ScanDirectoryAsync(
        string rootPath,
        Action<IReadOnlyList<DocumentItem>>? onBatch,
        CancellationToken ct = default)
    {
        string fullRootPath = Path.GetFullPath(rootPath);
        return Task.Run(() => ScanDirectory(fullRootPath, onBatch, ct), ct);
    }

    private static FolderScanResult ScanDirectory(
        string rootPath,
        Action<IReadOnlyList<DocumentItem>>? onBatch,
        CancellationToken ct)
    {
        Logger.Log($"[SCAN] Starting: {rootPath}");
        var results = new List<DocumentItem>();
        var batch = new List<DocumentItem>(ScanBatchSize);
        bool isComplete = true;

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
            };
            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(rootPath);

            while (pendingDirectories.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                string directory = pendingDirectories.Pop();

                try
                {
                    foreach (string subdirectory in Directory.EnumerateDirectories(directory, "*", options))
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            FileAttributes attributes = File.GetAttributes(subdirectory);
                            if ((attributes & (FileAttributes.Hidden | FileAttributes.System |
                                               FileAttributes.ReparsePoint)) == 0)
                                pendingDirectories.Push(subdirectory);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            isComplete = false;
                            Logger.Warn($"[SCAN] Could not inspect directory '{subdirectory}': {ex.Message}");
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    isComplete = false;
                    Logger.Warn($"[SCAN] Could not enumerate directories in '{directory}': {ex.Message}");
                }

                try
                {
                    foreach (string path in Directory.EnumerateFiles(directory, "*", options))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!IsSupportedFilePath(path))
                            continue;

                        DocumentItem? item = ReadDocument(path, rootPath, out bool fileReadComplete);
                        if (!fileReadComplete)
                            isComplete = false;
                        if (item != null)
                        {
                            results.Add(item);
                            batch.Add(item);
                            if (batch.Count == ScanBatchSize)
                            {
                                onBatch?.Invoke(batch.ToArray());
                                batch.Clear();
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    isComplete = false;
                    Logger.Warn($"[SCAN] Could not enumerate files in '{directory}': {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            isComplete = false;
            Logger.Warn($"[SCAN] Failed to enumerate '{rootPath}': {ex.Message}");
        }

        if (batch.Count > 0)
            onBatch?.Invoke(batch.ToArray());

        Logger.Log($"[SCAN] Done: {rootPath} — {results.Count} files (complete: {isComplete})");
        return new FolderScanResult(results, isComplete);
    }

    public static bool IsSupportedFilePath(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path));

    public static Task<DocumentItem?> ReadDocumentAsync(string filePath, string rootPath)
        => Task.FromResult(ReadDocument(filePath, rootPath, out _));

    private static DocumentItem? ReadDocument(
        string filePath, string rootPath, out bool isComplete)
    {
        isComplete = true;
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
            isComplete = false;
            Logger.Warn($"[SCAN] Skipped file '{filePath}': {ex.Message}");
            return null;
        }
    }
}

public sealed record FolderScanResult(List<DocumentItem> Items, bool IsComplete);
