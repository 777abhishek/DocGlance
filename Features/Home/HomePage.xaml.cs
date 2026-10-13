// HomePage.xaml.cs — Unified Recent / All Documents view with segmented toggle.

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;
using QuickView.Core.Models;
using QuickView.Common.Formatters;
using QuickView.Services.FileSystem.Index;
using QuickView.Services.FileSystem.Scanner;
using QuickView.Services.Thumbnails;
using QuickView.Infrastructure.Logging;
using QuickView.Services.Storage;

namespace QuickView.Features.Home;

public sealed partial class HomePage : Page
{
#pragma warning disable CS0067  // subscribed by shell/navigation at runtime
    public event EventHandler<string>? DocumentOpened;
#pragma warning restore CS0067

    // ── ViewModel ─────────────────────────────────────────────────────────────
    public HomeViewModel ViewModel { get; } = new();

    // ── Convenience accessors (keep existing external API surface) ────────────
    public System.Collections.ObjectModel.ObservableCollection<DocumentItem> RecentDocuments   => ViewModel.RecentDocuments;
    public System.Collections.ObjectModel.ObservableCollection<DocumentItem> FilteredDocuments => ViewModel.FilteredDocuments;
    public System.Collections.ObjectModel.ObservableCollection<string>       ScannedFolders    => ViewModel.ScannedFolders;

    // ── Page-level state (UI concerns only) ───────────────────────────────────
    private bool   _loaded;
    private DispatcherQueue _dispatcherQueue = null!;


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
        RecentModeButton.IsChecked = true;
        SetSortDirectionUI(ascending: true);
        ClearButton.Visibility = Visibility.Visible;

        LoadRecentDocuments();
        RefreshDisplay();

        try
        {
            await FileIndexService.InitializeDatabaseAsync();
            await LoadCachedIndexAsync();
        }
        catch (Exception ex) { Logger.Log($"[DB ERROR] {ex.Message}"); }
    }


    // ═════════════════════════════════════════════════════════════════════════
    // DATABASE CACHE
    // ═════════════════════════════════════════════════════════════════════════

    private async Task LoadCachedIndexAsync()
    {
        try
        {
            ViewModel.IsScanning = true;
            var folders     = await FileIndexService.GetScannedFoldersAsync();
            var cachedFiles = await FileIndexService.LoadIndexedFilesAsync();

            foreach (var folder in folders)
                if (!ViewModel.ScannedFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                    ViewModel.ScannedFolders.Add(folder);

            var folderSet = new System.Collections.Generic.HashSet<string>(
                folders.Select(f => HomeViewModel.NormalizePath(f)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var file in cachedFiles)
                if (File.Exists(file.FilePath) &&
                    folderSet.Any(f => file.FilePath.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                    ViewModel.BackgroundIndex.Add(file);

            RefreshDisplay();
        }
        catch (Exception ex) { Logger.Log($"[DB ERROR] LoadCachedIndex: {ex.Message}"); }
        finally { ViewModel.IsScanning = false; }
    }


    // ═════════════════════════════════════════════════════════════════════════
    // MODE TOGGLE — Recent ↔ All Documents
    // ═════════════════════════════════════════════════════════════════════════

    private void ViewModeToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        ViewModel.ShowingRecent = ReferenceEquals(sender, RecentModeButton);
        ClearButton.Visibility = ViewModel.ShowingRecent ? Visibility.Visible : Visibility.Collapsed;
        RefreshDisplay();
    }


    // ═════════════════════════════════════════════════════════════════════════
    // RECENT FILES
    // ═════════════════════════════════════════════════════════════════════════

    public void LoadRecentDocuments()
    {
        ViewModel.RecentDocuments.Clear();
        foreach (var path in RecentFilesStore.Load())
        {
            if (!File.Exists(path)) continue;
            try { ViewModel.RecentDocuments.Add(DocumentItem.FromFileInfo(new FileInfo(path))); }
            catch { }
        }
        if (ViewModel.ShowingRecent && _loaded) RefreshDisplay();
    }

    public void PushRecent(string filePath)
    {
        RecentFilesStore.Push(filePath);
        LoadRecentDocuments();
    }

    private void ClearRecent_Click(object sender, RoutedEventArgs e)
    {
        foreach (var doc in ViewModel.RecentDocuments.ToList())
            RecentFilesStore.Remove(doc.FilePath);
        ViewModel.RecentDocuments.Clear();
        RefreshDisplay();
    }


    // ═════════════════════════════════════════════════════════════════════════
    // SEARCH
    // ═════════════════════════════════════════════════════════════════════════

    public void SetSearchText(string text)
    {
        ViewModel.SearchText = text ?? string.Empty;
        if (HomeSearchBox != null) HomeSearchBox.Text = ViewModel.SearchText;
        if (_loaded) RefreshDisplay();
    }

    private void HomeSearch_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        ViewModel.SearchText = sender.Text ?? string.Empty;
        if (_loaded) RefreshDisplay();
    }

    private void HomeSearch_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        ViewModel.SearchText = args.QueryText ?? string.Empty;
        if (_loaded) RefreshDisplay();
    }


    // ═════════════════════════════════════════════════════════════════════════
    // SORT
    // ═════════════════════════════════════════════════════════════════════════

    private void SortAsc_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SortDescending = false;
        SetSortDirectionUI(ascending: true);
        if (_loaded) RefreshDisplay();
    }

    private void SortDesc_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SortDescending = true;
        SetSortDirectionUI(ascending: false);
        if (_loaded) RefreshDisplay();
    }

    private void SetSortDirectionUI(bool ascending)
    {
        // Sort direction is managed by HomeViewModel; the current UI exposes
        // sort criteria through SortOptions rather than separate direction buttons.
    }

    private void FileTypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.TypeFilterTag = (FileTypeFilter?.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
        if (_loaded) RefreshDisplay();
    }

    private void SortOptions_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.SortModeIndex = SortOptions?.SelectedIndex ?? 0;
        if (_loaded) RefreshDisplay();
    }

    private void GroupByOptions_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.GroupByMode = (GroupByOptions?.SelectedItem as ComboBoxItem)?.Tag as string ?? "None";
        if (_loaded) RefreshDisplay();
    }


    // ═════════════════════════════════════════════════════════════════════════
    // CORE: RefreshDisplay — single method that rebuilds FilteredDocuments
    // ═════════════════════════════════════════════════════════════════════════

    private void RefreshDisplay()
    {
        ViewModel.RefreshFilter();
        UpdateUI();
    }

    private static bool MatchTypeGroup(string ft, string tag)
        => HomeViewModel.MatchTypeGroup(ft, tag);


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
            InitializeWithWindow.Initialize(picker, QuickView.UI.Shell.TitleBarWindow.Current?.Hwnd ?? IntPtr.Zero);

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
            QuickView.UI.Shell.TitleBarWindow.Current?.OpenDocument(doc);
        }
        catch (Exception ex) { Logger.Error("OpenFile_Click", ex); }
    }

    private void Tools_Click(object sender, RoutedEventArgs e)
    {
        Logger.Log("[HomePage] Document Tools button clicked from filter section.");
        QuickView.UI.Shell.TitleBarWindow.Current?.OpenToolsPage();
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
            InitializeWithWindow.Initialize(picker, QuickView.UI.Shell.TitleBarWindow.Current?.Hwnd ?? IntPtr.Zero);

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

        ViewModel.CancelScanForFolder(path);
        ViewModel.ScannedFolders.Remove(path);
        ViewModel.RemoveFolderFromIndex(path);

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
        ViewModel.CancelAllScans();
        while (ViewModel.BackgroundIndex.TryTake(out _)) { }
        FilteredDocuments.Clear();
        await FileIndexService.ClearIndexAsync();
        foreach (var folder in ViewModel.ScannedFolders.ToList())
            await ScanFolderAsync(folder);
    }

    private async Task ScanFolderAsync(string folderPath)
    {
        var cts = ViewModel.CreateScanToken(folderPath);
        ViewModel.IsScanning = true;
        try
        {
            var known    = new System.Collections.Generic.HashSet<string>(
                ViewModel.BackgroundIndex.Select(d => d.FilePath), StringComparer.OrdinalIgnoreCase);
            var items    = await FolderScanner.ScanFolderAsync(folderPath, cts.Token);
            var newItems = items.Where(d => known.Add(d.FilePath)).ToList();

            foreach (var item in newItems) ViewModel.BackgroundIndex.Add(item);
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
            ViewModel.IsScanning = false;
            ViewModel.RemoveScanToken(folderPath, cts);
        }
    }


    // ═════════════════════════════════════════════════════════════════════════
    // THUMBNAILS
    // ═════════════════════════════════════════════════════════════════════════

    private async Task LoadThumbnailsAsync(System.Collections.Generic.List<DocumentItem> items, System.Threading.CancellationToken ct)
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


    // ═════════════════════════════════════════════════════════════════════════
    // UI HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    private void UpdateUI()
    {
        if (!_loaded) return;

        bool hasFolders = ViewModel.ScannedFolders.Count > 0;
        bool hasItems   = ViewModel.FilteredDocuments.Count > 0;
        bool hasGroups  = ViewModel.GroupedDocuments.Count > 0;
        bool hasIndex   = ViewModel.BackgroundIndex.Count > 0;
        bool isGrouped  = !ViewModel.ShowingRecent && ViewModel.GroupByMode != "None";

        EmptyState.Visibility =
            (!hasFolders && (!ViewModel.ShowingRecent || ViewModel.RecentDocuments.Count == 0))
                ? Visibility.Visible : Visibility.Collapsed;

        NoResultsState.Visibility =
            (!ViewModel.ShowingRecent && hasFolders && hasIndex && !hasItems && !hasGroups)
                ? Visibility.Visible : Visibility.Collapsed;

        DocumentsGridView.Visibility = (!isGrouped && hasItems) ? Visibility.Visible : Visibility.Collapsed;
        GroupedDocumentsView.Visibility = (isGrouped && hasGroups) ? Visibility.Visible : Visibility.Collapsed;

        if (DocumentCountLabel != null)
            DocumentCountLabel.Text = ViewModel.DocumentCountText;

        if (RescanButton != null)
            RescanButton.Visibility = hasFolders ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetScanningState(bool scanning)
    {
        ViewModel.IsScanning = scanning;
        if (ScanProgressRing != null)
        {
            ScanProgressRing.IsActive   = scanning;
            ScanProgressRing.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        }
        if (RescanButton    != null) RescanButton.IsEnabled    = !scanning;
        if (AddFolderButton != null) AddFolderButton.IsEnabled = !scanning;
    }

    private static string FormatSize(long bytes)
        => FileSizeFormatter.Format(bytes);

    // ── Quick Access Navigation ────────────────────────────────────────────────

    private void NavigateToSettings_Click(object sender, RoutedEventArgs e)
    {
        UI.Shell.TitleBarWindow.Current?.OpenSettingsTab();
    }

    private void NavigateToTools_Click(object sender, RoutedEventArgs e)
    {
        UI.Shell.TitleBarWindow.Current?.OpenToolsPage();
    }
}
