using Microsoft.EntityFrameworkCore;
using System;
using System.IO;

namespace QuickView.FileSystem.Index;

public class FileIndexContext : DbContext
{
    public DbSet<IndexedFile>   IndexedFiles   { get; set; } = null!;
    public DbSet<ScannedFolder> ScannedFolders { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickView", "fileindex.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IndexedFile>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.FilePath).IsUnique();
            e.HasIndex(x => x.FolderPath);
            e.HasIndex(x => x.LastModified);
        });
        modelBuilder.Entity<ScannedFolder>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.FolderPath).IsUnique();
        });
    }
}

public class IndexedFile
{
    public int      Id            { get; set; }
    public string   FilePath      { get; set; } = string.Empty;
    public string   FileName      { get; set; } = string.Empty;
    public string   FolderPath    { get; set; } = string.Empty;
    public string   FileExtension { get; set; } = string.Empty;
    public long     FileSizeBytes { get; set; }
    public DateTime LastModified  { get; set; }
    public DateTime IndexedAt     { get; set; }
}

public class ScannedFolder
{
    public int      Id          { get; set; }
    public string   FolderPath  { get; set; } = string.Empty;
    public DateTime LastScanned { get; set; }
    public int      FileCount   { get; set; }
}
