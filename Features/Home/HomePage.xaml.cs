// HomePage.xaml.cs — Unified Recent / All Documents view with segmented toggle.

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
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
using DocGlance.UI.Controls;
using Windows.Foundation;

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
    private Task _databaseInitializationTask = Task.CompletedTask;
    private static readonly SemaphoreSlim ScanScheduler = new(3, 3);
    private DispatcherQueueTimer _scanRefreshTimer = null!;
    private bool _scanRefreshScheduled;
    private const double FilterPanelBreakpoint = 1120;
    private const double CompactSelectionBreakpoint = 1280;
    private bool _responsiveLayoutInitialized;
    private bool _filterPanelShouldShow;
    private Storyboard? _filterPanelStoryboard;
    private readonly Dictionary<string, FileSystemWatcher> _folderWatchers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _watchDebounce =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Timer> _folderReconciliationTimers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PendingFolderChanges> _pendingFolderChanges =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _activeFolderRoots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _rootSynchronization =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DocumentItem> _selectedDocuments =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _watcherGate = new();
    private int _activeScanCount;
    private uint _selectionPointerId;
    private Point _selectionStart;
    private bool _selectionTracking;
    private bool _selectionGestureStarted;
    private bool _selectionAdditive;
    private DocumentCardV2? _selectionStartCard;
    private DocumentCardV2? _suppressedCardAfterMarquee;


    // ═════════════════════════════════════════════════════════════════════════
    // CONSTRUCTOR + LOADED
    // ═════════════════════════════════════════════════════════════════════════

    public HomePage()
    {
        InitializeComponent();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        HomeLayout.SizeChanged += HomeLayout_SizeChanged;
        _scanRefreshTimer = _dispatcherQueue.CreateTimer();
        _scanRefreshTimer.IsRepeating = false;
        _scanRefreshTimer.Interval = TimeSpan.FromMilliseconds(150);
        _scanRefreshTimer.Tick += (_, _) =>
        {
            _scanRefreshScheduled = false;
            if (!ViewModel.ShowingRecent && !_showingSpaces)
                RefreshDisplay();
        };
        DocumentsContent.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(SelectionSurface_PointerPressed),
            handledEventsToo: true);
        DocumentsContent.AddHandler(
            UIElement.PointerMovedEvent,
            new PointerEventHandler(SelectionSurface_PointerMoved),
            handledEventsToo: true);
        DocumentsContent.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(SelectionSurface_PointerReleased),
            handledEventsToo: true);
        DocumentsContent.AddHandler(
            UIElement.PointerCanceledEvent,
            new PointerEventHandler(SelectionSurface_PointerCanceled),
            handledEventsToo: true);
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        RecentModeButton.IsChecked = true;
        SetSortDirectionUI(ascending: true);
        ClearButton.Visibility = Visibility.Visible;

        LoadRecentDocuments();
        LoadWorkspaces();
        RefreshDisplay();

        // Initialize database in background - don't block UI
        _databaseInitializationTask = Task.Run(FileIndexService.InitializeDatabaseAsync);
        _ = _databaseInitializationTask.ContinueWith(
            task => Logger.Error("Database initialization failed.", task.Exception!.GetBaseException()),
            TaskContinuationOptions.OnlyOnFaulted);
    }


    // ═════════════════════════════════════════════════════════════════════════
    // DATABASE CACHE
    // ═════════════════════════════════════════════════════════════════════════

    private async Task LoadCachedIndexAsync()
    {
        try
        {
            await _databaseInitializationTask;
            var folders = await Task.Run(FileIndexService.GetScannedFoldersAsync);
            var cachedFiles = await Task.Run(FileIndexService.LoadIndexedFilesAsync);

            foreach (var folder in folders)
            {
                if (!ViewModel.ScannedFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                    ViewModel.ScannedFolders.Add(folder);
                try
                {
                    await StorageAccessStore.GetFolderAsync(folder);
                    StartFolderWatcher(folder);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[SCAN] Cached folder access is unavailable for '{folder}': {ex.Message}");
                }
            }

            var folderSet = new System.Collections.Generic.HashSet<string>(
                folders.Select(f => HomeViewModel.NormalizePath(f)),
                StringComparer.OrdinalIgnoreCase);

            List<DocumentItem> validCachedFiles = await Task.Run(() =>
                cachedFiles.Where(file =>
                    File.Exists(file.FilePath) &&
                    folderSet.Any(folderPath =>
                        HomeViewModel.IsPathWithinRoot(file.FilePath, folderPath)))
                    .ToList());
            ViewModel.UpsertIndexedDocuments(validCachedFiles);

            RefreshDisplay();
            foreach (string folder in folders)
                _ = ScanFolderAsync(folder);
        }
        catch (Exception ex) { Logger.Log($"[DB ERROR] LoadCachedIndex: {ex.Message}"); }
    }


    // ═════════════════════════════════════════════════════════════════════════
    // MODE TOGGLE — Recent ↔ All Documents
    // ═════════════════════════════════════════════════════════════════════════

    private async void ViewModeToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        ClearDocumentSelection();
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
        // Fan out all WinRT StorageFile and GetBasicProperties calls in parallel
        // (bounded to 8 concurrent calls so we don't hammer the Future Access List).
        // Previously these ran sequentially: on 20+ recent files the UI would stall
        // for several hundred milliseconds on each app launch.
        var paths = RecentFilesStore.Load().ToList();
        var semaphore = new SemaphoreSlim(8, 8);
        var tasks = paths.Select(async path =>
        {
            await semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                Windows.Storage.StorageFile file = await StorageAccessStore.GetFileAsync(path).ConfigureAwait(false);
                var properties = await file.GetBasicPropertiesAsync().AsTask().ConfigureAwait(false);
                long fileSize = checked((long)properties.Size);
                return (DocumentItem?)new DocumentItem
                {
                    FilePath      = file.Path,
                    Title         = file.Name,
                    FolderPath    = Path.GetDirectoryName(file.Path) ?? string.Empty,
                    FileType      = file.FileType.TrimStart('.').ToUpperInvariant(),
                    FileSizeBytes = fileSize,
                    FileSize      = FileSizeFormatter.Format(fileSize),
                    LastWriteTime = properties.DateModified.DateTime,
                    LastModified  = properties.DateModified.DateTime.ToString("MMM dd, yyyy"),
                };
            }
            catch (Exception ex)
            {
                Logger.Warn($"[RecentFiles] Could not restore access to '{path}': {ex.Message}");
                return (DocumentItem?)null;
            }
            finally
            {
                semaphore.Release();
            }
        });

        DocumentItem?[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

        // Filter nulls and preserve the original recency order (paths list order).
        var documents = results
            .Select((item, i) => (item, originalIndex: i))
            .Where(t => t.item != null)
            .OrderBy(t => t.originalIndex)
            .Select(t => t.item!)
            .ToList();

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
        PruneDocumentSelection();
        UpdateUI();
    }

    private void PruneDocumentSelection()
    {
        if (_selectedDocuments.Count == 0)
            return;

        IEnumerable<DocumentItem> visibleDocuments = ViewModel.ShowingRecent
            ? ViewModel.RecentDocuments
            : ViewModel.GroupByMode == "None"
                ? ViewModel.FilteredDocuments
                : ViewModel.GroupedDocuments.SelectMany(group => group.Items);
        var visiblePaths = new HashSet<string>(
            visibleDocuments.Select(document => document.FilePath),
            StringComparer.OrdinalIgnoreCase);
        foreach (string selectedPath in _selectedDocuments.Keys.ToList())
            if (!visiblePaths.Contains(selectedPath))
                _selectedDocuments.Remove(selectedPath);
        UpdateDocumentSelectionUI();
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

    private async void CreateWorkspaceFromFiles_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List,
            };
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(
                picker, UI.Shell.TitleBarWindow.Current?.Hwnd ?? IntPtr.Zero);
            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0)
                return;

            foreach (Windows.Storage.StorageFile file in files)
                StorageAccessStore.RememberFile(file);

            await CreateWorkspaceAsync(files.Select(file => file.Path));
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to create a workspace from selected files", ex);
            await ShowWorkspaceMessageAsync(ex.Message);
        }
    }

    private async void CreateWorkspaceFromSelection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await CreateWorkspaceAsync(_selectedDocuments.Keys.ToList());
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to create a workspace from selected documents", ex);
            await ShowWorkspaceMessageAsync(ex.Message);
        }
    }

    private async Task CreateWorkspaceAsync(IEnumerable<string> filePaths)
    {
        string? name = await PromptWorkspaceNameAsync("Create workspace from files");
        if (name == null)
            return;

        List<string> paths = filePaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Any(path => !StorageAccessStore.HasRememberedFile(path)))
            throw new InvalidOperationException(
                "Access to one or more selected files is unavailable. Reopen them and try again.");

        DocGlance.Services.Storage.WorkspaceStore.Save(name, paths, selectedIndex: 0);
        LoadWorkspaces();
        ClearDocumentSelection();
        SpacesModeButton.IsChecked = true;
    }

    private async Task<string?> PromptWorkspaceNameAsync(string title)
    {
        var nameBox = new TextBox
        {
            PlaceholderText = "Workspace name",
            MaxLength = 80,
        };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = nameBox,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? nameBox.Text
            : null;
    }

    private async void OpenSelectedDocuments_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            List<DocumentItem> documents = _selectedDocuments.Values.ToList();
            if (documents.Count == 0)
                return;

            List<DocumentItem> missing = documents
                .Where(document => !File.Exists(document.FilePath))
                .ToList();
            if (missing.Count > 0)
                throw new FileNotFoundException(
                    $"{missing.Count} selected file(s) are no longer available. Rescan the folder and try again.");

            var window = UI.Shell.TitleBarWindow.Current
                ?? throw new InvalidOperationException("The active DocGlance window is unavailable.");
            window.OpenWorkspaceDocuments(documents, documents.Count - 1);
            ClearDocumentSelection();
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to open selected documents", ex);
            await ShowWorkspaceMessageAsync(ex.Message);
        }
    }

    private async void AddSelectedToWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (_allWorkspaces.Count == 0)
        {
            await ShowWorkspaceMessageAsync("Create a workspace before adding documents to it.");
            return;
        }

        var flyout = new MenuFlyout();
        foreach (DocGlance.Services.Storage.SavedWorkspace workspace in _allWorkspaces)
        {
            var item = new MenuFlyoutItem
            {
                Text = workspace.Name,
                Tag = workspace.Id,
            };
            item.Click += AddSelectedToWorkspaceMenuItem_Click;
            flyout.Items.Add(item);
        }
        flyout.ShowAt(AddToWorkspaceButton);
    }

    private async void AddSelectedToWorkspaceMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string workspaceId })
            return;

        try
        {
            DocGlance.Services.Storage.WorkspaceStore.AddDocuments(
                workspaceId, _selectedDocuments.Keys.ToList());
            LoadWorkspaces();
            ClearDocumentSelection();
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to add selected documents to a workspace", ex);
            await ShowWorkspaceMessageAsync(ex.Message);
        }
    }

    private void ClearDocumentSelection_Click(object sender, RoutedEventArgs e)
        => ClearDocumentSelection();

    private void ClearDocumentSelection()
    {
        _selectedDocuments.Clear();
        UpdateDocumentSelectionUI();
    }

    private void UpdateDocumentSelectionUI()
    {
        UpdateVisibleCardSelection(DocumentsContent);
        bool hasSelection = _selectedDocuments.Count > 0;
        SelectionActionBar.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        SelectionCountText.Text = hasSelection
            ? $"{_selectedDocuments.Count} selected"
            : string.Empty;
        DocumentCountLabel.Visibility = hasSelection
            ? Visibility.Collapsed
            : Visibility.Visible;
        ClearButton.Visibility = !hasSelection && ViewModel.ShowingRecent
            ? Visibility.Visible
            : Visibility.Collapsed;
        AddToWorkspaceButton.IsEnabled = _allWorkspaces.Count > 0;
        UpdateSelectionActionLayout();
    }

    private void HomeLayout_SizeChanged(object sender, SizeChangedEventArgs e)
        => ApplyResponsiveLayout();

    private void ApplyResponsiveLayout()
    {
        bool shouldShowFilter = !_showingSpaces &&
            !ViewModel.ShowingRecent &&
            HomeLayout.ActualWidth >= FilterPanelBreakpoint;
        bool animate = _responsiveLayoutInitialized &&
            shouldShowFilter != _filterPanelShouldShow;

        if (!_responsiveLayoutInitialized || shouldShowFilter != _filterPanelShouldShow)
        {
            _filterPanelShouldShow = shouldShowFilter;
            if (shouldShowFilter)
            {
                HomeLayout.ColumnDefinitions[1].Width = new GridLength(300);
                FilterPanel.Visibility = Visibility.Visible;
                if (animate)
                    AnimateFilterPanel(show: true);
                else
                {
                    FilterPanel.Opacity = 1;
                    ((TranslateTransform)FilterPanel.RenderTransform).X = 0;
                }
            }
            else if (animate && FilterPanel.Visibility == Visibility.Visible)
            {
                AnimateFilterPanel(show: false);
            }
            else
            {
                HomeLayout.ColumnDefinitions[1].Width = new GridLength(0);
                FilterPanel.Visibility = Visibility.Collapsed;
                FilterPanel.Opacity = 1;
                ((TranslateTransform)FilterPanel.RenderTransform).X = 0;
            }
        }

        _responsiveLayoutInitialized = true;
        UpdateSelectionActionLayout();
    }

    private void AnimateFilterPanel(bool show)
    {
        _filterPanelStoryboard?.Stop();
        var translation = (TranslateTransform)FilterPanel.RenderTransform;
        if (show)
        {
            FilterPanel.Visibility = Visibility.Visible;
            FilterPanel.Opacity = 0;
            translation.X = 18;
        }

        var storyboard = new Storyboard();
        var opacityAnimation = new DoubleAnimation
        {
            To = show ? 1 : 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(opacityAnimation, FilterPanel);
        Storyboard.SetTargetProperty(opacityAnimation, "Opacity");
        storyboard.Children.Add(opacityAnimation);

        var slideAnimation = new DoubleAnimation
        {
            To = show ? 0 : 18,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(slideAnimation, translation);
        Storyboard.SetTargetProperty(slideAnimation, "X");
        storyboard.Children.Add(slideAnimation);

        if (!show)
        {
            storyboard.Completed += (_, _) =>
            {
                if (_filterPanelShouldShow)
                    return;

                FilterPanel.Visibility = Visibility.Collapsed;
                FilterPanel.Opacity = 1;
                translation.X = 0;
                HomeLayout.ColumnDefinitions[1].Width = new GridLength(0);
            };
        }

        _filterPanelStoryboard = storyboard;
        storyboard.Begin();
    }

    private void UpdateSelectionActionLayout()
    {
        bool compact = HomeLayout.ActualWidth < CompactSelectionBreakpoint;
        WideSelectionActions.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactSelectionActionsButton.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        AddToWorkspaceButton.IsEnabled = _allWorkspaces.Count > 0;

        CompactAddToWorkspaceMenu.Items.Clear();
        foreach (DocGlance.Services.Storage.SavedWorkspace workspace in _allWorkspaces)
        {
            var item = new MenuFlyoutItem
            {
                Text = workspace.Name,
                Tag = workspace.Id,
            };
            item.Click += AddSelectedToWorkspaceMenuItem_Click;
            CompactAddToWorkspaceMenu.Items.Add(item);
        }
        CompactAddToWorkspaceMenu.IsEnabled = _allWorkspaces.Count > 0;
    }

    private void SelectionSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _suppressedCardAfterMarquee?.ResetClickSuppression();
        _suppressedCardAfterMarquee = null;
        if (!e.GetCurrentPoint(DocumentsContent).Properties.IsLeftButtonPressed)
            return;

        DependencyObject? source = e.OriginalSource as DependencyObject;
        if (source == null ||
            (!IsVisualDescendantOf(source, DocumentsGridView) &&
             !IsVisualDescendantOf(source, GroupedDocumentsView)))
            return;

        if (FindVisualAncestor<ScrollBar>(source) != null)
            return;

        DocumentCardV2? card = FindVisualAncestor<DocumentCardV2>(source);
        if (card?.IsContextMenuElement(source) == true ||
            card?.IsSelectionToggleElement(source) == true)
            return;

        _selectionTracking = true;
        _selectionPointerId = e.Pointer.PointerId;
        _selectionStart = e.GetCurrentPoint(DocumentsContent).Position;
        _selectionGestureStarted = false;
        _selectionAdditive =
            (e.KeyModifiers & Windows.System.VirtualKeyModifiers.Control) != 0;
        _selectionStartCard = card;
    }

    private void SelectionSurface_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_selectionTracking || e.Pointer.PointerId != _selectionPointerId ||
            !e.GetCurrentPoint(DocumentsContent).Properties.IsLeftButtonPressed)
            return;

        Point current = e.GetCurrentPoint(DocumentsContent).Position;
        if (!_selectionGestureStarted)
        {
            if (Math.Abs(current.X - _selectionStart.X) < 6 &&
                Math.Abs(current.Y - _selectionStart.Y) < 6)
                return;

            if (!DocumentsContent.CapturePointer(e.Pointer))
                return;

            _selectionGestureStarted = true;
            _selectionStartCard?.SuppressNextOpen();
            _suppressedCardAfterMarquee = _selectionStartCard;
            if (!_selectionAdditive)
                _selectedDocuments.Clear();
            SelectionCanvas.Visibility = Visibility.Visible;
        }

        UpdateSelectionRectangle(current);
        e.Handled = true;
    }

    private void SelectionSurface_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_selectionTracking || e.Pointer.PointerId != _selectionPointerId)
            return;

        if (_selectionGestureStarted)
        {
            Point current = e.GetCurrentPoint(DocumentsContent).Position;
            UpdateSelectionRectangle(current);
            SelectCardsInMarquee(current);
            SelectionCanvas.Visibility = Visibility.Collapsed;
            DocumentsContent.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }

        _selectionTracking = false;
        _selectionGestureStarted = false;
        _selectionPointerId = 0;
        _selectionStartCard = null;
    }

    private void SelectionSurface_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (!_selectionTracking || e.Pointer.PointerId != _selectionPointerId)
            return;

        SelectionCanvas.Visibility = Visibility.Collapsed;
        _selectionTracking = false;
        _selectionGestureStarted = false;
        _selectionPointerId = 0;
        _selectionStartCard = null;
    }

    private void UpdateSelectionRectangle(Point current)
    {
        double left = Math.Min(current.X, _selectionStart.X);
        double top = Math.Min(current.Y, _selectionStart.Y);
        SelectionRectangle.Width = Math.Abs(current.X - _selectionStart.X);
        SelectionRectangle.Height = Math.Abs(current.Y - _selectionStart.Y);
        Canvas.SetLeft(SelectionRectangle, left);
        Canvas.SetTop(SelectionRectangle, top);
    }

    private void SelectCardsInMarquee(Point current)
    {
        var marquee = new Rect(
            Math.Min(current.X, _selectionStart.X),
            Math.Min(current.Y, _selectionStart.Y),
            Math.Abs(current.X - _selectionStart.X),
            Math.Abs(current.Y - _selectionStart.Y));

        foreach (DocumentCardV2 card in GetVisibleDocumentCards())
        {
            if (card.Document == null || card.ActualWidth <= 0 || card.ActualHeight <= 0)
                continue;

            try
            {
                Rect cardBounds = card.TransformToVisual(DocumentsContent)
                    .TransformBounds(new Rect(0, 0, card.ActualWidth, card.ActualHeight));
                if (marquee.Left <= cardBounds.Right && marquee.Right >= cardBounds.Left &&
                    marquee.Top <= cardBounds.Bottom && marquee.Bottom >= cardBounds.Top)
                    _selectedDocuments[card.Document.FilePath] = card.Document;
            }
            catch (InvalidOperationException ex)
            {
                Logger.Warn($"Could not measure a document card for selection: {ex.Message}");
            }
        }

        UpdateDocumentSelectionUI();
    }

    private IEnumerable<DocumentCardV2> GetVisibleDocumentCards()
    {
        UIElement activeView = GroupedDocumentsView.Visibility == Visibility.Visible
            ? GroupedDocumentsView
            : DocumentsGridView;
        var cards = new List<DocumentCardV2>();
        CollectDocumentCards(activeView, cards);
        return cards;
    }

    private static void CollectDocumentCards(DependencyObject parent, List<DocumentCardV2> cards)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is DocumentCardV2 card)
            {
                cards.Add(card);
                continue;
            }
            CollectDocumentCards(child, cards);
        }
    }

    private void UpdateVisibleCardSelection(DependencyObject parent)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is DocumentCardV2 card)
            {
                card.IsSelected = card.Document != null &&
                    _selectedDocuments.ContainsKey(card.Document.FilePath);
                continue;
            }
            UpdateVisibleCardSelection(child);
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject source)
        where T : DependencyObject
    {
        DependencyObject? current = source;
        while (current != null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static bool IsVisualDescendantOf(DependencyObject source, DependencyObject ancestor)
    {
        DependencyObject? current = source;
        while (current != null)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
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

            if (ScannedFolders.Any(existing =>
                    HomeViewModel.IsPathWithinRoot(folder.Path, existing) ||
                    HomeViewModel.IsPathWithinRoot(existing, folder.Path)))
            {
                Logger.Warn($"[SCAN] Ignored overlapping folder root: {folder.Path}");
                return;
            }

            StorageAccessStore.RememberFolder(folder);
            if (!ScannedFolders.Contains(folder.Path, StringComparer.OrdinalIgnoreCase))
                ScannedFolders.Add(folder.Path);
            StartFolderWatcher(folder.Path);
            await ScanFolderAsync(folder);
        }
        catch (Exception ex) { Logger.Error("AddFolder_Click", ex); }
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string path) return;

        StorageAccessStore.ForgetFolder(path);
        StopFolderWatcher(path);
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
        await Task.WhenAll(ViewModel.ScannedFolders.ToList().Select(ScanFolderAsync));
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
        Interlocked.Increment(ref _activeScanCount);
        SetScanningState(true);
        bool schedulerAcquired = false;
        bool rootAcquired = false;
        SemaphoreSlim rootGate = _rootSynchronization.GetOrAdd(
            folderPath, _ => new SemaphoreSlim(1, 1));
        try
        {
            await ScanScheduler.WaitAsync(cts.Token);
            schedulerAcquired = true;
            await rootGate.WaitAsync(cts.Token);
            rootAcquired = true;
            CancellationToken scanToken = cts.Token;
            Action<IReadOnlyList<DocumentItem>> publishBatch = batch =>
            {
                if (!_dispatcherQueue.TryEnqueue(() =>
                    {
                        if (scanToken.IsCancellationRequested ||
                            !ScannedFolders.Contains(folderPath, StringComparer.OrdinalIgnoreCase))
                            return;

                        ViewModel.UpsertIndexedDocuments(batch);
                        if (!ViewModel.ShowingRecent && !_showingSpaces)
                            ScheduleScanRefresh();
                    }))
                    Logger.Warn($"[SCAN] Could not publish discovered files for '{folderPath}'.");
            };
            FolderScanResult result = await FolderScanner.ScanFolderAsync(
                folder, publishBatch, scanToken);
            cts.Token.ThrowIfCancellationRequested();
            await Task.Run(
                () => FileIndexService.CommitScanAsync(
                    folderPath, result.Items, result.IsComplete, cts.Token),
                cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (!ScannedFolders.Contains(folderPath, StringComparer.OrdinalIgnoreCase))
                return;
            ViewModel.ReconcileIndexedFolder(folderPath, result.Items, result.IsComplete);
            _scanRefreshTimer.Stop();
            _scanRefreshScheduled = false;
            RefreshDisplay();
            _ = LoadThumbnailsAsync(result.Items, cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Logger.Log($"[SCAN] {folderPath}: {ex.Message}"); }
        finally
        {
            if (rootAcquired)
                rootGate.Release();
            if (schedulerAcquired)
                ScanScheduler.Release();
            SetScanningState(Interlocked.Decrement(ref _activeScanCount) > 0);
            ViewModel.RemoveScanToken(folderPath, cts);
        }
    }

    private void ScheduleScanRefresh()
    {
        if (_scanRefreshScheduled)
            return;

        _scanRefreshScheduled = true;
        _scanRefreshTimer.Start();
    }


    // ═════════════════════════════════════════════════════════════════════════
    // LIVE FOLDER MONITORING
    // ═════════════════════════════════════════════════════════════════════════

    private void StartFolderWatcher(string folderPath)
    {
        _activeFolderRoots.TryAdd(folderPath, 0);
        lock (_watcherGate)
        {
            if (_folderWatchers.ContainsKey(folderPath))
                return;

            try
            {
                var watcher = new FileSystemWatcher(folderPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                                   NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024,
                };
                watcher.Created += (_, e) =>
                    QueueFolderChange(folderPath, e.FullPath, Directory.Exists(e.FullPath));
                watcher.Changed += (_, e) =>
                    QueueFolderChange(folderPath, e.FullPath, Directory.Exists(e.FullPath));
                watcher.Deleted += (_, e) =>
                    QueueFolderChange(folderPath, e.FullPath,
                        !FolderScanner.IsSupportedFilePath(e.FullPath) ||
                        ViewModel.BackgroundIndex.Any(document =>
                            HomeViewModel.IsPathWithinRoot(document.FilePath, e.FullPath)));
                watcher.Renamed += (_, e) =>
                {
                    bool isDirectory = Directory.Exists(e.FullPath);
                    QueueFolderChange(folderPath, e.FullPath, isDirectory, e.OldFullPath);
                };
                watcher.Error += (_, e) =>
                {
                    Logger.Warn($"[WATCH] Rescanning '{folderPath}' after watcher error: {e.GetException().Message}");
                    QueueFolderChange(folderPath, folderPath, requiresRescan: true);
                };
                watcher.EnableRaisingEvents = true;
                _folderWatchers.Add(folderPath, watcher);
                _folderReconciliationTimers.Add(folderPath, new Timer(
                    _ => QueueFolderRescan(folderPath),
                    null,
                    TimeSpan.FromMinutes(30),
                    TimeSpan.FromMinutes(30)));
                Logger.Log($"[WATCH] Monitoring: {folderPath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"[WATCH] Could not monitor '{folderPath}'", ex);
            }
        }
    }

    private void StopFolderWatcher(string folderPath)
    {
        _activeFolderRoots.TryRemove(folderPath, out _);
        lock (_watcherGate)
        {
            if (_folderWatchers.Remove(folderPath, out FileSystemWatcher? watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            if (_watchDebounce.Remove(folderPath, out CancellationTokenSource? debounce))
            {
                debounce.Cancel();
                debounce.Dispose();
            }
            if (_folderReconciliationTimers.Remove(folderPath, out Timer? timer))
                timer.Dispose();
            _pendingFolderChanges.TryRemove(folderPath, out _);
        }
    }

    private void QueueFolderChange(
        string folderPath, string path, bool requiresRescan, string? oldPath = null)
    {
        PendingFolderChanges pending = _pendingFolderChanges.GetOrAdd(
            folderPath, _ => new PendingFolderChanges());
        lock (pending)
        {
            pending.Paths.Add(path);
            if (oldPath != null)
                pending.Paths.Add(oldPath);
            pending.RequiresRescan |= requiresRescan;
        }

        var debounce = new CancellationTokenSource();
        lock (_watcherGate)
        {
            if (_watchDebounce.TryGetValue(folderPath, out CancellationTokenSource? previous))
            {
                previous.Cancel();
                previous.Dispose();
            }
            _watchDebounce[folderPath] = debounce;
        }
        _ = FlushFolderChangesAsync(folderPath, debounce);
    }

    private async Task FlushFolderChangesAsync(
        string folderPath, CancellationTokenSource debounce)
    {
        try
        {
            await Task.Delay(350, debounce.Token);
            lock (_watcherGate)
            {
                if (!_watchDebounce.TryGetValue(folderPath, out CancellationTokenSource? current) ||
                    !ReferenceEquals(current, debounce))
                    return;
                _watchDebounce.Remove(folderPath);
            }

            if (!_pendingFolderChanges.TryRemove(folderPath, out PendingFolderChanges? pending))
                return;
            string[] paths;
            bool requiresRescan;
            lock (pending)
            {
                paths = pending.Paths.ToArray();
                requiresRescan = pending.RequiresRescan;
            }

            if (requiresRescan)
            {
                QueueFolderRescan(folderPath);
                return;
            }
            await ApplyFolderChangesAsync(folderPath, paths);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Logger.Error($"[WATCH] Could not apply changes for '{folderPath}'", ex); }
        finally
        {
            debounce.Dispose();
        }
    }

    private void QueueFolderRescan(string folderPath)
    {
        if (!_activeFolderRoots.ContainsKey(folderPath))
            return;
        if (!_dispatcherQueue.TryEnqueue(() =>
            {
                if (_activeFolderRoots.ContainsKey(folderPath))
                    _ = ScanFolderAsync(folderPath);
            }))
            Logger.Warn($"[WATCH] Could not queue reconciliation for '{folderPath}'.");
    }

    private async Task ApplyFolderChangesAsync(string folderPath, IEnumerable<string> paths)
    {
        SemaphoreSlim rootGate = _rootSynchronization.GetOrAdd(
            folderPath, _ => new SemaphoreSlim(1, 1));
        await rootGate.WaitAsync();
        try
        {
            await ApplyFolderChangesCoreAsync(folderPath, paths);
        }
        finally { rootGate.Release(); }
    }

    private async Task ApplyFolderChangesCoreAsync(string folderPath, IEnumerable<string> paths)
    {
        if (!_activeFolderRoots.ContainsKey(folderPath))
            return;

        var changedDocuments = new List<DocumentItem>();
        var removedPaths = new List<string>();
        bool requiresRescan = false;
        foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!_activeFolderRoots.ContainsKey(folderPath))
                return;
            if (!FolderScanner.IsSupportedFilePath(path))
                continue;

            try
            {
                DocumentItem? document = File.Exists(path)
                    ? await FolderScanner.ReadDocumentAsync(path, folderPath)
                    : null;
                if (document == null)
                    removedPaths.Add(path);
                else
                    changedDocuments.Add(document);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WATCH] Could not update '{path}': {ex.Message}");
                requiresRescan = true;
            }
        }

        if (changedDocuments.Count > 0 || removedPaths.Count > 0)
        {
            if (!_activeFolderRoots.ContainsKey(folderPath))
                return;
            try
            {
                await FileIndexService.ApplyFileChangesAsync(changedDocuments, removedPaths);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WATCH] Could not persist changes for '{folderPath}': {ex.Message}");
                requiresRescan = true;
            }
        }

        if (changedDocuments.Count > 0 || removedPaths.Count > 0)
        {
            if (!_dispatcherQueue.TryEnqueue(() =>
                {
                    if (!_activeFolderRoots.ContainsKey(folderPath))
                        return;
                    foreach (string path in removedPaths)
                        ViewModel.RemoveIndexedDocument(path);
                    foreach (DocumentItem document in changedDocuments)
                        ViewModel.UpsertIndexedDocument(document);
                    RefreshDisplay();
                }))
                Logger.Warn($"[WATCH] Could not update the UI for changes in '{folderPath}'.");
        }

        if (changedDocuments.Count > 0)
            _ = LoadThumbnailsAsync(changedDocuments, CancellationToken.None);
        if (requiresRescan)
            QueueFolderRescan(folderPath);
    }

    private sealed class PendingFolderChanges
    {
        public HashSet<string> Paths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool RequiresRescan { get; set; }
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
    {
        if (args.Element is DocumentCardV2 card)
        {
            card.SelectionToggleRequested -= DocumentCard_SelectionToggleRequested;
            card.SelectionToggleRequested += DocumentCard_SelectionToggleRequested;
            card.IsSelected = card.Document != null &&
                _selectedDocuments.ContainsKey(card.Document.FilePath);
        }
    }

    private void DocumentCard_SelectionToggleRequested(DocumentCardV2 card)
    {
        if (card.Document == null)
            return;

        string path = card.Document.FilePath;
        if (!_selectedDocuments.Remove(path))
            _selectedDocuments[path] = card.Document;
        UpdateDocumentSelectionUI();
    }


    // ═════════════════════════════════════════════════════════════════════════
    // UI HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    private void UpdateUI()
    {
        if (!_loaded) return;
        ApplyResponsiveLayout();

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
            CreateWorkspaceFromFilesButton.Visibility = Visibility.Visible;
            SelectionActionBar.Visibility = Visibility.Collapsed;
            DocumentCountLabel.Visibility = Visibility.Visible;
            DocumentCountLabel.Text = Workspaces.Count == 1
                ? "1 workspace"
                : $"{Workspaces.Count} workspaces";
            return;
        }

        EmptySpacesState.Visibility = Visibility.Collapsed;
        WorkspacesView.Visibility = Visibility.Collapsed;
        CreateWorkspaceFromFilesButton.Visibility = Visibility.Collapsed;
        bool hasSelection = _selectedDocuments.Count > 0;
        ClearButton.Visibility = ViewModel.ShowingRecent && !hasSelection
            ? Visibility.Visible
            : Visibility.Collapsed;
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
        {
            DocumentCountLabel.Text = ViewModel.DocumentCountText;
            DocumentCountLabel.Visibility = hasSelection
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

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
