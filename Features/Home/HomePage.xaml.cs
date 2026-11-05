// HomePage.xaml.cs — Unified Recent / All Documents view with segmented toggle.

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;
using DocGlance.Core.Models;
using DocGlance.Common.Formatters;
using DocGlance.Services.FileSystem.Index;
using DocGlance.Services.FileSystem.Scanner;
using DocGlance.Services.Thumbnails;
using DocGlance.Infrastructure.Logging;
using DocGlance.Services.Storage;

namespace DocGlance.Features.Home;

public sealed partial class HomePage : Page
{
#pragma warning disable CS0067  // subscribed by shell/navigation at runtime
    public event EventHandler<string>? DocumentOpened;
#pragma warning restore CS0067

    // ── ViewModel ─────────────────────────────────────────────────────────────
    public HomeViewModel ViewModel { get; } = new();
    public ObservableCollection<DocGlance.Services.Storage.SavedWorkspace> Workspaces { get; } = new();

    // ── Convenience accessors (keep existing external API surface) ────────────
    public System.Collections.ObjectModel.ObservableCollection<DocumentItem> RecentDocuments   => ViewModel.RecentDocuments;
    public System.Collections.ObjectModel.ObservableCollection<DocumentItem> FilteredDocuments => ViewModel.FilteredDocuments;
    public System.Collections.ObjectModel.ObservableCollection<string>       ScannedFolders    => ViewModel.ScannedFolders;

    // ── Page-level state (UI concerns only) ───────────────────────────────────
    private bool   _loaded;
    private bool   _showingSpaces;
    private int    _recentLoadGeneration;
    private DispatcherQueue _dispatcherQueue = null!;
    private List<DocGlance.Services.Storage.SavedWorkspace> _allWorkspaces = new();


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
        LoadWorkspaces();
        RefreshDisplay();

        // Initialize database in background - don't block UI
        _ = Task.Run(async () =>
        {
            try
            {
                await FileIndexService.InitializeDatabaseAsync();
            }
            catch (Exception ex) { Logger.Log($"[DB ERROR] {ex.Message}"); }
        });
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
            {
                if (!ViewModel.ScannedFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                    ViewModel.ScannedFolders.Add(folder);
                try
                {
                    await StorageAccessStore.GetFolderAsync(folder);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[SCAN] Cached folder access is unavailable for '{folder}': {ex.Message}");
                }
            }

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

    private async void ViewModeToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        ViewModel.ShowingRecent = ReferenceEquals(sender, RecentModeButton);
        _showingSpaces = ReferenceEquals(sender, SpacesModeButton);
        ClearButton.Visibility = ViewModel.ShowingRecent ? Visibility.Visible : Visibility.Collapsed;
        
        // Lazy load: only load index when switching to All Documents mode for the first time
        if (!ViewModel.ShowingRecent && !_showingSpaces &&
            ViewModel.BackgroundIndex.IsEmpty && ViewModel.ScannedFolders.Count == 0)
        {
            await LoadCachedIndexAsync();
        }
        
        RefreshDisplay();
    }


    // ═════════════════════════════════════════════════════════════════════════
    // RECENT FILES
    // ═════════════════════════════════════════════════════════════════════════

    public void LoadRecentDocuments()
    {
        _ = LoadRecentDocumentsAsync(++_recentLoadGeneration);
    }

    private async Task LoadRecentDocumentsAsync(int generation)
    {
        var documents = new List<DocumentItem>();
        foreach (string path in RecentFilesStore.Load())
        {
            try
            {
                Windows.Storage.StorageFile file = await StorageAccessStore.GetFileAsync(path);
                var properties = await file.GetBasicPropertiesAsync();
                long fileSize = checked((long)properties.Size);
                documents.Add(new DocumentItem
                {
                    FilePath = file.Path,
                    Title = file.Name,
                    FolderPath = Path.GetDirectoryName(file.Path) ?? string.Empty,
                    FileType = file.FileType.TrimStart('.').ToUpperInvariant(),
                    FileSizeBytes = fileSize,
                    FileSize = FileSizeFormatter.Format(fileSize),
                    LastWriteTime = properties.DateModified.DateTime,
                    LastModified = properties.DateModified.DateTime.ToString("MMM dd, yyyy"),
                });
            }
            catch (Exception ex)
            {
                Logger.Warn($"[RecentFiles] Could not restore access to '{path}': {ex.Message}");
            }
        }

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (generation != _recentLoadGeneration) return;

            ViewModel.RecentDocuments.Clear();
            foreach (var document in documents)
                ViewModel.RecentDocuments.Add(document);

            if (ViewModel.ShowingRecent && _loaded) RefreshDisplay();
        });
    }

    public void PushRecent(string filePath)
    {
        RecentFilesStore.Push(filePath);
        LoadRecentDocuments();
    }

    private void ClearRecent_Click(object sender, RoutedEventArgs e)
    {
        // Ignore any in-flight disk scan so it cannot repopulate the cleared list.
        _recentLoadGeneration++;
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
        if (_showingSpaces)
            ApplyWorkspaceFilter();
        else
            ViewModel.RefreshFilter();
        UpdateUI();
    }

    private void LoadWorkspaces()
    {
        _allWorkspaces = DocGlance.Services.Storage.WorkspaceStore.Load();
        ApplyWorkspaceFilter();
        if (_loaded && _showingSpaces)
            UpdateUI();
    }

    public void RefreshWorkspaces() => LoadWorkspaces();

    private void ApplyWorkspaceFilter()
    {
        string search = ViewModel.SearchText.Trim();
        Workspaces.Clear();
        foreach (var workspace in _allWorkspaces.Where(workspace =>
                     string.IsNullOrEmpty(search) ||
                     workspace.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                     workspace.FilePaths.Any(path =>
                         Path.GetFileName(path).Contains(search, StringComparison.OrdinalIgnoreCase))))
            Workspaces.Add(workspace);
    }

    private async void SaveWorkspace_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox
        {
            PlaceholderText = "Workspace name",
            MaxLength = 80,
        };
        var dialog = new ContentDialog
        {
            Title = "Save current tabs as a workspace",
            Content = nameBox,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        try
        {
            var window = UI.Shell.TitleBarWindow.Current
                ?? throw new InvalidOperationException("The active DocGlance window is unavailable.");
            window.SaveCurrentWorkspace(nameBox.Text);
            LoadWorkspaces();
            SpacesModeButton.IsChecked = true;
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save workspace", ex);
            await ShowWorkspaceMessageAsync(ex.Message);
        }
    }

    private async void OpenWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DocGlance.Services.Storage.SavedWorkspace workspace })
            return;

        var dialog = new ContentDialog
        {
            Title = $"Open \"{workspace.Name}\"",
            Content = $"Open {workspace.DocumentCount} documents in the current window or in a new window?",
            PrimaryButtonText = "Current window",
            SecondaryButtonText = "New window",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None)
            return;

        try
        {
            var window = UI.Shell.TitleBarWindow.Current
                ?? throw new InvalidOperationException("The active DocGlance window is unavailable.");
            window.OpenWorkspace(workspace, result == ContentDialogResult.Secondary);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to open workspace: {workspace.Name}", ex);
            await ShowWorkspaceMessageAsync(ex.Message);
        }
    }

    private async void UpdateWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DocGlance.Services.Storage.SavedWorkspace workspace })
            return;

        try
        {
            var window = UI.Shell.TitleBarWindow.Current
                ?? throw new InvalidOperationException("The active DocGlance window is unavailable.");
            window.SaveCurrentWorkspace(workspace.Name, workspace.Id);
            LoadWorkspaces();
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to update workspace: {workspace.Name}", ex);
            await ShowWorkspaceMessageAsync(ex.Message);
        }
    }

    private void SetStartupWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DocGlance.Services.Storage.SavedWorkspace workspace })
            return;

        DocGlance.Services.Storage.WorkspaceStore.SetStartup(
            workspace.IsStartup ? null : workspace.Id);
        LoadWorkspaces();
    }

    private async void DeleteWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DocGlance.Services.Storage.SavedWorkspace workspace })
            return;

        var dialog = new ContentDialog
        {
            Title = $"Delete \"{workspace.Name}\"?",
            Content = "This removes the saved workspace, but does not delete any documents.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        DocGlance.Services.Storage.WorkspaceStore.Delete(workspace.Id);
        LoadWorkspaces();
    }

    private async Task ShowWorkspaceMessageAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "Workspace",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
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
                                        ".txt",".md",".markdown",".mdx",".mmd",".png",".jpg",".jpeg",".bmp",".gif",
                                        ".tif",".tiff",".svg",".svgz",".dds",".ico",".jxr",".wdp",".mp4",".mkv",".mp3" })
                picker.FileTypeFilter.Add(ext);
            InitializeWithWindow.Initialize(picker, DocGlance.UI.Shell.TitleBarWindow.Current?.Hwnd ?? IntPtr.Zero);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            StorageAccessStore.RememberFile(file);

            var props = await file.GetBasicPropertiesAsync();
            var doc = new DocumentItem
            {
                FilePath      = file.Path,
                Title         = file.DisplayName,
                FileType      = file.FileType.TrimStart('.').ToUpperInvariant(),
                FileSize      = FormatSize((long)props.Size),
                LastWriteTime = props.DateModified.DateTime,
            };
            DocGlance.UI.Shell.TitleBarWindow.Current?.OpenDocument(doc);
        }
        catch (Exception ex) { Logger.Error("OpenFile_Click", ex); }
    }

    private void Tools_Click(object sender, RoutedEventArgs e)
    {
        Logger.Log("[HomePage] Document Tools button clicked from filter section.");
        DocGlance.UI.Shell.TitleBarWindow.Current?.OpenToolsPage();
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
            InitializeWithWindow.Initialize(picker, DocGlance.UI.Shell.TitleBarWindow.Current?.Hwnd ?? IntPtr.Zero);

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null) return;

            StorageAccessStore.RememberFolder(folder);
            if (!ScannedFolders.Contains(folder.Path, StringComparer.OrdinalIgnoreCase))
                ScannedFolders.Add(folder.Path);
            await ScanFolderAsync(folder);
        }
        catch (Exception ex) { Logger.Error("AddFolder_Click", ex); }
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string path) return;

        StorageAccessStore.ForgetFolder(path);
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
        try
        {
            var folder = await StorageAccessStore.GetFolderAsync(folderPath);
            await ScanFolderAsync(folder);
        }
        catch (Exception ex)
        {
            Logger.Error($"[SCAN] Could not restore folder access: {folderPath}", ex);
        }
    }

    private async Task ScanFolderAsync(Windows.Storage.StorageFolder folder)
    {
        string folderPath = folder.Path;
        var cts = ViewModel.CreateScanToken(folderPath);
        ViewModel.IsScanning = true;
        try
        {
            var known    = new System.Collections.Generic.HashSet<string>(
                ViewModel.BackgroundIndex.Select(d => d.FilePath), StringComparer.OrdinalIgnoreCase);
            var items    = await FolderScanner.ScanFolderAsync(folder, cts.Token);
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

        if (_showingSpaces)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            EmptySpacesState.Visibility = Workspaces.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            WorkspacesView.Visibility = Workspaces.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            NoResultsState.Visibility = Visibility.Collapsed;
            DocumentsGridView.Visibility = Visibility.Collapsed;
            GroupedDocumentsView.Visibility = Visibility.Collapsed;
            ClearButton.Visibility = Visibility.Collapsed;
            HomeLayout.ColumnDefinitions[1].Width = new GridLength(0);
            FilterPanel.Visibility = Visibility.Collapsed;
            DocumentCountLabel.Text = Workspaces.Count == 1
                ? "1 workspace"
                : $"{Workspaces.Count} workspaces";
            return;
        }

        EmptySpacesState.Visibility = Visibility.Collapsed;
        WorkspacesView.Visibility = Visibility.Collapsed;
        HomeLayout.ColumnDefinitions[1].Width = new GridLength(300);
        FilterPanel.Visibility = Visibility.Visible;
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
