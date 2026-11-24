// HomeViewModel.cs — Observable state for HomePage (scan, filter, sort, search).

using CommunityToolkit.Mvvm.ComponentModel;
using DocGlance.Core.Models;
using DocGlance.Common.Formatters;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;

namespace DocGlance.Features.Home;

/// <summary>
/// Holds all non-UI state for the Home page: documents, scan tokens, filter, sort, and search.
/// Phase 1 refactoring: extracted from HomePage.xaml.cs to enable unit testing.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    // ── Document Collections ──────────────────────────────────────────────────

    public ObservableCollection<DocumentItem> RecentDocuments   { get; } = new();
    public ObservableCollection<DocumentItem> FilteredDocuments { get; } = new();
    public ObservableCollection<string>       ScannedFolders    { get; } = new();

    /// <summary>Full background index from all scanned folders.</summary>
    public ConcurrentBag<DocumentItem> BackgroundIndex { get; } = new();
    private readonly object _backgroundIndexLock = new();

    // ── View State ────────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _showingRecent = true;

    [ObservableProperty]
    private bool _sortDescending;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _sortModeIndex;

    [ObservableProperty]
    private string _typeFilterTag = string.Empty;

    [ObservableProperty]
    private string _groupByMode = "None";

    // ── Grouped Data ──────────────────────────────────────────────────────────

    public ObservableCollection<DocumentGroup> GroupedDocuments { get; } = new();

    // ── Scan Cancellation ────────────────────────────────────────────────────

    private readonly Dictionary<string, CancellationTokenSource> _scanTokens    = new();
    private readonly object                                       _scanTokenLock = new();

    // ── Computed Display Strings ─────────────────────────────────────────────

    public string DocumentCountText
    {
        get
        {
            int count = GroupByMode != "None" && GroupedDocuments.Count > 0
                ? GroupedDocuments.Sum(g => g.ItemCount)
                : FilteredDocuments.Count;
                
            return ShowingRecent
                ? count == 0 ? "No recent files"
                : count == 1 ? "1 recent file"
                : $"{count} recent files"
                : count == 0 ? "0 documents"
                : count == 1 ? "1 document"
                : $"{count:N0} documents";
        }
    }

    public string TotalFilesText  => BackgroundIndex.Count.ToString("N0");
    public string TotalSizeText   => FileSizeFormatter.Format(
        BackgroundIndex.Sum(d => d.FileSizeBytes));

    // ── Filter + Sort Logic ───────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds <see cref="FilteredDocuments"/> from the active source applying
    /// current search, type-filter, and sort settings.
    /// Must be called on the UI thread.
    /// </summary>
    public void RefreshFilter()
    {
        FilteredDocuments.Clear();
        GroupedDocuments.Clear();

        IEnumerable<DocumentItem> source = ShowingRecent
            ? RecentDocuments
            : (IEnumerable<DocumentItem>)BackgroundIndex;

        // Search
        string search = SearchText.Trim();
        if (!string.IsNullOrEmpty(search))
            source = source.Where(d =>
                d.Title.Contains(search,      StringComparison.OrdinalIgnoreCase) ||
                d.FileType.Contains(search,   StringComparison.OrdinalIgnoreCase) ||
                d.FolderPath.Contains(search, StringComparison.OrdinalIgnoreCase));

        // Type filter (All mode only)
        if (!ShowingRecent && !string.IsNullOrEmpty(TypeFilterTag))
            source = source.Where(d => MatchTypeGroup(d.FileType, TypeFilterTag));

        // Sort (All mode only; Recent keeps insertion order)
        if (!ShowingRecent)
        {
            IOrderedEnumerable<DocumentItem> sorted = SortModeIndex switch
            {
                1 => source.OrderBy(d => d.LastWriteTime),
                2 => source.OrderBy(d => d.FileSizeBytes),
                3 => source.OrderBy(d => d.FileType).ThenBy(d => d.Title, StringComparer.OrdinalIgnoreCase),
                _ => source.OrderBy(d => d.Title, StringComparer.OrdinalIgnoreCase),
            };
            source = SortDescending ? sorted.Reverse() : sorted;
        }

        // Group by mode
        if (!ShowingRecent && GroupByMode != "None")
        {
            var groups = GroupByMode switch
            {
                "Folder" => source.GroupBy(d => d.FolderPath)
                                  .OrderBy(g => g.Key)
                                  .Select(g => new DocumentGroup(g.Key, g.ToList())),
                "FileType" => source.GroupBy(d => d.FileType)
                                    .OrderBy(g => g.Key)
                                    .Select(g => new DocumentGroup(GetFileTypeGroupName(g.Key), g.ToList())),
                "Date" => source.GroupBy(d => GetDateGroup(d.LastWriteTime))
                                .OrderBy(g => GetDateGroupOrder(g.Key))
                                .Select(g => new DocumentGroup(g.Key, g.ToList())),
                _ => Enumerable.Empty<DocumentGroup>()
            };

            foreach (var group in groups)
                GroupedDocuments.Add(group);
        }
        else
        {
            foreach (var item in source)
                FilteredDocuments.Add(item);
        }

        // Notify derived display strings
        OnPropertyChanged(nameof(DocumentCountText));
        OnPropertyChanged(nameof(TotalFilesText));
        OnPropertyChanged(nameof(TotalSizeText));
    }

    private static string GetFileTypeGroupName(string fileType)
    {
        return fileType switch
        {
            "PDF" => "PDF Documents",
            "DOCX" or "DOC" => "Word Documents",
            "XLSX" or "XLS" or "CSV" => "Excel Spreadsheets",
            "PPTX" or "PPT" => "PowerPoint Presentations",
            "PNG" or "JPG" or "JPEG" or "BMP" or "GIF" or "WEBP" => "Images",
            "MP4" or "MKV" or "AVI" or "MOV" or "WMV" => "Videos",
            "TXT" or "MD" or "LOG" or "RTF" => "Text Files",
            "ZIP" or "RAR" or "7Z" or "TAR" or "GZ" => "Archives",
            "CS" or "CPP" or "PY" or "JS" or "TS" or "HTML" or "CSS" => "Code Files",
            _ => fileType.ToUpperInvariant() + " Files"
        };
    }

    private static string GetDateGroup(DateTime date)
    {
        var now = DateTime.Now;
        var diff = now - date;

        if (date.Date == now.Date) return "Today";
        if (date.Date == now.Date.AddDays(-1)) return "Yesterday";
        if (diff.TotalDays < 7) return "This Week";
        if (diff.TotalDays < 30) return "This Month";
        if (date.Year == now.Year) return date.ToString("MMMM yyyy");
        return date.ToString("yyyy");
    }

    private static int GetDateGroupOrder(string groupName)
    {
        return groupName switch
        {
            "Today" => 0,
            "Yesterday" => 1,
            "This Week" => 2,
            "This Month" => 3,
            _ => 4
        };
    }

    // ── Scan Token Management ─────────────────────────────────────────────────

    public CancellationTokenSource CreateScanToken(string folderPath)
    {
        CancelScanForFolder(folderPath);
        var cts = new CancellationTokenSource();
        lock (_scanTokenLock)
            _scanTokens[NormalizePath(folderPath)] = cts;
        return cts;
    }

    public void RemoveScanToken(string folderPath, CancellationTokenSource cts)
    {
        lock (_scanTokenLock)
        {
            string key = NormalizePath(folderPath);
            if (_scanTokens.TryGetValue(key, out var stored) && ReferenceEquals(stored, cts))
                _scanTokens.Remove(key);
        }
        cts.Dispose();
    }

    public void CancelScanForFolder(string folderPath)
    {
        string key = NormalizePath(folderPath);
        lock (_scanTokenLock)
            if (_scanTokens.TryGetValue(key, out var cts)) { cts.Cancel(); _scanTokens.Remove(key); }
    }

    public void CancelAllScans()
    {
        lock (_scanTokenLock)
        {
            foreach (var cts in _scanTokens.Values) cts.Cancel();
            _scanTokens.Clear();
        }
    }

    // ── Folder Management ────────────────────────────────────────────────────

    /// <summary>Removes all indexed files whose path starts with <paramref name="folderPath"/>.</summary>
    public void RemoveFolderFromIndex(string folderPath)
    {
        lock (_backgroundIndexLock)
        {
            var keep = BackgroundIndex
                .Where(document => !IsPathWithinRoot(document.FilePath, folderPath))
                .ToList();
            ReplaceBackgroundIndex(keep);
        }
    }

    public void UpsertIndexedDocument(DocumentItem document)
        => UpsertIndexedDocuments(new[] { document });

    public void UpsertIndexedDocuments(IEnumerable<DocumentItem> documents)
    {
        var updates = documents
            .GroupBy(document => document.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(),
                StringComparer.OrdinalIgnoreCase);
        if (updates.Count == 0)
            return;

        lock (_backgroundIndexLock)
        {
            var items = BackgroundIndex
                .GroupBy(document => document.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
            foreach (var (path, document) in updates)
                items[path] = document;
            ReplaceBackgroundIndex(items.Values);
        }
    }

    public void RemoveIndexedDocument(string filePath)
    {
        lock (_backgroundIndexLock)
        {
            var items = BackgroundIndex
                .Where(document => !string.Equals(
                    document.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            ReplaceBackgroundIndex(items);
        }
    }

    public void ReconcileIndexedFolder(
        string folderPath, IEnumerable<DocumentItem> documents, bool isComplete)
    {
        lock (_backgroundIndexLock)
        {
            var items = BackgroundIndex
                .Where(document => !isComplete || !IsPathWithinRoot(document.FilePath, folderPath))
                .GroupBy(document => document.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
            foreach (DocumentItem document in documents)
                items[document.FilePath] = document;
            ReplaceBackgroundIndex(items.Values);
        }
    }

    private void ReplaceBackgroundIndex(IEnumerable<DocumentItem> documents)
    {
        while (BackgroundIndex.TryTake(out _)) { }
        foreach (DocumentItem document in documents)
            BackgroundIndex.Add(document);
    }

    public static bool IsPathWithinRoot(string filePath, string folderPath)
    {
        string root = Path.GetFullPath(folderPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(filePath);
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    // ── Type Group Matching ──────────────────────────────────────────────────

    public static bool MatchTypeGroup(string ft, string tag) => tag switch
    {
        "IMAGE"   => FileTypeGroups.ImageExts.Contains(ft),
        "VIDEO"   => FileTypeGroups.VideoExts.Contains(ft),
        "ARCHIVE" => FileTypeGroups.ArchiveExts.Contains(ft),
        "CODE"    => FileTypeGroups.CodeExts.Contains(ft),
        "DOCX"    => ft is "DOCX" or "DOC",
        "PPTX"    => ft is "PPTX" or "PPT",
        "XLSX"    => ft is "XLSX" or "XLS" or "CSV",
        "TXT"     => ft is "TXT"  or "MD"  or "LOG" or "RTF",
        _         => string.Equals(ft, tag, StringComparison.OrdinalIgnoreCase),
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    public static string NormalizePath(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
               .ToLowerInvariant() + Path.DirectorySeparatorChar;
}

/// <summary>Centralised file-type extension sets used by Home filter and scanner.</summary>
public static class FileTypeGroups
{
    public static readonly HashSet<string> ImageExts   = new(StringComparer.OrdinalIgnoreCase)
        { "PNG","JPG","JPEG","BMP","GIF","WEBP","TIFF" };
    public static readonly HashSet<string> VideoExts   = new(StringComparer.OrdinalIgnoreCase)
        { "MP4","MKV","AVI","MOV","WMV" };
    public static readonly HashSet<string> ArchiveExts = new(StringComparer.OrdinalIgnoreCase)
        { "ZIP","RAR","7Z","TAR","GZ" };
    public static readonly HashSet<string> CodeExts    = new(StringComparer.OrdinalIgnoreCase)
        { "CS","CPP","PY","JS","TS","JSON","XML","HTML","CSS" };
}
