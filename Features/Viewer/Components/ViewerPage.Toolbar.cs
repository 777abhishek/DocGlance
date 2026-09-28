using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using QuickView.Infrastructure.Storage;
using System;
using System.IO;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace QuickView.Features.Viewer;

public sealed partial class ViewerPage
{
    // ── Sidebar toggle ────────────────────────────────────────────────────────

    private void SidebarButton_Click(object sender, RoutedEventArgs e)
    {
        bool isOpen = SidebarButton.IsChecked ?? false;

        // Hide/show the sidebar content
        SidebarCard.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;

        // Collapse/expand the column — instant, no animation needed
        SidebarColumn.Width = isOpen ? new GridLength(240) : new GridLength(0);

        // Force a synchronous measure/arrange pass so ViewportWidth reflects
        // the new column width before we compute the fit zoom.  Using a
        // deferred enqueue (old approach) was racy: it sometimes read the stale
        // width and produced a wrong zoom, especially on fast toggles.
        // Calling UpdateLayout() here is the same pattern Rune uses in its
        // Scroller_SizeChanged handler — read the authoritative new size,
        // then rebuild the layout against it.
        ReapplyFitMode();
    }

    // ── Page number tap → flyout input ───────────────────────────────────────

    private void PageNumber_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var box = new NumberBox
        {
            Value              = _page,
            Minimum            = 1,
            Maximum            = _total,
            SmallChange        = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Width              = 120,
        };
        var flyout = new Flyout { Content = box };
        box.KeyDown += (_, ke) =>
        {
            if (ke.Key == Windows.System.VirtualKey.Enter)
            {
                GoToPage((int)box.Value);
                flyout.Hide();
            }
            if (ke.Key == Windows.System.VirtualKey.Escape)
                flyout.Hide();
        };
        flyout.ShowAt(PageNumberBox);
    }

    // ── Fit / Rotate ──────────────────────────────────────────────────────────

    private void FitWidthButton_Click(object sender, RoutedEventArgs e)
    {
        _fitMode = FitMode.FitWidth;
        FitWidthButton.IsChecked = true;
        FitPageButton.IsChecked  = false;
        ApplyFitWidth();
    }

    private void FitPageButton_Click(object sender, RoutedEventArgs e)
    {
        _fitMode = FitMode.FitPage;
        FitPageButton.IsChecked  = true;
        FitWidthButton.IsChecked = false;
        ApplyFitPage();
    }

    private void RotateRightButton_Click(object sender, RoutedEventArgs e) => Rotate(+90);
    private void RotateLeftButton_Click(object sender,  RoutedEventArgs e) => Rotate(-90);

    /// <summary>
    /// Rotates the document view by <paramref name="delta"/> degrees (multiple of 90).
    ///
    /// Windows.Data.Pdf.PdfPageRenderOptions has no rotation parameter, so we
    /// apply a RenderTransform to the Border + swap Width/Height on the
    /// surrounding StackPanel item so the layout engine allocates the correct
    /// space (portrait ↔ landscape).
    ///
    /// Concretely:
    ///   • At 0°/180°: Border.Width = physW, Border.Height = physH  (portrait)
    ///   • At 90°/270°: Border.Width = physH, Border.Height = physW (landscape)
    ///     with a RotateTransform on Border centred at (physH/2, physW/2).
    ///
    /// This approach preserves the existing BitmapImages (no re-render needed)
    /// and gives the StackPanel the correct bounding box, so fit-width reads
    /// the right dimension after rotation.  Thumbnails get the same treatment.
    ///
    /// After updating all children, ReapplyFitMode() re-computes the zoom.
    /// </summary>
    private void Rotate(double delta)
    {
        _rotation = (_rotation + delta + 360.0) % 360.0;

        if (_pdfState == null) return;

        bool swap = (int)(_rotation / 90.0) % 2 == 1; // true at 90° and 270°

        for (int i = 0; i < PagesPanel.Children.Count; i++)
        {
            if (PagesPanel.Children[i] is not Border border) continue;

            // Primary source: Tag tuple stores original (unrotated) physical pixel
            // dimensions and is set at placeholder-creation time — always reliable.
            double physW = 0, physH = 0;
            if (border.Tag is (double ow, double oh))
            {
                physW = ow; physH = oh;
            }
            else
            {
                // Fallback: read from the Image inside the Grid (rendered page)
                // or from a plain Image (should not occur in practice).
                var img = FindPageImage(border);
                if (img != null && img.Width > 0)
                {
                    physW = img.Width; physH = img.Height;
                    border.Tag = (physW, physH); // cache for next time
                }
            }

            if (physW <= 0 || physH <= 0) continue;

            // M3: Use the same physical pixel units consistently as placeholder creation.
            // Placeholders set Grid.Width/Height = physW/physH (physical pixels).
            // Rotation swaps dimensions but stays in the same unit space.
            if (swap)
            {
                // 90° or 270°: layout slot is landscape (physH wide × physW tall)
                border.Width  = physH;
                border.Height = physW;
            }
            else
            {
                // 0° or 180°: layout slot is portrait
                border.Width  = physW;
                border.Height = physH;
            }
            border.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            border.RenderTransform = new Microsoft.UI.Xaml.Media.RotateTransform { Angle = _rotation };
        }

        // Update thumbnail BoxHeight to match new aspect ratio
        for (int i = 0; i < Thumbnails.Count && i < PagesPanel.Children.Count; i++)
        {
            if (PagesPanel.Children[i] is Border b && b.Tag is (double ow, double oh))
            {
                // Thumbnail width is always 200 logical px; aspect changes with rotation.
                double aspect = swap ? (ow / oh) : (oh / ow);
                Thumbnails[i].BoxHeight = 200.0 * aspect;
            }
        }

        // Re-compute zoom for the new page orientation.
        ReapplyFitMode();
    }

    /// <summary>
    /// Finds the Image element inside a page Border, regardless of whether the
    /// Border contains a plain Image (not yet used) or a Grid+Image+Canvas (rendered).
    /// </summary>
    private static Image? FindPageImage(Border border)
    {
        if (border.Child is Image img) return img;
        if (border.Child is Grid grid)
            foreach (var child in grid.Children)
                if (child is Image i) return i;
        return null;
    }

    // ── Find bar ──────────────────────────────────────────────────────────────

    private void FindButton_Click(object sender, RoutedEventArgs e)
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
                // Shift+Enter → previous hit; Enter → next hit
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

    // ── Bookmark ──────────────────────────────────────────────────────────────

    internal void BookmarkMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_doc?.FilePath == null) return;
        BookmarkStore.Toggle(_doc.FilePath, _page, $"Page {_page}");
        if (BookmarksTab.IsChecked == true) RefreshBookmarkList();
    }

    // ── Copy path ─────────────────────────────────────────────────────────────

    private void CopyPathMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_doc?.FilePath == null) return;
        var pkg = new DataPackage();
        pkg.SetText(_doc.FilePath);
        Clipboard.SetContent(pkg);
    }

    private async void CopyPageTextMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_pdfState == null) return;
        string text = await Rendering.Pdf.PdfTextService.ExtractTextAsync(_pdfState, _page - 1);
        if (text.Length == 0) return;
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    // ── Presentation mode (full-screen) ──────────────────────────────────────

    private void PresentationModeMenuItem_Click(object sender, RoutedEventArgs e)
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

    // ── Print ─────────────────────────────────────────────────────────────────

    private async void PrintMenuItem_Click(object sender, RoutedEventArgs e)
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

    // ── Open external / Explorer ──────────────────────────────────────────────

    internal async void OpenExternalButton_Click(object sender, RoutedEventArgs e)
    {
        if (_doc?.FilePath == null) return;
        try
        {
            await Windows.System.Launcher.LaunchFileAsync(
                await StorageFile.GetFileFromPathAsync(_doc.FilePath));
        }
        catch { }
    }

    private async void ShowInExplorerButton_Click(object sender, RoutedEventArgs e)
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

    // ── Document info ─────────────────────────────────────────────────────────

    private async void DocInfoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_doc == null || XamlRoot == null) return;
        var dialog = Components.DocumentInfoDialog.Build(_doc, _total, XamlRoot);
        await dialog.ShowAsync();
    }

    // ── Settings navigation ───────────────────────────────────────────────────

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Shell.TitleBarWindow.Current?.NavigateTo(typeof(Features.Settings.SettingsPage));
    }
}
