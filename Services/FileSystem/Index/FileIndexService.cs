using Microsoft.EntityFrameworkCore;
using DocGlance.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DocGlance.Services.FileSystem.Index;

public static class FileIndexService
{
    private static readonly SemaphoreSlim WriteLock = new(1, 1);

    public static async Task InitializeDatabaseAsync()
    {
        using var db = new FileIndexContext();
        await db.Database.EnsureCreatedAsync();
    }

    public static async Task<List<DocumentItem>> LoadIndexedFilesAsync()
    {
        using var db = new FileIndexContext();
        var rows = await db.IndexedFiles.AsNoTracking().ToListAsync();
        return rows.Select(f => new DocumentItem
        {
            FilePath      = f.FilePath,
            Title         = f.FileName,
            FolderPath    = f.FolderPath,
            FileType      = f.FileExtension.TrimStart('.').ToUpperInvariant(),
            FileSizeBytes = f.FileSizeBytes,
            LastWriteTime = f.LastModified,
        }).ToList();
    }

    public static Task SaveFilesToIndexAsync(IEnumerable<DocumentItem> documents) =>
        ApplyFileChangesAsync(documents, Array.Empty<string>());

    public static async Task CommitScanAsync(
        string folderPath,
        IEnumerable<DocumentItem> documents,
        bool isComplete,
        CancellationToken cancellationToken = default)
    {
        var docs = documents.ToList();
        string rootPrefix = GetRootPrefix(folderPath);
        string rootPrefixLower = rootPrefix.ToLowerInvariant();
        DateTime now = DateTime.UtcNow;

        await WriteLock.WaitAsync(cancellationToken);
        try
        {
            using var db = new FileIndexContext();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var existingRows = await db.IndexedFiles
                .Where(file => file.FilePath.ToLower().StartsWith(rootPrefixLower))
                .ToListAsync(cancellationToken);
            var existingByPath = new Dictionary<string, IndexedFile>(
                StringComparer.OrdinalIgnoreCase);
            foreach (IndexedFile row in existingRows)
            {
                if (!existingByPath.TryAdd(row.FilePath, row))
                    db.IndexedFiles.Remove(row);
            }
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (DocumentItem document in docs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seenPaths.Add(document.FilePath))
                    continue;

                if (existingByPath.TryGetValue(document.FilePath, out IndexedFile? existing))
                {
                    UpdateRow(existing, document, now);
                }
                else
                {
                    db.IndexedFiles.Add(CreateRow(document, now));
                }
            }

            if (isComplete)
            {
                db.IndexedFiles.RemoveRange(existingRows.Where(
                    file => !seenPaths.Contains(file.FilePath)));
            }

            string folderPathLower = folderPath.ToLowerInvariant();
            ScannedFolder? scannedFolder = await db.ScannedFolders
                .FirstOrDefaultAsync(folder => folder.FolderPath.ToLower() == folderPathLower, cancellationToken);
            if (scannedFolder == null)
            {
                db.ScannedFolders.Add(new ScannedFolder
                {
                    FolderPath = folderPath,
                    LastScanned = now,
                    FileCount = docs.Count,
                });
            }
            else
            {
                scannedFolder.LastScanned = now;
                scannedFolder.FileCount = docs.Count;
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            WriteLock.Release();
        }
    }

    public static async Task UpsertFileAsync(DocumentItem document)
        => await ApplyFileChangesAsync(new[] { document }, Array.Empty<string>());

    public static async Task RemoveFileAsync(string filePath)
        => await ApplyFileChangesAsync(Array.Empty<DocumentItem>(), new[] { filePath });

    public static async Task ApplyFileChangesAsync(
        IEnumerable<DocumentItem> documents, IEnumerable<string> removedPaths)
    {
        var upserts = documents
            .GroupBy(document => document.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();
        var upsertsByPath = upserts.ToDictionary(
            document => document.FilePath, StringComparer.OrdinalIgnoreCase);
        var removals = removedPaths
            .Where(path => !upsertsByPath.ContainsKey(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await WriteLock.WaitAsync();
        try
        {
            using var db = new FileIndexContext();
            await using var transaction = await db.Database.BeginTransactionAsync();
            DateTime now = DateTime.UtcNow;

            foreach (DocumentItem document in upserts)
            {
                var existing = await db.IndexedFiles
                    .FirstOrDefaultAsync(file =>
                        file.FilePath.ToLower() == document.FilePath.ToLower());
                if (existing == null)
                    db.IndexedFiles.Add(CreateRow(document, now));
                else
                    UpdateRow(existing, document, now);
            }

            foreach (string filePath in removals)
            {
                var existing = await db.IndexedFiles
                    .FirstOrDefaultAsync(file =>
                        file.FilePath.ToLower() == filePath.ToLower());
                if (existing != null)
                    db.IndexedFiles.Remove(existing);
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        finally { WriteLock.Release(); }
    }

    public static async Task<List<string>> GetScannedFoldersAsync()
    {
        using var db = new FileIndexContext();
        return await db.ScannedFolders.AsNoTracking()
            .Select(f => f.FolderPath).ToListAsync();
    }

    public static async Task AddScannedFolderAsync(string folderPath, int fileCount)
    {
        await WriteLock.WaitAsync();
        try
        {
            using var db = new FileIndexContext();
            var existing = await db.ScannedFolders
                .FirstOrDefaultAsync(f => f.FolderPath.ToLower() == folderPath.ToLower());
            if (existing == null)
                db.ScannedFolders.Add(new ScannedFolder
                {
                    FolderPath = folderPath,
                    LastScanned = DateTime.UtcNow,
                    FileCount = fileCount,
                });
            else
            {
                existing.LastScanned = DateTime.UtcNow;
                existing.FileCount = fileCount;
            }
            await db.SaveChangesAsync();
        }
        finally { WriteLock.Release(); }
    }

    public static async Task RemoveFolderFromIndexAsync(string folderPath)
    {
        await WriteLock.WaitAsync();
        try
        {
            using var db = new FileIndexContext();
            string rootPrefixLower = GetRootPrefix(folderPath).ToLowerInvariant();
            var files = await db.IndexedFiles
                .Where(file => file.FilePath.ToLower().StartsWith(rootPrefixLower)).ToListAsync();
            db.IndexedFiles.RemoveRange(files);
            string folderPathLower = folderPath.ToLowerInvariant();
            var folder = await db.ScannedFolders
                .FirstOrDefaultAsync(f => f.FolderPath.ToLower() == folderPathLower);
            if (folder != null) db.ScannedFolders.Remove(folder);
            await db.SaveChangesAsync();
        }
        finally { WriteLock.Release(); }
    }

    public static async Task ClearIndexAsync()
    {
        await WriteLock.WaitAsync();
        try
        {
            using var db = new FileIndexContext();
            db.IndexedFiles.RemoveRange(db.IndexedFiles);
            db.ScannedFolders.RemoveRange(db.ScannedFolders);
            await db.SaveChangesAsync();
        }
        finally { WriteLock.Release(); }
    }

    private static IndexedFile CreateRow(DocumentItem document, DateTime indexedAt) => new()
    {
        FilePath = document.FilePath,
        FileName = document.Title,
        FolderPath = document.FolderPath,
        FileExtension = document.FileType,
        FileSizeBytes = document.FileSizeBytes,
        LastModified = document.LastWriteTime,
        IndexedAt = indexedAt,
    };

    private static void UpdateRow(IndexedFile row, DocumentItem document, DateTime indexedAt)
    {
        row.FilePath = document.FilePath;
        row.FileName = document.Title;
        row.FolderPath = document.FolderPath;
        row.FileExtension = document.FileType;
        row.FileSizeBytes = document.FileSizeBytes;
        row.LastModified = document.LastWriteTime;
        row.IndexedAt = indexedAt;
    }

    private static string GetRootPrefix(string folderPath)
    {
        string fullPath = Path.GetFullPath(folderPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath + Path.DirectorySeparatorChar;
    }
}
