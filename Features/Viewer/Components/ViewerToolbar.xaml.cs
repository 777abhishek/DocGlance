using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System;
using DocGlance.Infrastructure.Logging;

namespace DocGlance.Features.Viewer.Components;

public sealed partial class ViewerToolbar : UserControl
{
    public event EventHandler? SidebarToggled;
    public event EventHandler? FitWidthRequested;
    public event EventHandler? FitPageRequested;
    public event EventHandler<double>? RotateRequested;
    public event EventHandler? FindRequested;
    public event EventHandler? OpenExternalRequested;
    public event EventHandler? ShowInExplorerRequested;
    public event EventHandler? DocInfoRequested;
    public event EventHandler? PresentationModeRequested;
    public event EventHandler? PrintRequested;
    public event EventHandler? CopyPageTextRequested;
    public event EventHandler? CopyPathRequested;
    public event EventHandler? BookmarkRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? WordWrapRequested;
    
    // Page navigation
    public event EventHandler<int>? GoToPageRequested;

    // ── SidebarButton proxies ─────────────────────────────────────────────────
    public bool IsSidebarOpen => SidebarButton.IsChecked ?? false;
    public bool? SidebarIsChecked
    {
        get => SidebarButton.IsChecked;
        set => SidebarButton.IsChecked = value;
    }
    public Visibility SidebarButtonVisibility
    {
        get => SidebarButton.Visibility;
        set => SidebarButton.Visibility = value;
    }

    // ── FitWidthButton proxies ────────────────────────────────────────────────
    public bool IsFitWidthChecked
    {
        get => FitWidthButton.IsChecked ?? false;
        set => FitWidthButton.IsChecked = value;
    }
    public Visibility FitWidthButtonVisibility
    {
        get => FitWidthButton.Visibility;
        set => FitWidthButton.Visibility = value;
    }

    // ── FitPageButton proxies ─────────────────────────────────────────────────
    public bool IsFitPageChecked
    {
        get => FitPageButton.IsChecked ?? false;
        set => FitPageButton.IsChecked = value;
    }
    public Visibility FitPageButtonVisibility
    {
        get => FitPageButton.Visibility;
        set => FitPageButton.Visibility = value;
    }

    // ── WordWrapButton proxies ────────────────────────────────────────────────
    public bool IsWordWrapChecked
    {
        get => WordWrapButton.IsChecked ?? false;
        set => WordWrapButton.IsChecked = value;
    }
    public Visibility WordWrapVisibility
    {
        get => WordWrapButton.Visibility;
        set => WordWrapButton.Visibility = value;
    }

    public ViewerToolbar() => InitializeComponent();

    public void UpdatePageInfo(int current, int total)
    {
        PageNumberBox.Text = current.ToString();
        TotalPagesText.Text = $"/ {total}";
    }

    private void SidebarButton_Click(object sender, RoutedEventArgs e) => SidebarToggled?.Invoke(this, EventArgs.Empty);
    private void FitWidthButton_Click(object sender, RoutedEventArgs e) => FitWidthRequested?.Invoke(this, EventArgs.Empty);
    private void FitPageButton_Click(object sender, RoutedEventArgs e) => FitPageRequested?.Invoke(this, EventArgs.Empty);
    private void RotateLeftButton_Click(object sender, RoutedEventArgs e) => RotateRequested?.Invoke(this, -90);
    private void RotateRightButton_Click(object sender, RoutedEventArgs e) => RotateRequested?.Invoke(this, 90);
    private void FindButton_Click(object sender, RoutedEventArgs e) => FindRequested?.Invoke(this, EventArgs.Empty);
    private void OpenExternalButton_Click(object sender, RoutedEventArgs e) => OpenExternalRequested?.Invoke(this, EventArgs.Empty);
    private void ShowInExplorerButton_Click(object sender, RoutedEventArgs e) => ShowInExplorerRequested?.Invoke(this, EventArgs.Empty);
    private void DocInfoButton_Click(object sender, RoutedEventArgs e) => DocInfoRequested?.Invoke(this, EventArgs.Empty);
    private void PresentationModeMenuItem_Click(object sender, RoutedEventArgs e) => PresentationModeRequested?.Invoke(this, EventArgs.Empty);
    private void PrintMenuItem_Click(object sender, RoutedEventArgs e) => PrintRequested?.Invoke(this, EventArgs.Empty);
    private void CopyPageTextMenuItem_Click(object sender, RoutedEventArgs e) => CopyPageTextRequested?.Invoke(this, EventArgs.Empty);
    private void CopyPathMenuItem_Click(object sender, RoutedEventArgs e) => CopyPathRequested?.Invoke(this, EventArgs.Empty);
    private void BookmarkMenuItem_Click(object sender, RoutedEventArgs e) => BookmarkRequested?.Invoke(this, EventArgs.Empty);
    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    private void ToolsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Logger.Log("[ViewerToolbar] Document Tools menu item clicked.");

        var window = DocGlance.UI.Shell.TitleBarWindow.Current;
        if (window == null)
        {
            Logger.Log("[ViewerToolbar] Document Tools navigation failed: TitleBarWindow.Current is null.");
            return;
        }

        Logger.Log("[ViewerToolbar] Opening Document Tools page.");
        window.OpenToolsPage();
    }
    private void WordWrapButton_Click(object sender, RoutedEventArgs e) => WordWrapRequested?.Invoke(this, EventArgs.Empty);
    
    private void PageNumber_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (!int.TryParse(PageNumberBox.Text, out int current)) current = 1;
        if (!int.TryParse(TotalPagesText.Text.Replace("/ ", ""), out int total)) total = 1;

        var box = new NumberBox
        {
            Value = current,
            Minimum = 1,
            Maximum = total,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Width = 120,
        };
        var flyout = new Flyout { Content = box };
        box.KeyDown += (_, ke) =>
        {
            if (ke.Key == Windows.System.VirtualKey.Enter)
            {
                GoToPageRequested?.Invoke(this, (int)box.Value);
                flyout.Hide();
            }
            if (ke.Key == Windows.System.VirtualKey.Escape)
                flyout.Hide();
        };
        flyout.ShowAt(PageNumberBox);
    }
}
