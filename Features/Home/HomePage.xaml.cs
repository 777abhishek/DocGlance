// HomePage.xaml.cs — Unified Recent / All Documents view with segmented toggle.

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;
using QuickView.Models;
using QuickView.FileSystem.Index;
using QuickView.FileSystem.Scanner;
using QuickView.Thumbnails;
using QuickView.Infrastructure.Logging;
using QuickView.Infrastructure.Storage;

namespace QuickView.Features.Home;

public sealed partial class HomePage : Page
{
#pragma warning disable CS0067  // subscribed by shell/navigation at runtime
    public event EventHandler<string>? DocumentOpened;
#pragma warning restore CS0067

    // ── Data sources ──────────────────────────────────────────────────────────
    public readonly ObservableCollection<DocumentItem> RecentDocuments   = new();
    public readonly ObservableCollection<DocumentItem> FilteredDocuments = new();
    public readonly ObservableCollection<string>       ScannedFolders    = new();

    private readonly ConcurrentBag<DocumentItem> _backgroundIndex = new();

    // ── State ─────────────────────────────────────────────────────────────────
    private bool   _loaded       = false;
    private bool   _showingRecent = true;   // true = Recent tab, false = All tab
    private bool   _isGridView   = true;
    private bool   _sortDesc     = false;
    private string _searchText   = string.Empty;
    private DispatcherQueue _dispatcherQueue = null!;

    // ── Scan cancellation ─────────────────────────────────────────────────────
    private readonly Dictionary<string, CancellationTokenSource> _scanTokens    = new();
    private readonly object                                       _scanTokenLock = new();

    // ── File type sets ────────────────────────────────────────────────────────
    private static readonly HashSet<string> ImageExts   = new(StringComparer.OrdinalIgnoreCase)
        { "PNG","JPG","JPEG","BMP","GIF","WEBP","TIFF" };
    private static readonly HashSet<string> VideoExts   = new(StringComparer.OrdinalIgnoreCase)
        { "MP4","MKV","AVI","MOV","WMV" };
    private static readonly HashSet<string> ArchiveExts = new(StringComparer.OrdinalIgnoreCase)
        { "ZIP","RAR","7Z","TAR","GZ" };
    private static readonly HashSet<string> CodeExts    = new(StringComparer.OrdinalIgnoreCase)
        { "CS","CPP","PY","JS","TS","JSON","XML","HTML","CSS" };


    // ═════════════════════════════════════════════════════════════════════════
    // CONSTRUCTOR + LOADED
    // ═════════════════════════════════════════════════════════════════════════

    public HomePage()
    {
        InitializeComponent();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;

        // Default state: Recent tab, grid layout, ascending sort
        GridViewToggle.IsChecked   = true;
        RecentModeButton.IsChecked = true;
        SetSortDirectionUI(ascending: true);

        // Clear button only relevant in Recent mode
        ClearButton.Visibility = Visibility.Visible;

        LoadRecentDocuments();
        RefreshDisplay();

        try
        {
            await FileIndexService.InitializeDatabaseAsync();
            await LoadCachedIndexAsync();
        }
        catch (Exception ex)
        {
            Logger.Log($"[DB ERROR] {ex.Message}");
        }
    }


    // ═════════════════════════════════════════════════════════════════════════
    // DATABASE CACHE
    // ═════════════════════════════════════════════════════════════════════════

    private async Task LoadCachedIndexAsync()
    {
        try
        {
            SetScanningState(true);

            var folders     = await FileIndexService.GetScannedFoldersAsync();
            var cachedFiles = await FileIndexService.LoadIndexedFilesAsync();

            foreach (var folder in folders)
                if (!ScannedFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                    ScannedFolders.Add(folder);

            var folderSet = new HashSet<string>(
                folders.Select(f => NormalisePath(f)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var file in cachedFiles)
                if (File.Exists(file.FilePath) &&
                    folderSet.Any(f => file.FilePath.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                    _backgroundIndex.Add(file);

            RefreshDisplay();
        }
        catch (Exception ex)
        {
            Logger.Log($"[DB ERROR] LoadCachedIndex: {ex.Message}");
        }
        finally { SetScanningState(false); }
    }


    // ═════════════════════════════════════════════════════════════════════════
    // MODE TOGGLE — Recent ↔ All Documents
    // ═════════════════════════════════════════════════════════════════════════

    private void ViewModeToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;

        _showingRecent = ReferenceEquals(sender, RecentModeButton);

        // Clear button only relevant in Recent mode
        ClearButton.Visibility = _showingRecent ? Visibility.Visible : Visibility.Collapsed;

        RefreshDisplay();
    }


    // ═════════════════════════════════════════════════════════════════════════
    // LAYOUT TOGGLE — Grid ↔ List
    // ═════════════════════════════════════════════════════════════════════════

    private void ViewToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        _isGridView = ReferenceEquals(sender, GridViewToggle);
        ApplyLayoutVisibility();
    }

    private void ApplyLayoutVisibility()
    {
        bool show = FilteredDocuments.Count > 0 || _showingRecent;
        DocumentsGridView.Visibility = (_loaded && _isGridView)  ? Visibility.Visible : Visibility.Collapsed;
        DocumentsListView.Visibility = (_loaded && !_isGridView) ? Visibility.Visible : Visibility.Collapsed;
    }


    // ═════════════════════════════════════════════════════════════════════════
    // RECENT FILES
    // ═════════════════════════════════════════════════════════════════════════

    public void LoadRecentDocuments()
    {
        RecentDocuments.Clear();
        foreach (var path in RecentFilesStore.Load())
        {
            if (!File.Exists(path)) continue;
            try { RecentDocuments.Add(DocumentItem.FromFileInfo(new FileInfo(path))); }
            catch { /* skip */ }
        }

        if (_showingRecent && _loaded) RefreshDisplay();
    }

    public void PushRecent(string filePath)
    {
        RecentFilesStore.Push(filePath);
        LoadRecentDocuments();
    }

    private void ClearRecent_Click(object sender, RoutedEventArgs e)
    {
        foreach (var doc in RecentDocuments.ToList())
            RecentFilesStore.Remove(doc.FilePath);
        RecentDocuments.Clear();
        RefreshDisplay();
    }


    // ═════════════════════════════════════════════════════════════════════════
    // SEARCH
    // ═════════════════════════════════════════════════════════════════════════

    public void SetSearchText(string text)
    {
        _searchText = text ?? string.Empty;
        if (HomeSearchBox != null) HomeSearchBox.Text = _searchText;
        if (_loaded) RefreshDisplay();
    }

    private void HomeSearch_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _searchText = sender.Text ?? string.Empty;
        if (_loaded) RefreshDisplay();
    }

    private void HomeSearch_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _searchText = args.QueryText ?? string.Empty;
        if (_loaded) RefreshDisplay();
    }


    // ═════════════════════════════════════════════════════════════════════════
    // SORT
    // ═════════════════════════════════════════════════════════════════════════

    private void SortAsc_Click(object sender, RoutedEventArgs e)
    {
        _sortDesc = false;
        SetSortDirectionUI(ascending: true);
        if (_loaded) RefreshDisplay();
    }

    private void SortDesc_Click(object sender, RoutedEventArgs e)
    {
        _sortDesc = true;
        SetSortDirectionUI(ascending: false);
        if (_loaded) RefreshDisplay();
    }

    private void SetSortDirectionUI(bool ascending)
    {
        if (SortAscButton == null || SortDescButton == null) return;
        SortAscButton.Style  = ascending ? (Style)Application.Current.Resources["AccentButtonStyle"] : null;
        SortDescButton.Style = ascending ? null : (Style)Application.Current.Resources["AccentButtonStyle"];
    }

    private void FileTypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        { if (_loaded) RefreshDisplay(); }

    private void SortOptions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        { if (_loaded) RefreshDisplay(); }


    // ═════════════════════════════════════════════════════════════════════════
    // CORE: RefreshDisplay — single method that rebuilds FilteredDocuments
    // ═════════════════════════════════════════════════════════════════════════

    private void RefreshDisplay()
    {
        FilteredDocuments.Clear();

        IEnumerable<DocumentItem> source = _showingRecent
            ? RecentDocuments
            : (IEnumerable<DocumentItem>)_backgroundIndex;

        // Search filter (applies to both modes)
        string search = _searchText.Trim();
        if (!string.IsNullOrEmpty(search))
            source = source.Where(d =>
                d.Title.Contains(search,      StringComparison.OrdinalIgnoreCase) ||
                d.FileType.Contains(search,   StringComparison.OrdinalIgnoreCase) ||
                d.FolderPath.Contains(search, StringComparison.OrdinalIgnoreCase));

        // Type filter (only meaningful in All mode)
        if (!_showingRecent)
        {
            string? typeTag = (FileTypeFilter?.SelectedItem as ComboBoxItem)?.Tag as string;
            if (!string.IsNullOrEmpty(typeTag))
                source = source.Where(d => MatchTypeGroup(d.FileType, typeTag!));
        }

        // Sort (only meaningful in All mode; Recent keeps insertion order)
        if (!_showingRecent)
        {
            int sortIdx = SortOptions?.SelectedIndex ?? 0;
            IOrderedEnumerable<DocumentItem> sorted = sortIdx switch
            {
                1 => source.OrderBy(d => d.LastWriteTime),
                2 => source.OrderBy(d => d.FileSizeBytes),
                3 => source.OrderBy(d => d.FileType).ThenBy(d => d.Title, StringComparer.OrdinalIgnoreCase),
                _ => source.OrderBy(d => d.Title, StringComparer.OrdinalIgnoreCase),
            };
            source = _sortDesc ? sorted.Reverse() : sorted;
        }

        foreach (var item in source)
            FilteredDocuments.Add(item);

        UpdateUI();
    }

    private static bool MatchTypeGroup(string ft, string tag) => tag switch
    {
        "IMAGE"   => ImageExts.Contains(ft),
        "VIDEO"   => VideoExts.Contains(ft),
        "ARCHIVE" => ArchiveExts.Contains(ft),
        "CODE"    => CodeExts.Contains(ft),
        "DOCX"    => ft is "DOCX" or "DOC",
        "PPTX"    => ft is "PPTX" or "PPT",
        "XLSX"    => ft is "XLSX" or "XLS" or "CSV",
        "TXT"     => ft is "TXT"  or "MD"  or "LOG" or "RTF",
        _         => string.Equals(ft, tag, StringComparison.OrdinalIgnoreCase),
    };


    // ═════════════════════════════════════════════════════════════════════════
    // OPEN FILE
    // ═════════════════════════════════════════════════════════════════════════

    private async void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            foreach (var ext in new[]{ ".pdf",".docx",".doc",".xlsx",".xls",".pptx",".ppt",
                                        ".txt",".md",".png",".jpg",".jpeg",".mp4",".mkv",".mp3" })
                picker.FileTypeFilter.Add(ext);
            InitializeWithWindow.Initialize(picker, QuickView.Shell.TitleBarWindow.Current?.Hwnd ?? IntPtr.Zero);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var props = await file.GetBasicPropertiesAsync();
            var doc = new DocumentItem
            {
                FilePath      = file.Path,
                Title         = file.DisplayName,
                FileType      = file.FileType.TrimStart('.').ToUpperInvariant(),
                FileSize      = FormatSize((long)props.Size),
                LastWriteTime = props.DateModified.DateTime,
            };
            QuickView.Shell.TitleBarWindow.Current?.OpenDocument(doc);
        }
        catch (Exception ex) { Logger.Error("OpenFile_Click", ex); }
    }


    // ═════════════════════════════════════════════════════════════════════════
    // ADD / REMOVE / RESCAN FOLDERS
    // ═════════════════════════════════════════════════════════════════════════

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, QuickView.Shell.TitleBarWindow.Current?.Hwnd ?? IntPtr.Zero);

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null) return;
            if (ScannedFolders.Contains(folder.Path, StringComparer.OrdinalIgnoreCase)) return;

            ScannedFolders.Add(folder.Path);
            await ScanFolderAsync(folder.Path);
        }
        catch (Exception ex) { Logger.Error("AddFolder_Click", ex); }
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string path) return;

        CancelScanForFolder(path);
        ScannedFolders.Remove(path);

        string prefix = NormalisePath(path);
        var keep = _backgroundIndex
            .Where(d => !d.FilePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        while (_backgroundIndex.TryTake(out _)) { }
        foreach (var item in keep) _backgroundIndex.Add(item);

        RefreshDisplay();
        ThumbnailService.PurgeCacheForFolder(path);
        _ = Task.Run(async () =>
        {
            try { await FileIndexService.RemoveFolderFromIndexAsync(path); }
            catch (Exception ex) { Logger.Log($"[DB] RemoveFolder: {ex.Message}"); }
        });
    }

    private async void Rescan_Click(object sender, RoutedEventArgs e)
    {
        lock (_scanTokenLock) { foreach (var c in _scanTokens.Values) c.Cancel(); _scanTokens.Clear(); }
        while (_backgroundIndex.TryTake(out _)) { }
        FilteredDocuments.Clear();
        await FileIndexService.ClearIndexAsync();
        foreach (var folder in ScannedFolders.ToList())
            await ScanFolderAsync(folder);
    }

    private async Task ScanFolderAsync(string folderPath)
    {
        CancelScanForFolder(folderPath);
        var cts = new CancellationTokenSource();
        lock (_scanTokenLock) _scanTokens[NormalisePath(folderPath)] = cts;

        SetScanningState(true);
        try
        {
            var known    = new HashSet<string>(_backgroundIndex.Select(d => d.FilePath), StringComparer.OrdinalIgnoreCase);
            var items    = await FolderScanner.ScanFolderAsync(folderPath, cts.Token);
            var newItems = items.Where(d => known.Add(d.FilePath)).ToList();

            foreach (var item in newItems) _backgroundIndex.Add(item);

            RefreshDisplay();

            _ = Task.Run(async () =>
            {
                try
                {
                    await FileIndexService.SaveFilesToIndexAsync(newItems);
                    await FileIndexService.AddScannedFolderAsync(folderPath, newItems.Count);
                }
                catch (Exception ex) { Logger.Log($"[DB] Save: {ex.Message}"); }
            });

            _ = LoadThumbnailsAsync(newItems, cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Logger.Log($"[SCAN] {folderPath}: {ex.Message}"); }
        finally
        {
            SetScanningState(false);
            lock (_scanTokenLock)
            {
                string key = NormalisePath(folderPath);
                if (_scanTokens.TryGetValue(key, out var s) && ReferenceEquals(s, cts))
                    _scanTokens.Remove(key);
            }
            cts.Dispose();
        }
    }

    private void CancelScanForFolder(string folderPath)
    {
        string key = NormalisePath(folderPath);
        lock (_scanTokenLock)
            if (_scanTokens.TryGetValue(key, out var cts)) { cts.Cancel(); _scanTokens.Remove(key); }
    }

    private static string NormalisePath(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
               .ToLowerInvariant() + Path.DirectorySeparatorChar;


    // ═════════════════════════════════════════════════════════════════════════
    // THUMBNAILS
    // ═════════════════════════════════════════════════════════════════════════

    private async Task LoadThumbnailsAsync(List<DocumentItem> items, CancellationToken ct)
    {
        foreach (var doc in items)
        {
            if (ct.IsCancellationRequested) break;
            if (!ThumbnailService.SupportsThumbnail($".{doc.FileType}")) continue;
            try
            {
                var thumb = await ThumbnailService.GetThumbnailAsync(doc.FilePath, ct);
                if (thumb != null) _dispatcherQueue.TryEnqueue(() => doc.Thumbnail = thumb);
            }
            catch (Exception ex) { Logger.Log($"[THUMB] {doc.Title}: {ex.Message}"); }
        }
    }


    // ═════════════════════════════════════════════════════════════════════════
    // ITEM CLICK (ItemsRepeater + ListView)
    // ═════════════════════════════════════════════════════════════════════════

    private void DocumentsRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
        => _ = args;

    private void Documents_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is DocumentItem doc)
            QuickView.Shell.TitleBarWindow.Current?.OpenDocument(doc);
    }


    // ═════════════════════════════════════════════════════════════════════════
    // UI HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    private void UpdateUI()
    {
        if (!_loaded) return;

        bool hasFolders = ScannedFolders.Count > 0;
        bool hasItems   = FilteredDocuments.Count > 0;
        bool hasIndex   = _backgroundIndex.Count > 0;

        // Empty state: show when no folders added AND not in Recent mode (or recent is empty)
        EmptyState.Visibility =
            (!hasFolders && (!_showingRecent || RecentDocuments.Count == 0))
                ? Visibility.Visible : Visibility.Collapsed;

        // No-results: show when in All mode with folders but no matches
        NoResultsState.Visibility =
            (!_showingRecent && hasFolders && hasIndex && !hasItems)
                ? Visibility.Visible : Visibility.Collapsed;

        // Content views
        DocumentsGridView.Visibility = (hasItems && _isGridView)  ? Visibility.Visible : Visibility.Collapsed;
        DocumentsListView.Visibility = (hasItems && !_isGridView) ? Visibility.Visible : Visibility.Collapsed;

        // Count label
        int count = FilteredDocuments.Count;
        if (DocumentCountLabel != null)
            DocumentCountLabel.Text = _showingRecent
                ? (count == 0 ? "No recent files" : count == 1 ? "1 recent file" : $"{count} recent files")
                : (count == 0 ? "0 documents"     : count == 1 ? "1 document"    : $"{count:N0} documents");

        // Statistics panel (All mode only)
        if (TotalFilesLabel != null)
            TotalFilesLabel.Text = _backgroundIndex.Count.ToString("N0");
        if (TotalSizeLabel != null)
            TotalSizeLabel.Text = FormatSize(_backgroundIndex.Sum(d => d.FileSizeBytes));

        if (RescanButton != null)
            RescanButton.Visibility = hasFolders ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetScanningState(bool scanning)
    {
        if (ScanProgressRing != null)
        {
            ScanProgressRing.IsActive   = scanning;
            ScanProgressRing.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        }
        if (RescanButton    != null) RescanButton.IsEnabled    = !scanning;
        if (AddFolderButton != null) AddFolderButton.IsEnabled = !scanning;
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B","KB","MB","GB","TB" };
        double v = bytes; int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.##} {units[i]}";
    }
}
