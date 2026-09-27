// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Document;

public sealed partial class MainWindow : Window
{
    private readonly Dictionary<TabViewItem, string> _tabDocuments = new();
    private bool _showingHome = true;

    public MainWindow()
    {
        InitializeComponent();
        Title = "QuickView - Document Viewer";
        
        // Set up window
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragArea);

        // Enable drag-drop for files onto the window
        MainGrid.AllowDrop = true;
        MainGrid.DragOver  += MainGrid_DragOver;
        MainGrid.Drop      += MainGrid_Drop;
    }

    // ================================================================
    // DRAG & DROP — open files by dragging onto the window
    // ================================================================

    private void MainGrid_DragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Open files";
            e.DragUIOverride.IsGlyphVisible = true;
        }
    }

    private async void MainGrid_Drop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            return;

        var items = await e.DataView.GetStorageItemsAsync();
        foreach (var item in items)
        {
            if (item is Windows.Storage.StorageFile file)
                OpenDocument(file.Path, file.Name);
        }
    }

    // Called by the "Open File" button on the HomePage empty state
    public void RequestOpenFile() => _ = OpenDocumentAsync();

    // ================================================================
    // TAB MANAGEMENT
    // ================================================================

    private void DocumentTabs_AddTabButtonClick(TabView sender, object args)
    {
        _ = OpenDocumentAsync();
    }

    private void DocumentTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // View switching is handled by OpenDocument() when a new tab is opened.
        // This only handles the case where the user clicks an existing tab.
        if (_showingHome) return;   // already on home, don't interfere
        if (DocumentTabs.SelectedItem is TabViewItem)
        {
            ShowDocumentView();
        }
    }

    private void DocumentTabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab is TabViewItem tabToClose)
        {
            _tabDocuments.Remove(tabToClose);
            DocumentTabs.TabItems.Remove(tabToClose);

            if (DocumentTabs.TabItems.Count == 0)
            {
                ShowHomeView();
            }
        }
    }

    private void AddDocumentTab(string filePath, string fileName)
    {
        var newTab = new TabViewItem
        {
            Header = fileName,
            IconSource = new SymbolIconSource { Symbol = Symbol.Document }
        };

        _tabDocuments[newTab] = filePath;
        DocumentTabs.TabItems.Add(newTab);
        DocumentTabs.SelectedItem = newTab;
        // NOTE: do NOT call ShowDocumentView() here — OpenDocument() does it
        // after DocumentHost.Content is set.
    }

    // ================================================================
    // VIEW SWITCHING
    // ================================================================

    private void ShowHomeView()
    {
        _showingHome = true;
        HomePageView.Visibility = Visibility.Visible;
        DocumentHost.Visibility = Visibility.Collapsed;
        PdfActionBar.Visibility = Visibility.Collapsed;
        ThumbnailSidebar.Visibility = Visibility.Collapsed;
    }

    private void ShowDocumentView()
    {
        _showingHome = false;
        HomePageView.Visibility = Visibility.Collapsed;
        DocumentHost.Visibility = Visibility.Visible;
        PdfActionBar.Visibility = Visibility.Visible;
        
        // Hide the old standalone thumbnail sidebar (ViewerPage has its own)
        ThumbnailSidebar.Visibility = Visibility.Collapsed;

        // Enable action bar
        PdfActionBar.SetEnabled(true);
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_showingHome)
        {
            // Already on home, do nothing or toggle back to last document
            if (DocumentTabs.TabItems.Count > 0)
            {
                ShowDocumentView();
            }
        }
        else
        {
            ShowHomeView();
        }
    }

    // ================================================================
    // DOCUMENT OPERATIONS
    // ================================================================

    private async System.Threading.Tasks.Task OpenDocumentAsync()
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };

        // Documents
        picker.FileTypeFilter.Add(".pdf");
        picker.FileTypeFilter.Add(".docx");
        picker.FileTypeFilter.Add(".doc");
        picker.FileTypeFilter.Add(".xlsx");
        picker.FileTypeFilter.Add(".xls");
        picker.FileTypeFilter.Add(".pptx");
        picker.FileTypeFilter.Add(".ppt");
        picker.FileTypeFilter.Add(".txt");
        picker.FileTypeFilter.Add(".md");
        // Images
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".gif");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".webp");
        // Video
        picker.FileTypeFilter.Add(".mp4");
        picker.FileTypeFilter.Add(".mkv");
        picker.FileTypeFilter.Add(".avi");
        picker.FileTypeFilter.Add(".mov");
        // Audio
        picker.FileTypeFilter.Add(".mp3");
        picker.FileTypeFilter.Add(".wav");
        picker.FileTypeFilter.Add(".flac");

        // Initialize picker with window handle
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var files = await picker.PickMultipleFilesAsync();
        foreach (var file in files)
        {
            OpenDocument(file.Path, file.Name);
        }
    }

    private void OpenDocument(string filePath, string fileName)
    {
        System.Diagnostics.Debug.WriteLine($"OpenDocument called: {fileName}");
        
        AddDocumentTab(filePath, fileName);
        
        // Load ViewerPage for this document
        var document = new QuickView.Models.DocumentItem
        {
            FilePath = filePath,
            Title = fileName,
            FileType = System.IO.Path.GetExtension(filePath),
            FileSize = GetFileSize(filePath),
            LastWriteTime = System.IO.File.GetLastWriteTime(filePath)
        };

        System.Diagnostics.Debug.WriteLine($"Creating ViewerPage...");
        
        // Navigate to viewer page
        var viewerPage = new QuickView.Features.Viewer.ViewerPage();
        DocumentHost.Content = viewerPage;
        
        // Load document directly
        viewerPage.LoadDocument(document);
        
        System.Diagnostics.Debug.WriteLine($"Showing document view...");
        
        // Show viewer UI
        ShowDocumentView();
        
        // Update action bar with viewer state
        UpdateActionBarFromViewer();
        
        System.Diagnostics.Debug.WriteLine($"Document opened successfully!");
    }

    private string GetFileSize(string filePath)
    {
        try
        {
            var fileInfo = new System.IO.FileInfo(filePath);
            long bytes = fileInfo.Length;
            
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            
            return $"{size:0.##} {sizes[order]}";
        }
        catch
        {
            return "Unknown size";
        }
    }

    private void HomePage_DocumentOpened(object? sender, string filePath)
    {
        System.Diagnostics.Debug.WriteLine($"MainWindow received: {filePath}");
        
        var fileName = System.IO.Path.GetFileName(filePath);
        OpenDocument(filePath, fileName);
    }

    private void HomePage_OpenFileRequested(object? sender, object e)
        => _ = OpenDocumentAsync();

    // ================================================================
    // PDF ACTION BAR HANDLERS
    // ================================================================

    private QuickView.Features.Viewer.ViewerPage? CurrentViewerPage => DocumentHost.Content as QuickView.Features.Viewer.ViewerPage;

    private void PdfActionBar_PageChanged(object? sender, int pageNumber)
    {
        CurrentViewerPage?.NavigateToPage(pageNumber);
    }

    private void PdfActionBar_ZoomInRequested(object? sender, EventArgs e)
    {
        CurrentViewerPage?.ZoomIn();
        UpdateActionBarFromViewer();
    }

    private void PdfActionBar_ZoomOutRequested(object? sender, EventArgs e)
    {
        CurrentViewerPage?.ZoomOut();
        UpdateActionBarFromViewer();
    }

    private void PdfActionBar_FitWidthRequested(object? sender, EventArgs e)
    {
        CurrentViewerPage?.FitWidth();
        UpdateActionBarFromViewer();
    }

    private void PdfActionBar_RotateRequested(object? sender, EventArgs e)
    {
        CurrentViewerPage?.RotateClockwise();
    }

    private void PdfActionBar_SearchRequested(object? sender, EventArgs e)
    {
        CurrentViewerPage?.ShowSearch();
    }

    private void PdfActionBar_PrintRequested(object? sender, EventArgs e)
    {
        // TODO: Implement print
    }

    private void PdfActionBar_SaveAsRequested(object? sender, EventArgs e)
    {
        // TODO: Implement save as
    }

    private void PdfActionBar_PropertiesRequested(object? sender, EventArgs e)
    {
        // TODO: Show document properties
    }

    private void PdfActionBar_SettingsRequested(object? sender, EventArgs e)
    {
        NavigateToSettings();
    }

    public void NavigateToSettings()
    {
        // Show settings in the content area — reuse DocumentHost
        DocumentHost.Content = new QuickView.Features.Settings.SettingsPage();
        _showingHome = false;
        HomePageView.Visibility = Visibility.Collapsed;
        DocumentHost.Visibility = Visibility.Visible;
        PdfActionBar.Visibility = Visibility.Collapsed;
    }

    private void UpdateActionBarFromViewer()
    {
        if (CurrentViewerPage != null)
        {
            PdfActionBar.SetPageInfo(CurrentViewerPage.CurrentPage, CurrentViewerPage.TotalPages);
            PdfActionBar.SetZoomLevel(CurrentViewerPage.CurrentZoom * 100);
        }
    }

    // ================================================================
    // THUMBNAIL SIDEBAR HANDLERS
    // ================================================================

    private void ThumbnailSidebar_PageSelected(object? sender, int pageNumber)
    {
        CurrentViewerPage?.NavigateToPage(pageNumber);
        UpdateActionBarFromViewer();
    }
}

