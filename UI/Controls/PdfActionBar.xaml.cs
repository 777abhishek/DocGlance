using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace QuickView.UI.Controls;

public sealed partial class PdfActionBar : UserControl
{
    public event EventHandler<int>? PageChanged;
    public event EventHandler? ZoomInRequested;
    public event EventHandler? ZoomOutRequested;
    public event EventHandler? ZoomLevelClicked;
    public event EventHandler? FitWidthRequested;
    public event EventHandler? RotateRequested;
    public event EventHandler? SearchRequested;
    public event EventHandler? PrintRequested;
    public event EventHandler? SaveAsRequested;
    public event EventHandler? PropertiesRequested;
    public event EventHandler? SettingsRequested;

    public PdfActionBar()
    {
        InitializeComponent();
    }

    public void SetPageInfo(int currentPage, int totalPages)
    {
        PageBox.Value = currentPage;
        PageBox.Maximum = totalPages;
        PageCountLabel.Text = $"of {totalPages}";
    }

    public void SetZoomLevel(double zoomPercent)
    {
        ZoomLabel.Text = $"{Math.Round(zoomPercent)}%";
    }

    public void SetEnabled(bool enabled)
    {
        PageBox.IsEnabled = enabled;
        ZoomOutButton.IsEnabled = enabled;
        ZoomInButton.IsEnabled = enabled;
        ZoomLevelButton.IsEnabled = enabled;
        FitWidthButton.IsEnabled = enabled;
        RotateButton.IsEnabled = enabled;
        SearchButton.IsEnabled = enabled;
        PrintButton.IsEnabled = enabled;
        MoreButton.IsEnabled = enabled;
    }

    private void PageBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!double.IsNaN(args.NewValue))
        {
            PageChanged?.Invoke(this, (int)args.NewValue);
        }
    }

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        => ZoomOutRequested?.Invoke(this, EventArgs.Empty);

    private void ZoomInButton_Click(object sender, RoutedEventArgs e)
        => ZoomInRequested?.Invoke(this, EventArgs.Empty);

    private void ZoomLevelButton_Click(object sender, RoutedEventArgs e)
        => ZoomLevelClicked?.Invoke(this, EventArgs.Empty);

    private void FitWidthButton_Click(object sender, RoutedEventArgs e)
        => FitWidthRequested?.Invoke(this, EventArgs.Empty);

    private void RotateButton_Click(object sender, RoutedEventArgs e)
        => RotateRequested?.Invoke(this, EventArgs.Empty);

    private void SearchButton_Click(object sender, RoutedEventArgs e)
        => SearchRequested?.Invoke(this, EventArgs.Empty);

    private void PrintButton_Click(object sender, RoutedEventArgs e)
        => PrintRequested?.Invoke(this, EventArgs.Empty);

    private void SaveAsMenuItem_Click(object sender, RoutedEventArgs e)
        => SaveAsRequested?.Invoke(this, EventArgs.Empty);

    private void PropertiesMenuItem_Click(object sender, RoutedEventArgs e)
        => PropertiesRequested?.Invoke(this, EventArgs.Empty);

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        => SettingsRequested?.Invoke(this, EventArgs.Empty);
}
