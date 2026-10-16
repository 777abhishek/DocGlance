using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DocGlance.Core.Enums;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using DocGlance.Services.Storage;
using DocGlance.Rendering.Pdf;
using DocGlance.Features.Viewer.Components;
using System;
using System.IO;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace DocGlance.Features.Viewer;

public sealed partial class ViewerPage
{
    private void Toolbar_SidebarToggled(object? sender, EventArgs e)
    {
        bool isOpen = Toolbar.IsSidebarOpen;
        SidebarCard.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
        SidebarColumn.Width = isOpen ? new GridLength(240) : new GridLength(0);
        ViewModel.SidebarOpen = isOpen;
        ScheduleFitModeReapply();
    }

    private void Toolbar_GoToPageRequested(object? sender, int page)
    {
        GoToPage(page);
    }

    private void Toolbar_FitWidthRequested(object? sender, EventArgs e)
    {
        SetFitMode(FitMode.FitWidth);
        ApplyFitWidth();
    }

    private void Toolbar_FitPageRequested(object? sender, EventArgs e)
    {
        SetFitMode(FitMode.FitPage);
        ApplyFitPage();
    }

    private void Toolbar_RotateRequested(object? sender, double delta) => Rotate(delta);

    private void Toolbar_PdfToolSelected(PdfAnnotationTool? tool)
    {
        if (_pdfState?.ManagedPdfDocument != null)
        {
            Toolbar.SetSelectedPdfTool(null);
            return;
        }
        PdfViewerControl.SetAnnotationTool(tool);
        if (tool != PdfAnnotationTool.Markup)
            ClearPdfTextSelection();
    }

    private void Toolbar_MarkupSettingsChanged(object? sender, EventArgs e)
    {
        PdfViewerControl.SetMarkupSettings(
            Toolbar.MarkupColorArgb, Toolbar.MarkupOpacity, Toolbar.MarkupKind);
    }

    private void Toolbar_UndoAnnotationRequested(object? sender, EventArgs e) =>
        _ = RestorePdfAnnotationHistoryAsync(undo: true);

    private void Toolbar_RedoAnnotationRequested(object? sender, EventArgs e) =>
        _ = RestorePdfAnnotationHistoryAsync(undo: false);

    private void Rotate(double delta)
    {
        _rotation = (_rotation + delta + 360.0) % 360.0;
        PdfViewerControl.PdfRotation = _rotation;
        ClearPdfTextSelection();
        ViewModel.Rotation = _rotation;

        // VIEWPORT ARCHITECTURE: rotation swaps page axes (90°/270°), so the
        // canvas extent changes and the region must be re-rendered.
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
        if (_spreadsheetDocument != null)
        {
            string sheetText = _spreadsheetDocument.ExtractSheetText(
                SpreadsheetViewerControl.ActiveSheetIndex);
            if (sheetText.Length == 0) return;

            var sheetPackage = new DataPackage();
            sheetPackage.SetText(sheetText);
            Clipboard.SetContent(sheetPackage);
            return;
        }

        if (_docxDocument != null)
        {
            string docxText = _docxDocument.ExtractPageText(_page - 1);
            if (docxText.Length == 0) return;

            var docxPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            docxPackage.SetText(docxText);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(docxPackage);
            return;
        }

    }

    private void PresentationModeMenuItem_Click(object? sender, EventArgs e)
    {
        TogglePresentationMode();
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
