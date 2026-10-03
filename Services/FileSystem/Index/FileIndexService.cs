using Microsoft.EntityFrameworkCore;
using QuickView.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QuickView.Services.FileSystem.Index;

public static class FileIndexService
{
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

    public static async Task SaveFilesToIndexAsync(IEnumerable<DocumentItem> documents)
    {
        using var db  = new FileIndexContext();
        var now  = DateTime.UtcNow;
        var docs = documents.ToList();
        if (docs.Count == 0) return;

        // Load all existing rows for the incoming paths in ONE query instead of
        // issuing a separate SELECT per document (fixes the N+1 bug).
        var incomingPaths = docs.Select(d => d.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingRows  = await db.IndexedFiles
            .Where(f => incomingPaths.Contains(f.FilePath))
            .ToListAsync();

        var existingByPath = existingRows.ToDictionary(
            f => f.FilePath, StringComparer.OrdinalIgnoreCase);

        var toAdd = new List<IndexedFile>();

        foreach (var doc in docs)
        {
            if (existingByPath.TryGetValue(doc.FilePath, out var existing))
            {
                // Only dirty-track rows that actually changed.
                if (existing.LastModified != doc.LastWriteTime)
                {
                    existing.LastModified  = doc.LastWriteTime;
                    existing.FileSizeBytes = doc.FileSizeBytes;
                    existing.IndexedAt     = now;
                }
            }
            else
            {
                toAdd.Add(new IndexedFile
                {
                    FilePath      = doc.FilePath,
                    FileName      = doc.Title,
                    FolderPath    = doc.FolderPath,
                    FileExtension = doc.FileType,
                    FileSizeBytes = doc.FileSizeBytes,
                    LastModified  = doc.LastWriteTime,
                    IndexedAt     = now,
                });
            }
        }

        if (toAdd.Count > 0) await db.IndexedFiles.AddRangeAsync(toAdd);
        await db.SaveChangesAsync();
    }

    public static async Task<List<string>> GetScannedFoldersAsync()
    {
        using var db = new FileIndexContext();
        return await db.ScannedFolders.AsNoTracking()
            .Select(f => f.FolderPath).ToListAsync();
    }

    public static async Task AddScannedFolderAsync(string folderPath, int fileCount)
    {
        using var db = new FileIndexContext();
        var existing = await db.ScannedFolders
            .FirstOrDefaultAsync(f => f.FolderPath == folderPath);
        if (existing == null)
            db.ScannedFolders.Add(new ScannedFolder
            {
                FolderPath  = folderPath,
                LastScanned = DateTime.UtcNow,
                FileCount   = fileCount,
            });
        else
        {
            existing.LastScanned = DateTime.UtcNow;
            existing.FileCount   = fileCount;
        }
        await db.SaveChangesAsync();
    }

    public static async Task RemoveFolderFromIndexAsync(string folderPath)
    {
        using var db = new FileIndexContext();
        var files = await db.IndexedFiles
            .Where(f => f.FolderPath == folderPath).ToListAsync();
        db.IndexedFiles.RemoveRange(files);
        var folder = await db.ScannedFolders
            .FirstOrDefaultAsync(f => f.FolderPath == folderPath);
        if (folder != null) db.ScannedFolders.Remove(folder);
        await db.SaveChangesAsync();
    }

    public static async Task ClearIndexAsync()
    {
        using var db = new FileIndexContext();
        db.IndexedFiles.RemoveRange(db.IndexedFiles);
        db.ScannedFolders.RemoveRange(db.ScannedFolders);
        await db.SaveChangesAsync();
    }
}
