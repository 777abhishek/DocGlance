// TitleBarWindow.xaml.cs
// Chrome-style TabView: Home tab (permanent) + closeable document tabs.
// Each document tab owns its own ViewerPage instance so zoom/page state
// is preserved when switching tabs — exactly like VS Code / Rune.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WinRT;
using WinRT.Interop;
using DocGlance.Core.Models;
using DocGlance.Core.Constants;
using DocGlance.Features.Home;
using DocGlance.Features.Viewer;
using DocGlance.Infrastructure.Configuration;
using DocGlance.Infrastructure.Logging;
using DocGlance.Services.Input;
using DocGlance.Services.Session;

namespace DocGlance.UI.Shell;

public sealed partial class TitleBarWindow : Window
{
    // ── Singleton so pages can reach the shell ────────────────────────────────
    public static new TitleBarWindow? Current { get; private set; }

    // ── Per-tab state: tab item → (ViewerPage, DocumentItem) ─────────────────
    private readonly Dictionary<TabViewItem, (ViewerPage viewer, DocumentItem doc)> _tabData = new();
    
    // Special tabs (Settings, etc.) that don't show documents
    private readonly Dictionary<TabViewItem, UIElement> _specialTabs = new();

    // The permanent "home" tab
    private TabViewItem? _homeTab;
    private int _selectionChangeVersion;
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _acrylicConfiguration;
    private ICompositionSupportsSystemBackdrop? _acrylicTarget;
    private bool _homePageInitialized;
    private bool _homePageInitializationQueued;
    private bool _isPresentationMode;
    private bool _windowCloseFlushInProgress;
    private bool _allowWindowClose;
    private Visibility _tabsVisibilityBeforePresentation;
    private Visibility _updateBannerVisibilityBeforePresentation;

    private readonly QuickLookHotkeyService _quickLookHotkeyService;

    // ── HWND (for pickers in unpackaged apps) ─────────────────────────────────
    public IntPtr Hwnd => WindowNative.GetWindowHandle(this);

    public void RestoreAndActivate()
    {
        // Window.Activate() alone does not reliably restore a minimized WinUI window.
        ShowWindow(Hwnd, SW_RESTORE);
        Activate();

        // The launching process grants this instance foreground permission during
        // single-instance handover. Make the foreground request explicitly rather
        // than relying only on WinUI's Activate() request.
        bool foregrounded = SetForegroundWindow(Hwnd);
        Logger.Log($"[WindowActivation] SetForegroundWindow={foregrounded}");
    }

    public bool SetPresentationMode(bool enabled)
    {
        if (_isPresentationMode == enabled)
            return true;

        try
        {
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(
                Microsoft.UI.Win32Interop.GetWindowIdFromWindow(Hwnd));
            appWindow.SetPresenter(enabled
                ? Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen
                : Microsoft.UI.Windowing.AppWindowPresenterKind.Default);

            if (enabled)
            {
                _tabsVisibilityBeforePresentation = Tabs.Visibility;
                _updateBannerVisibilityBeforePresentation = UpdateBannerControl.Visibility;
                Tabs.Visibility = Visibility.Collapsed;
                UpdateBannerControl.Visibility = Visibility.Collapsed;
            }
            else
            {
                Tabs.Visibility = _tabsVisibilityBeforePresentation;
                UpdateBannerControl.Visibility = _updateBannerVisibilityBeforePresentation;
            }

            _isPresentationMode = enabled;
            Logger.Log($"[Presentation] Window mode changed: enabled={enabled}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"[Presentation] Failed to change window mode: enabled={enabled}", ex);
            return false;
        }
    }


    public bool IsQuickLookHotkeyRegistered => _quickLookHotkeyService.IsRegistered;

    public bool TrySetQuickLookShortcutEnabled(bool enabled, out string error)
        => _quickLookHotkeyService.TrySetShortcutEnabled(enabled, out error);

    public bool TrySetQuickLookHotkey(int virtualKey, uint modifiers, out string error)
        => _quickLookHotkeyService.TrySetHotkey(virtualKey, modifiers, out error);

    public void SuspendQuickLookHotkey() => _quickLookHotkeyService.Suspend();

    public bool ResumeQuickLookHotkey(out string error)
        => _quickLookHotkeyService.Resume(out error);

    internal void BecomeQuickLookHotkeyOwner() => _quickLookHotkeyService.BecomeOwner();

    private void ReleaseQuickLookHotkeyOwner() => _quickLookHotkeyService.ReleaseOwner();

    internal void HandleGlobalFileOpenHotkey() => _quickLookHotkeyService.HandleGlobalFileOpenHotkey();


    public TitleBarWindow()
    {
        Current = this;
        InitializeComponent();
        _quickLookHotkeyService = new QuickLookHotkeyService(this);
        if (!QuickLookHotkeyService.HasOwner)
            BecomeQuickLookHotkeyOwner();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragArea);
        Title = "DocGlance";
        AppWindow.Closing += AppWindow_Closing;

        // Track active window
        Activated += async (s, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
            {
                Current = this;
                return;
            }

            foreach (var (viewer, _) in _tabData.Values.ToArray())
                await viewer.FlushPendingPdfAnnotationsAsync();
        };
        
        // Save session and release the custom backdrop controller when the window closes.
        Closed += (s, e) =>
        {
            ReleaseQuickLookHotkeyOwner();
            SaveSession();
            RemoveAcrylicBackdrop();
        };

        // Set the window icon
        SetWindowIcon();
        
        // Configure title bar button colors for theme support
        ConfigureTitleBarTheme();

        // Drag & drop files onto window
        RootGrid.AllowDrop = true;

        // Add the permanent Home tab
        AddHomeTab();

        // Navigate to HomePage and start update checker
        RootGrid.Loaded += RootGrid_Loaded;
        UpdateBannerControl.Attach();
        
        // Listen for theme changes to update title bar
        if (Content is FrameworkElement root)
        {
            root.ActualThemeChanged += (s, e) => ConfigureTitleBarTheme();
        }

    }

    private async void AppWindow_Closing(
        object sender,
        Microsoft.UI.Windowing.AppWindowClosingEventArgs e)
    {
        if (_allowWindowClose)
            return;

        e.Cancel = true;
        if (_windowCloseFlushInProgress)
            return;

        _windowCloseFlushInProgress = true;
        try
        {
            foreach (var (viewer, _) in _tabData.Values.ToArray())
            {
                if (!await viewer.FlushPendingPdfAnnotationsAsync())
                    return;
            }

            _allowWindowClose = true;
            Close();
        }
        finally
        {
            _windowCloseFlushInProgress = false;
        }
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= RootGrid_Loaded;
        QueueHomePageInitialization();
    }

    private void QueueHomePageInitialization()
    {
        if (_homePageInitialized || _homePageInitializationQueued)
            return;

        _homePageInitializationQueued = true;
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                _homePageInitializationQueued = false;
                if (_homePageInitialized || !ReferenceEquals(Tabs.SelectedItem, _homeTab))
                    return;

                _homePageInitialized = true;
                DocGlance.Infrastructure.Diagnostics.DiagnosticsService.Milestone("HomePageInitializationStarted");
                HomeFrame.Navigate(typeof(HomePage));
                DocGlance.Infrastructure.Diagnostics.DiagnosticsService.Milestone("HomePageInitialized");
            });
    }

    // ── Home tab ──────────────────────────────────────────────────────────────

    private void AddHomeTab()
    {
        _homeTab = new TabViewItem
        {
            Header          = "Home",
            IsClosable      = false, // Home tab is permanent
            IconSource      = new SymbolIconSource { Symbol = Symbol.Home },
        };
        Tabs.TabItems.Add(_homeTab);
        Tabs.SelectedItem = _homeTab;
    }

    // ── Open a document in a new tab (or focus existing) ─────────────────────

    private static BitmapIconSource CreateDocumentIconSource(string fileType)
    {
        string iconName = fileType.TrimStart('.').ToLowerInvariant() switch
        {
            "pdf" => "PDF",
            "pptx" or "ppt" or "pptm" or "ppsx" => "PowerPoint",
            "docx" or "doc" => "Word",
            "xlsx" or "xls" => "Excel",
            "csv" or "tsv" => "CSV",
            "log" => "LOG",
            "md" or "markdown" => "Markdown",
            "rtf" => "RichText",
            "json" => "JSON",
            "xml" or "xaml" => "XML",
            "cs" or "vb" or "fs" or "js" or "mjs" or "ts" or "tsx" or "jsx" or
            "py" or "java" or "kt" or "cpp" or "c" or "h" or "go" or "rs" or
            "swift" or "rb" or "php" or "sh" or "ps1" or "bat" or "html" or
            "htm" or "css" or "scss" or "sql" => "Code",
            _ => "Text",
        };

        return new BitmapIconSource
        {
            UriSource = new Uri($"ms-appx:///Assets/FileTypeIcons/{iconName}.png"),
            ShowAsMonochrome = false,
        };
    }

    public void OpenDocument(DocumentItem doc)
    {
        Infrastructure.Logging.Logger.Log($"[TitleBarWindow] OpenDocument called: Title={doc.Title}, FilePath={doc.FilePath}, FileType={doc.FileType}");
        
        // If file already open in a tab, just focus it
        foreach (var kv in _tabData)
        {
            if (string.Equals(kv.Value.doc.FilePath, doc.FilePath,
                              StringComparison.OrdinalIgnoreCase))
            {
                Infrastructure.Logging.Logger.Log($"[TitleBarWindow] File already open, focusing tab");
                Tabs.SelectedItem = kv.Key;
                DocHost.Content = kv.Value.viewer;
                DocHost.Visibility = Visibility.Visible;
                HomeFrame.Visibility = Visibility.Collapsed;
                Title = doc.Title + " — DocGlance";
                return;
            }
        }

        Infrastructure.Logging.Logger.Log($"[TitleBarWindow] Creating new tab for document");
        
        // Persist to recent files and refresh the home page strip
        DocGlance.Services.Storage.RecentFilesStore.Push(doc.FilePath);
        if (HomeFrame.Content is DocGlance.Features.Home.HomePage home)
            home.LoadRecentDocuments();

        // Create new viewer tab
        var viewer = new ViewerPage();

        var tab = new TabViewItem
        {
            Header     = doc.Title,
            IsClosable = true,
            IconSource = CreateDocumentIconSource(doc.FileType),
        };

        _tabData[tab] = (viewer, doc);
        Tabs.TabItems.Add(tab);
        Tabs.SelectedItem = tab;

        // Immediately attach viewer to content host and update shell
        DocHost.Content = viewer;
        DocHost.Visibility = Visibility.Visible;
        HomeFrame.Visibility = Visibility.Collapsed;
        Title = doc.Title + " — DocGlance";

        Infrastructure.Logging.Logger.Log($"[TitleBarWindow] Calling viewer.LoadDocument");
        viewer.LoadDocument(doc);
        Infrastructure.Logging.Logger.Log($"[TitleBarWindow] OpenDocument complete");
        
        // Save session after opening new tab
        SaveSession();
    }

    // ── Legacy NavigateTo — kept so old call sites compile ───────────────────
    public void NavigateTo(Type pageType, object? parameter = null)
    {
        if (pageType == typeof(ViewerPage) && parameter is DocumentItem doc)
        {
            OpenDocument(doc);
            return;
        }
        
        // For Settings or other pages, open in a new tab instead of HomeFrame
        if (pageType == typeof(Features.Settings.SettingsPage))
        {
            OpenSettingsTab();
            return;
        }
        
        // Fallback: navigate in HomeFrame and switch to Home tab
        HomeFrame.Navigate(pageType, parameter);
        Tabs.SelectedItem = _homeTab;
    }
    
    public void OpenSettingsTab()
    {
        // Check if Settings tab already exists
        foreach (var item in Tabs.TabItems)
        {
            if (item is TabViewItem tab && _specialTabs.ContainsKey(tab) && tab.Header as string == "Settings")
            {
                Tabs.SelectedItem = tab;
                return;
            }
        }
        var frame = new Frame();
        frame.Navigate(typeof(Features.Settings.SettingsPage));
        
        var settingsTab = new TabViewItem
        {
            Header     = "Settings",
            IsClosable = true,
            IconSource = new SymbolIconSource { Symbol = Symbol.Setting },
        };
        
        // Store in special tabs dictionary
        _specialTabs[settingsTab] = frame;
        
        Tabs.TabItems.Add(settingsTab);
        Tabs.SelectedItem = settingsTab;
        
        DocHost.Content = frame;
        DocHost.Visibility = Visibility.Visible;
        HomeFrame.Visibility = Visibility.Collapsed;
        Title = "Settings — DocGlance";
    }

    public void OpenToolsPage()
    {
        Logger.Log($"[TitleBarWindow] OpenToolsPage requested. Existing tabs: {Tabs.TabItems.Count}");

        foreach (var item in Tabs.TabItems)
        {
            if (item is TabViewItem tab && _specialTabs.ContainsKey(tab) && tab.Header as string == "Tools")
            {
                Logger.Log("[TitleBarWindow] Selecting existing Document Tools tab.");
                Tabs.SelectedItem = tab;
                ActivateToolsTab(tab);
                return;
            }
        }

        var toolsTab = new TabViewItem
        {
            Header = "Tools",
            IsClosable = true,
            IconSource = new FontIconSource 
            { 
                Glyph = "\uE90F", 
                FontSize = 12,
                FontFamily = new FontFamily("ms-appx:///Assets/Fonts/FluentSystemIcons-Regular.ttf#FluentSystemIcons-Regular"),
            },
        };

        _specialTabs[toolsTab] = new Features.Tools.ToolsPage();
        Tabs.TabItems.Add(toolsTab);
        Tabs.SelectedItem = toolsTab;
        ActivateToolsTab(toolsTab);
        Logger.Log($"[TitleBarWindow] Created and selected Document Tools tab. Total tabs: {Tabs.TabItems.Count}");
    }

    private void ActivateToolsTab(TabViewItem tab)
    {
        if (!_specialTabs.TryGetValue(tab, out var content))
        {
            Logger.Log("[TitleBarWindow] Document Tools activation failed: tab content is missing.");
            return;
        }

        DocHost.Content = content;
        DocHost.Visibility = Visibility.Visible;
        DocHost.Opacity = 1.0;
        HomeFrame.Visibility = Visibility.Collapsed;
        HomeFrame.Opacity = 0.0;
        Title = "Document Tools — DocGlance";
        Logger.Log("[TitleBarWindow] Document Tools content activated.");
    }

    public void GoBack()
    {
        // "Back" means go to the home tab
        Tabs.SelectedItem = _homeTab;
    }

    // ── Tab events ────────────────────────────────────────────────────────────

    // UI-01 fix: cross-fade between tabs instead of instant visibility swap
    private async void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Tabs.SelectedItem is not TabViewItem selected) return;
        var selectionVersion = ++_selectionChangeVersion;

        bool isHome = ReferenceEquals(selected, _homeTab);
        if (isHome)
            QueueHomePageInitialization();

        // Update window title to document name (safely - check if window is still valid)
        try
        {
            if (!isHome && _tabData.TryGetValue(selected, out var selData))
                Title = selData.doc.Title + " — DocGlance";
            else
                Title = "DocGlance";
        }
        catch (Exception ex)
        {
            // Window may be closed during tab tear-out - ignore title update errors
            Infrastructure.Logging.Logger.Log($"[Window] Title update failed (window closed?): {ex.Message}");
        }

        // Home tab
        if (isHome)
        {
            // Fade out current content
            if (DocHost.Visibility == Visibility.Visible)
            {
                DocHost.Opacity = 0.0;
                await Task.Delay(150); // Wait for opacity transition
                if (selectionVersion != _selectionChangeVersion ||
                    !ReferenceEquals(Tabs.SelectedItem, selected))
                    return;
                DocHost.Visibility = Visibility.Collapsed;
                DocHost.Content = null;
            }
            
            // Fade in home
            HomeFrame.Visibility = Visibility.Visible;
            HomeFrame.Opacity = 1.0;
        }
        // Document tab (in _tabData)
        else if (_tabData.TryGetValue(selected, out var data))
        {
            // Fade out home if visible
            if (HomeFrame.Visibility == Visibility.Visible)
            {
                HomeFrame.Opacity = 0.0;
                await Task.Delay(150); // Wait for opacity transition
                if (selectionVersion != _selectionChangeVersion ||
                    !ReferenceEquals(Tabs.SelectedItem, selected))
                    return;
                HomeFrame.Visibility = Visibility.Collapsed;
            }
            
            // Fade in document viewer
            DocHost.Content = data.viewer;
            DocHost.Visibility = Visibility.Visible;
            DocHost.Opacity = 1.0;
        }
        // Special tabs (Settings, etc.) - stored in _specialTabs
        else if (_specialTabs.TryGetValue(selected, out var content))
        {
            // Fade out home if visible
            if (HomeFrame.Visibility == Visibility.Visible)
            {
                HomeFrame.Opacity = 0.0;
                await Task.Delay(150); // Wait for opacity transition
                HomeFrame.Visibility = Visibility.Collapsed;
            }
            
            // Fade in special content
            DocHost.Content = content;
            DocHost.Visibility = Visibility.Visible;
            DocHost.Opacity = 1.0;
        }
    }

    private async void Tabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        await CloseTabAsync(args.Tab);
    }

    private async Task CloseTabAsync(TabViewItem tab)
    {
        if (_tabData.TryGetValue(tab, out var documentData) &&
            !await documentData.viewer.FlushPendingPdfAnnotationsAsync())
            return;

        // Handle closing Home tab — recreate it to ensure window always has one tab
        bool wasHomeTab = ReferenceEquals(tab, _homeTab);
        
        // Clean up document/special tabs
        if (_tabData.ContainsKey(tab))
        {
            if (_tabData[tab].viewer is ViewerPage vp)
                vp.CancelLoad();
            _tabData.Remove(tab);
        }
        _specialTabs.Remove(tab);
        
        Tabs.TabItems.Remove(tab);

        // If Home tab was closed, recreate it
        if (wasHomeTab)
        {
            _homeTab = null;
            AddHomeTab();
            Tabs.SelectedItem = _homeTab;
        }
        // Otherwise, if all document tabs are closed, select Home
        else if (_tabData.Count == 0 && Tabs.TabItems.Count > 0)
        {
            Tabs.SelectedItem = _homeTab;
        }
        
        // Save session state after closing tab
        SaveSession();
    }

    // ── Tab tear-out: drag tab out → new window ──────────────────────────────
    // SDK 2.3 two-phase protocol:
    //   Phase 1 — TabTearOutWindowRequested: create window, MUST set args.NewWindowId
    //             (if not set, the SDK cancels the tear-out and may crash).
    //   Phase 2 — TabTearOutRequested: tab has been removed from this TabView by the
    //             SDK; open the document in the new window and clean up local state.
    //
    // IMPORTANT: Only the PICKED (dragged) tab tears out — not all tabs in the window.

    private TitleBarWindow? _tearOutWindow;
    private DocumentItem?   _tearOutDoc;
    private TabViewItem?    _tearOutTab;

    private void Tabs_TabTearOutWindowRequested(
        TabView sender, TabViewTabTearOutWindowRequestedEventArgs args)
    {
        // Identify the specific tab being torn out (args.Tabs[0] is the dragged tab).
        if (args.Tabs.Length == 0) return;
        if (args.Tabs[0] is not TabViewItem tab) return;
        
        // Store which tab is being torn out
        _tearOutTab = tab;
        
        // Handle Home tab tear-out — create document from current Home state
        if (ReferenceEquals(tab, _homeTab))
        {
            // Home tab tears out as a new Home tab in the new window
            var w = new TitleBarWindow();
            App.AllWindows.Add(w);
            w.Closed += (_, _) => App.AllWindows.Remove(w);
            
            args.NewWindowId = w.AppWindow.Id;
            _tearOutWindow = w;
            _tearOutDoc = null;  // Home has no document
            return;
        }
        
        // Handle document tab tear-out
        if (!_tabData.TryGetValue(tab, out var data)) return;

        // Create a new TitleBarWindow but don't open the doc yet — the SDK
        // still needs to complete its drag animation before the window is ready.
        var newWindow = new TitleBarWindow();
        App.AllWindows.Add(newWindow);
        newWindow.Closed += (_, _) => App.AllWindows.Remove(newWindow);

        // REQUIRED: tell the SDK which window will host the torn-out tab.
        // Omitting this causes an access violation in the SDK's drag handler.
        args.NewWindowId = newWindow.AppWindow.Id;

        // Carry state to Phase 2.
        _tearOutWindow = newWindow;
        _tearOutDoc    = data.doc;
    }

    private async void Tabs_TabTearOutRequested(
        TabView sender, TabViewTabTearOutRequestedEventArgs args)
    {
        // The SDK has already removed the tab from this TabView.
        // Our job: clean up local state and open the doc in the new window.
        if (args.Tabs.Length == 0) return;
        if (args.Tabs[0] is not TabViewItem tab) return;

        var newWindow = _tearOutWindow;
        var doc       = _tearOutDoc;
        var tornTab   = _tearOutTab;
        
        _tearOutWindow = null;
        _tearOutDoc    = null;
        _tearOutTab    = null;

        // Handle Home tab tear-out — just recreate it in source window
        bool wasHomeTab = ReferenceEquals(tab, tornTab) && ReferenceEquals(tornTab, _homeTab);
        if (wasHomeTab)
        {
            // Recreate Home tab in this window
            _homeTab = null;
            AddHomeTab();
            Tabs.SelectedItem = _homeTab;
            
            // New window already has its Home tab from constructor
            if (newWindow != null)
            {
                newWindow.Activate();
                newWindow.Tabs.SelectedItem = newWindow._homeTab;
            }
            return;
        }

        // Clean up viewer state for the document tab that left this window.
        if (_tabData.TryGetValue(tab, out var data))
        {
            if (!await data.viewer.FlushPendingPdfAnnotationsAsync())
            {
                Tabs.TabItems.Add(tab);
                Tabs.SelectedItem = tab;
                newWindow?.Close();
                return;
            }
            data.viewer.CancelLoad();
            _tabData.Remove(tab);
        }

        // If no more document tabs, select Home
        if (_tabData.Count == 0)
            Tabs.SelectedItem = _homeTab;
        
        // Save session state when tabs change
        SaveSession();

        // Open the document in the detached window once it is activated.
        if (newWindow == null || doc == null) return;

        newWindow.Activate();
        newWindow.Activated += OnReady;

        void OnReady(object s, WindowActivatedEventArgs _)
        {
            newWindow.Activated -= OnReady;
            newWindow.OpenDocument(doc);
        }
    }

    private void Tabs_ExternalTornOutTabsDropped(TabView sender, TabViewExternalTornOutTabsDroppedEventArgs args)
    {
        // A tab from another DocGlance window was dropped onto this one.
        // args.Tabs contains the TabViewItems — but since UI elements can't
        // cross windows we use the Tag (DocumentItem) we attached at tear-out.
        foreach (var obj in args.Tabs)
        {
            if (obj is TabViewItem tab && tab.Tag is DocumentItem doc)
                OpenDocument(doc);
        }
    }

    /// <summary>
    /// Creates a brand-new TitleBarWindow that opens doc and registers itself
    /// in App.AllWindows. The window closes itself when its last doc tab closes.
    /// </summary>
    private static TitleBarWindow CreateDetachedWindow(DocumentItem doc)
    {
        var w = new TitleBarWindow();
        App.AllWindows.Add(w);

        w.Closed += (_, _) => App.AllWindows.Remove(w);

        // Open document after the window is activated and laid out
        w.Activated += OnNewWindowActivated;

        void OnNewWindowActivated(object s, WindowActivatedEventArgs _)
        {
            w.Activated -= OnNewWindowActivated;
            w.OpenDocument(doc);
        }

        return w;
    }

    private void Tabs_AddTabButtonClick(TabView sender, object args)
        => _ = OpenFilePicker();

    private async System.Threading.Tasks.Task OpenFilePicker()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            foreach (var ext in new[]
            {
                ".pdf",".docx",".doc",".xlsx",".xls",".pptx",".ppt",
                ".txt",".md",".json",".xml",".html",".css",".js",".ts",
                ".cs",".py",".log",".csv",".rtf",".png",".jpg",".jpeg"
            }) picker.FileTypeFilter.Add(ext);

            InitializeWithWindow.Initialize(picker, Hwnd);
            var files = await picker.PickMultipleFilesAsync();
            if (files != null)
            {
                foreach (var file in files)
                {
                    var doc = await DocumentItemFromStorageFile(file);
                    OpenDocument(doc);
                }
            }
        }
        catch (Exception ex)
        {
            Infrastructure.Logging.Logger.Error("OpenFilePicker failed", ex);
        }
    }

    private static async System.Threading.Tasks.Task<DocumentItem>
        DocumentItemFromStorageFile(StorageFile file)
    {
        var props = await file.GetBasicPropertiesAsync();
        double bytes = props.Size;
        string[] units = { "B","KB","MB","GB" };
        int i = 0;
        while (bytes >= 1024 && i < units.Length - 1) { bytes /= 1024; i++; }
        string size = $"{bytes:0.##} {units[i]}";

        return new DocumentItem
        {
            FilePath      = file.Path,
            Title         = file.DisplayName,
            FileType      = file.FileType.TrimStart('.').ToUpperInvariant(),
            FileSize      = size,
            FileSizeBytes = (long)props.Size,
            LastWriteTime = props.DateModified.DateTime,
            FolderPath    = Path.GetDirectoryName(file.Path) ?? "",
        };
    }

    // ── Drag & drop ───────────────────────────────────────────────────────────

    private void RootGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Open";
        }
    }

    private async void RootGrid_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var items = await e.DataView.GetStorageItemsAsync();
            foreach (var item in items)
                if (item is StorageFile file)
                    OpenDocument(await DocumentItemFromStorageFile(file));
        }
        catch (Exception ex)
        {
            // Unguarded async void here crashed straight into the XAML handler.
            Infrastructure.Logging.Logger.Error("RootGrid_Drop failed", ex);
        }
    }

    // ── Window icon (unpackaged) ──────────────────────────────────────────────

    // For unpackaged WinUI 3 apps AppWindow.SetIcon needs an absolute path.
    // WM_SETICON on the HWND is additionally required for the taskbar and
    // Alt+Tab switcher to show the correct icon instead of the generic WinUI frame.

    private const int  ICON_SMALL      = 0;
    private const int  ICON_BIG        = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const int  IMAGE_ICON      = 1;
    private const int  WM_SETICON      = 0x0080;
    private const int  SW_RESTORE      = 9;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, string lpszName,
        uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg,
        IntPtr wParam, IntPtr lParam);

    private void SetWindowIcon()
    {
        try
        {
#if PACKAGED
            // In packaged apps, AppWindow.SetIcon() accepts package-relative paths.
            // The P/Invoke WM_SETICON approach is unnecessary — the AppWindow API
            // handles title bar, taskbar, and Alt+Tab icons automatically.
            string icoPath = "Assets/AppIcon.ico";
            if (System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, icoPath)))
            {
                AppWindow.SetIcon(icoPath);
                Logger.Log("[Icon] Set window icon from package assets.");
            }
#else
            // Unpackaged apps require absolute paths for AppWindow.SetIcon(),
            // plus manual WM_SETICON calls for taskbar and Alt+Tab.
            string icoPath = Path.Combine(
                AppContext.BaseDirectory, "Assets", "AppIcon.ico");

            if (!File.Exists(icoPath)) return;

            // 1. AppWindow.SetIcon — updates the title bar chrome icon
            AppWindow.SetIcon(icoPath);

            IntPtr hwnd = Hwnd;

            // 2. Large icon (32×32) — taskbar + Alt+Tab
            IntPtr hBig = LoadImage(IntPtr.Zero, icoPath,
                IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
            if (hBig != IntPtr.Zero)
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, hBig);

            // 3. Small icon (16×16) — title bar on classic themes
            IntPtr hSmall = LoadImage(IntPtr.Zero, icoPath,
                IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
            if (hSmall != IntPtr.Zero)
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, hSmall);

            // Do NOT DestroyIcon — the OS owns these for the window's lifetime
#endif
        }
        catch (Exception ex)
        {
            Logger.Log($"[Icon] SetWindowIcon failed: {ex.Message}");
        }
    }

    private void ConfigureAcrylicBackdrop(bool isDark)
    {
        if (!isDark)
        {
            RemoveAcrylicBackdrop();
            RootGrid.Background = new SolidColorBrush(Microsoft.UI.Colors.White);
            return;
        }

        if (_acrylicController != null)
        {
            _acrylicConfiguration!.Theme = SystemBackdropTheme.Dark;
            RootGrid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            return;
        }

        if (!DesktopAcrylicController.IsSupported())
        {
            RootGrid.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 30));
            return;
        }

        var configuration = new SystemBackdropConfiguration
        {
            // Keep Desktop Acrylic active when another owned/system window (for example,
            // the file picker) takes focus; the built-in backdrop replaces it with a flat tint.
            IsInputActive = true,
            Theme = SystemBackdropTheme.Dark
        };
        var controller = new DesktopAcrylicController
        {
            TintColor = Windows.UI.Color.FromArgb(255, 30, 30, 30),
            FallbackColor = Windows.UI.Color.FromArgb(255, 30, 30, 30)
        };
        var target = this.As<ICompositionSupportsSystemBackdrop>();
        try
        {
            controller.AddSystemBackdropTarget(target);
            controller.SetSystemBackdropConfiguration(configuration);
            _acrylicController = controller;
            _acrylicConfiguration = configuration;
            _acrylicTarget = target;
            RootGrid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        catch (Exception ex)
        {
            controller.Dispose();
            RootGrid.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 30));
            Logger.Log($"[Backdrop] Failed to attach Desktop Acrylic: {ex.Message}");
        }
    }

    private void RemoveAcrylicBackdrop()
    {
        if (_acrylicController == null)
            return;

        try
        {
            if (_acrylicTarget != null)
                _acrylicController.RemoveSystemBackdropTarget(_acrylicTarget);
        }
        catch (Exception ex)
        {
            Logger.Log($"[Backdrop] Failed to detach Desktop Acrylic: {ex.Message}");
        }
        finally
        {
            _acrylicController.Dispose();
            _acrylicController = null;
            _acrylicConfiguration = null;
            _acrylicTarget = null;
        }
    }

    // ── Title bar theme configuration ─────────────────────────────────────────
    
    public void ConfigureTitleBarTheme()
    {
        try
        {
            // Determine current theme
            var theme = ElementTheme.Default;
            if (Content is FrameworkElement root)
                theme = root.ActualTheme;

            bool isDark = theme == ElementTheme.Dark ||
                         (theme == ElementTheme.Default &&
                          Application.Current.RequestedTheme == ApplicationTheme.Dark);

            ConfigureAcrylicBackdrop(isDark);

            var titleBar = AppWindow.TitleBar;
            if (titleBar == null) return;

            // Transparent title-bar controls reveal the Acrylic window material in dark mode.
            var fg      = isDark ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
                                 : Windows.UI.Color.FromArgb(255,   0,   0,   0);
            var fgDim   = isDark ? Windows.UI.Color.FromArgb(180, 255, 255, 255)
                                 : Windows.UI.Color.FromArgb(180,   0,   0,   0);
            var hoverBg = isDark ? Windows.UI.Color.FromArgb( 20, 255, 255, 255)
                                 : Windows.UI.Color.FromArgb( 20,   0,   0,   0);
            var pressBg = isDark ? Windows.UI.Color.FromArgb( 35, 255, 255, 255)
                                 : Windows.UI.Color.FromArgb( 35,   0,   0,   0);
            var transp  = Windows.UI.Color.FromArgb(0, 0, 0, 0);

            titleBar.BackgroundColor                 = transp;
            titleBar.InactiveBackgroundColor         = transp;
            titleBar.ButtonBackgroundColor           = transp;
            titleBar.ButtonInactiveBackgroundColor   = transp;
            titleBar.ButtonForegroundColor           = fg;
            titleBar.ButtonInactiveForegroundColor   = fgDim;
            titleBar.ButtonHoverForegroundColor      = fg;
            titleBar.ButtonHoverBackgroundColor      = hoverBg;
            titleBar.ButtonPressedForegroundColor    = fgDim;
            titleBar.ButtonPressedBackgroundColor    = pressBg;
        }
        catch (Exception ex)
        {
            DocGlance.Infrastructure.Logging.Logger.Log(
                $"[Theme] ConfigureTitleBarTheme failed: {ex.Message}");
        }
    }

    // ── Session State Management ──────────────────────────────────────────────

    /// <summary>
    /// Saves the current session state (open tabs) so it can be restored on next launch.
    /// </summary>
    public void SaveSession()
    {
        int selectedIndex = -1;
        if (Tabs.SelectedItem is TabViewItem selectedTab && _tabData.ContainsKey(selectedTab))
            selectedIndex = _tabData.Keys.ToList().IndexOf(selectedTab);

        SessionService.SaveSession(_tabData.Values.Select(value => value.doc), selectedIndex);
    }

    /// <summary>
    /// Restores the previous session (reopens tabs that were open when the app closed).
    /// </summary>
    public void RestoreSession()
    {
        SessionService.RestoreSession(DispatcherQueue, OpenDocument, SelectRestoredDocument);
    }

    private void SelectRestoredDocument(DocumentItem document)
    {
        var selectedTab = _tabData
            .FirstOrDefault(entry => ReferenceEquals(entry.Value.doc, document)).Key;
        if (selectedTab != null)
            Tabs.SelectedItem = selectedTab;
    }
}
