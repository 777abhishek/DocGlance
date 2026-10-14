using DocGlance.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DocGlance.Services.Storage;

/// <summary>
/// Persists per-file bookmarks to %LocalAppData%\DocGlance\bookmarks.json.
/// Thread-safe for single-threaded UI use (all calls from UI thread).
/// </summary>
public static class BookmarkStore
{
    static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DocGlance", "bookmarks.json");

    static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    // In-memory list — loaded once on first access.
    static List<BookmarkItem>? _cache;

    static List<BookmarkItem> Load()
    {
        if (_cache != null) return _cache;
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                _cache = JsonSerializer.Deserialize<List<BookmarkItem>>(json) ?? new();
                return _cache;
            }
        }
        catch { /* corrupt file — start fresh */ }
        _cache = new();
        return _cache;
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_cache, _opts));
        }
        catch { }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>All bookmarks for a given file path, sorted by page.</summary>
    public static List<BookmarkItem> GetForFile(string filePath) =>
        Load().Where(b => b.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
              .OrderBy(b => b.PageNumber)
              .ToList();

    /// <summary>True if page is already bookmarked in this file.</summary>
    public static bool IsBookmarked(string filePath, int page) =>
        Load().Any(b => b.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)
                     && b.PageNumber == page);

    /// <summary>Add bookmark; no-op if already exists. Returns the new item.</summary>
    public static BookmarkItem Add(string filePath, int page, string label)
    {
        var list = Load();
        
        // Use FirstOrDefault to avoid double-scanning the list.
        var existing = list.FirstOrDefault(b => 
            b.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)
            && b.PageNumber == page);
        
        if (existing != null)
            return existing;

        var item = new BookmarkItem
        {
            FilePath   = filePath,
            PageNumber = page,
            Label      = label,
            CreatedAt  = DateTime.UtcNow.ToString("o"),
        };
        list.Add(item);
        Save();
        return item;
    }

    /// <summary>Remove bookmark for this file + page. Returns true if removed.</summary>
    public static bool Remove(string filePath, int page)
    {
        var list    = Load();
        var removed = list.RemoveAll(b =>
            b.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)
            && b.PageNumber == page);
        if (removed > 0) { Save(); return true; }
        return false;
    }

    /// <summary>Toggle bookmark. Returns true = now bookmarked, false = removed.</summary>
    public static bool Toggle(string filePath, int page, string label)
    {
        if (IsBookmarked(filePath, page)) { Remove(filePath, page); return false; }
        Add(filePath, page, label);
        return true;
    }
}
