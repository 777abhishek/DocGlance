using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using QuickView.Services.Storage;
using System;
using System.IO;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace QuickView.Features.Viewer;

public sealed partial class ViewerPage
{
    private void Toolbar_SidebarToggled(object? sender, EventArgs e)
    {
        bool isOpen = Toolbar.IsSidebarOpen;
        SidebarCard.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
        SidebarColumn.Width = isOpen ? new GridLength(240) : new GridLength(0);
        ViewModel.SidebarOpen = isOpen;
        ReapplyFitMode();
    }

    private void Toolbar_GoToPageRequested(object? sender, int page)
    {
        GoToPage(page);
    }

    private void Toolbar_FitWidthRequested(object? sender, EventArgs e)
    {
        _fitMode = FitMode.FitWidth;
        // fit mode set via IsFit*Checked;
        ApplyFitWidth();
    }

    private void Toolbar_FitPageRequested(object? sender, EventArgs e)
    {
        _fitMode = FitMode.FitPage;
        // fit mode set via IsFit*Checked;
        ApplyFitPage();
    }

    private void Toolbar_RotateRequested(object? sender, double delta) => Rotate(delta);

    private void Rotate(double delta)
    {
        _rotation = (_rotation + delta + 360.0) % 360.0;
        ViewModel.Rotation = _rotation;

        // VIEWPORT ARCHITECTURE: rotation swaps page axes (90°/270°), so the
        // canvas extent changes and the region must be re-rendered with the
        // PDFium rotate flag.
        if (_pdfState != null && _viewportState != null)
        {
            UpdateCanvasSize();
            _ = RenderPagesAsync();
        }
    }

    private void Toolbar_FindRequested(object? sender, EventArgs e)
    {
        bool show = FindBar.Visibility != Visibility.Visible;
        FindBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
            FindBox.Focus(FocusState.Programmatic);
        else
        {
            FindBox.Text = "";
            ClearSearchHighlights();
        }
    }

    private void FindBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Enter:
            {
                bool shift = Microsoft.UI.Input.InputKeyboardSource
                    .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                NavigateSearchHit(shift ? -1 : +1);
                e.Handled = true;
                break;
            }
            case Windows.System.VirtualKey.Escape:
                FindBar.Visibility = Visibility.Collapsed;
                FindBox.Text = "";
                ClearSearchHighlights();
                e.Handled = true;
                break;
        }
    }

    private void FindPrev_Click(object sender, RoutedEventArgs e) => NavigateSearchHit(-1);
    private void FindNext_Click(object sender, RoutedEventArgs e) => NavigateSearchHit(+1);
    private void FindClose_Click(object sender, RoutedEventArgs e)
    {
        FindBar.Visibility = Visibility.Collapsed;
        FindBox.Text = "";
        if (MatchCaseButton != null) MatchCaseButton.IsChecked = false;
        ClearSearchHighlights();
    }

    internal void BookmarkMenuItem_Click(object? sender, EventArgs e)
    {
        if (_doc?.FilePath == null) return;
        BookmarkStore.Toggle(_doc.FilePath, _page, $"Page {_page}");
        if (BookmarksTab.IsChecked == true) RefreshBookmarkList();
    }

    private void CopyPathMenuItem_Click(object? sender, EventArgs e)
    {
        if (_doc?.FilePath == null) return;
        var pkg = new DataPackage();
        pkg.SetText(_doc.FilePath);
        Clipboard.SetContent(pkg);
    }

    private async void CopyPageTextMenuItem_Click(object? sender, EventArgs e)
    {
        if (_pdfState == null) return;
        string text = await Rendering.Pdf.PdfTextService.ExtractTextAsync(_pdfState, _page - 1);
        if (text.Length == 0) return;
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private void PresentationModeMenuItem_Click(object? sender, EventArgs e)
    {
        var app = Microsoft.UI.Xaml.Application.Current as QuickView.App;
        var window = app?.MainWindow;
        if (window == null) return;

        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(
            Microsoft.UI.Win32Interop.GetWindowIdFromWindow(
                WinRT.Interop.WindowNative.GetWindowHandle(window)));

        if (appWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
            appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);
        else
            appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
    }

    private async void PrintMenuItem_Click(object? sender, EventArgs e)
    {
        if (_doc?.FilePath == null) return;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(_doc.FilePath);
            await Windows.System.Launcher.LaunchFileAsync(file,
                new Windows.System.LauncherOptions { ContentType = "application/pdf" });
        }
        catch { }
    }

    internal async void OpenExternalButton_Click(object? sender, EventArgs e)
    {
        if (_doc?.FilePath == null) return;
        try
        {
            await Windows.System.Launcher.LaunchFileAsync(
                await StorageFile.GetFileFromPathAsync(_doc.FilePath));
        }
        catch { }
    }

    private async void ShowInExplorerButton_Click(object? sender, EventArgs e)
    {
        if (_doc?.FilePath == null) return;
        try
        {
            var file   = await StorageFile.GetFileFromPathAsync(_doc.FilePath);
            var folder = await StorageFolder.GetFolderFromPathAsync(
                             Path.GetDirectoryName(_doc.FilePath)!);
            var opts = new Windows.System.FolderLauncherOptions();
            opts.ItemsToSelect.Add(file);
            await Windows.System.Launcher.LaunchFolderAsync(folder, opts);
        }
        catch { }
    }

    private async void DocInfoButton_Click(object? sender, EventArgs e)
    {
        if (_doc == null || XamlRoot == null) return;
        var dialog = Components.DocumentInfoDialog.Build(_doc, _total, XamlRoot);
        await dialog.ShowAsync();
    }

    private void SettingsMenuItem_Click(object? sender, EventArgs e)
    {
        UI.Shell.TitleBarWindow.Current?.NavigateTo(typeof(Features.Settings.SettingsPage));
    }
}
