// HomeViewModel.cs — Observable state for HomePage (scan, filter, sort, search).

using CommunityToolkit.Mvvm.ComponentModel;
using QuickView.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;

namespace QuickView.Features.Home;

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

    // ── View State ────────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _showingRecent = true;

    [ObservableProperty]
    private bool _isGridView = true;

    [ObservableProperty]
    private bool _sortDescending;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _sortModeIndex;

    [ObservableProperty]
    private string _typeFilterTag = string.Empty;

    // ── Scan Cancellation ────────────────────────────────────────────────────

    private readonly Dictionary<string, CancellationTokenSource> _scanTokens    = new();
    private readonly object                                       _scanTokenLock = new();

    // ── Computed Display Strings ─────────────────────────────────────────────

    public string DocumentCountText
    {
        get
        {
            int count = FilteredDocuments.Count;
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
    public string TotalSizeText   => Shared.Formatters.FileSizeFormatter.Format(
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

        foreach (var item in source)
            FilteredDocuments.Add(item);

        // Notify derived display strings
        OnPropertyChanged(nameof(DocumentCountText));
        OnPropertyChanged(nameof(TotalFilesText));
        OnPropertyChanged(nameof(TotalSizeText));
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
        string prefix = NormalizePath(folderPath);
        var keep = BackgroundIndex
            .Where(d => !d.FilePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        while (BackgroundIndex.TryTake(out _)) { }
        foreach (var item in keep) BackgroundIndex.Add(item);
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
