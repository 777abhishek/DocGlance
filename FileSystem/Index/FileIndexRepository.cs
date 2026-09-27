using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QuickView.FileSystem.Index;

/// <summary>
/// Low-level data-access layer for the file index.
/// <see cref="FileIndexService"/> is the higher-level service that uses this.
/// </summary>
public static class FileIndexRepository
{
    public static async Task<List<IndexedFile>> GetAllAsync()
    {
        using var db = new FileIndexContext();
        return await db.IndexedFiles.AsNoTracking().ToListAsync();
    }

    public static async Task UpsertAsync(IEnumerable<IndexedFile> files)
    {
        using var db = new FileIndexContext();
        foreach (var f in files)
        {
            var existing = await db.IndexedFiles
                .FirstOrDefaultAsync(x => x.FilePath == f.FilePath);
            if (existing == null) db.IndexedFiles.Add(f);
            else
            {
                existing.LastModified  = f.LastModified;
                existing.FileSizeBytes = f.FileSizeBytes;
                existing.IndexedAt     = f.IndexedAt;
            }
        }
        await db.SaveChangesAsync();
    }

    public static async Task DeleteByFolderAsync(string folderPath)
    {
        using var db = new FileIndexContext();
        var rows = await db.IndexedFiles
            .Where(f => f.FolderPath == folderPath).ToListAsync();
        db.IndexedFiles.RemoveRange(rows);
        await db.SaveChangesAsync();
    }

    public static async Task ClearAllAsync()
    {
        using var db = new FileIndexContext();
        db.IndexedFiles.RemoveRange(db.IndexedFiles);
        db.ScannedFolders.RemoveRange(db.ScannedFolders);
        await db.SaveChangesAsync();
    }
}
