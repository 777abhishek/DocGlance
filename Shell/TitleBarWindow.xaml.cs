// TitleBarWindow.xaml.cs
// Chrome-style TabView: Home tab (permanent) + closeable document tabs.
// Each document tab owns its own ViewerPage instance so zoom/page state
// is preserved when switching tabs — exactly like VS Code / Rune.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WinRT.Interop;
using QuickView.Models;
using QuickView.Features.Home;
using QuickView.Features.Viewer;

namespace QuickView.Shell;

public sealed partial class TitleBarWindow : Window
{
    // ── Singleton so pages can reach the shell ────────────────────────────────
    public static new TitleBarWindow? Current { get; private set; }

    // ── Per-tab state: tab item → (ViewerPage, DocumentItem) ─────────────────
    private readonly Dictionary<TabViewItem, (ViewerPage viewer, DocumentItem doc)> _tabData = new();

    // The permanent "home" tab
    private TabViewItem? _homeTab;

    // ── HWND (for pickers in unpackaged apps) ─────────────────────────────────
    public IntPtr Hwnd => WindowNative.GetWindowHandle(this);

    public TitleBarWindow()
    {
        Current = this;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragArea);
        Title = "QuickView";

        // Set the window icon
        SetWindowIcon();

        // Drag & drop files onto window
        RootGrid.AllowDrop = true;

        // Add the permanent Home tab
        AddHomeTab();

        // Navigate to HomePage and start update checker
        HomeFrame.Navigate(typeof(HomePage));
        UpdateBannerControl.Attach();
    }

    // ── Home tab ──────────────────────────────────────────────────────────────

    private void AddHomeTab()
    {
        _homeTab = new TabViewItem
        {
            Header          = "Home",
            IsClosable      = false,
            IconSource      = new SymbolIconSource { Symbol = Symbol.Home },
        };
        Tabs.TabItems.Add(_homeTab);
        Tabs.SelectedItem = _homeTab;
    }

    // ── Open a document in a new tab (or focus existing) ─────────────────────

    public void OpenDocument(DocumentItem doc)
    {
        // If file already open in a tab, just focus it
        foreach (var kv in _tabData)
        {
            if (string.Equals(kv.Value.doc.FilePath, doc.FilePath,
                              StringComparison.OrdinalIgnoreCase))
            {
                Tabs.SelectedItem = kv.Key;
                return;
            }
        }

        // Persist to recent files and refresh the home page strip
        QuickView.Infrastructure.Storage.RecentFilesStore.Push(doc.FilePath);
        if (HomeFrame.Content is QuickView.Features.Home.HomePage home)
            home.LoadRecentDocuments();

        // Create new viewer tab
        var viewer = new ViewerPage();

        var tab = new TabViewItem
        {
            Header     = doc.Title,
            IsClosable = true,
            IconSource = new FontIconSource
            {
                Glyph    = doc.FileGlyph,
                FontSize = 12,
            },
        };

        _tabData[tab] = (viewer, doc);
        Tabs.TabItems.Add(tab);
        Tabs.SelectedItem = tab;    // triggers SelectionChanged → ShowViewer

        // Load document after tab is selected so ViewerPage is in the tree
        viewer.LoadDocument(doc);
    }

    // ── Legacy NavigateTo — kept so old call sites compile ───────────────────
    public void NavigateTo(Type pageType, object? parameter = null)
    {
        if (pageType == typeof(ViewerPage) && parameter is DocumentItem doc)
        {
            OpenDocument(doc);
            return;
        }
        HomeFrame.Navigate(pageType, parameter);
    }

    public void GoBack()
    {
        // "Back" means go to the home tab
        Tabs.SelectedItem = _homeTab;
    }

    // ── Tab events ────────────────────────────────────────────────────────────

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Tabs.SelectedItem is not TabViewItem selected) return;

        bool isHome = ReferenceEquals(selected, _homeTab);

        // Home tab
        HomeFrame.Visibility = isHome ? Visibility.Visible : Visibility.Collapsed;

        // Document tab
        if (!isHome && _tabData.TryGetValue(selected, out var data))
        {
            DocHost.Content    = data.viewer;
            DocHost.Visibility = Visibility.Visible;
        }
        else if (isHome)
        {
            DocHost.Visibility = Visibility.Collapsed;
            DocHost.Content    = null;
        }
    }

    private void Tabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        var tab = args.Tab;
        if (_tabData.ContainsKey(tab))
        {
            // Cancel any in-progress rendering
            if (_tabData[tab].viewer is ViewerPage vp)
                vp.CancelLoad();

            _tabData.Remove(tab);
        }
        Tabs.TabItems.Remove(tab);

        // If no document tabs remain, go home
        if (_tabData.Count == 0)
            Tabs.SelectedItem = _homeTab;
    }

    private void Tabs_AddTabButtonClick(TabView sender, object args)
        => _ = OpenFilePicker();

    // ── File picker (+ tab) ───────────────────────────────────────────────────

    private async System.Threading.Tasks.Task OpenFilePicker()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        foreach (var ext in new[]
        {
            ".pdf",".docx",".doc",".xlsx",".xls",".pptx",".ppt",
            ".txt",".md",".png",".jpg",".jpeg",".gif",".bmp",
            ".webp",".mp4",".mkv",".mov",".avi",".mp3",".wav",".flac"
        }) picker.FileTypeFilter.Add(ext);

        InitializeWithWindow.Initialize(picker, Hwnd);
        var files = await picker.PickMultipleFilesAsync();
        foreach (var file in files)
            OpenDocument(await DocumentItemFromStorageFile(file));
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
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        foreach (var item in items)
            if (item is StorageFile file)
                OpenDocument(await DocumentItemFromStorageFile(file));
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
            // Absolute path — required for unpackaged apps
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
        }
        catch (Exception ex)
        {
            QuickView.Infrastructure.Logging.Logger.Log(
                $"[Icon] SetWindowIcon failed: {ex.Message}");
        }
    }

}
