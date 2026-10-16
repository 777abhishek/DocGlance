using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DocGlance.Core.Models;

public partial class PageThumbnailItem : ObservableObject
{
    [ObservableProperty] private int          pageNumber;
    [ObservableProperty] private string       pageLabel    = string.Empty;
    [ObservableProperty] private ImageSource? thumbnail;
    [ObservableProperty] private bool         isSelected;

    // Aspect-ratio height for the thumbnail box (matches Rune ThumbnailItem.BoxHeight).
    // Sidebar thumb width is 154px. Default A4 ratio = 297/210 ≈ 1.414.
    // Updated when the real page size is known.
    [ObservableProperty] private double boxHeight = 154 * 1.414; // ≈ 218
    [ObservableProperty] private double boxWidth = 200;
    [ObservableProperty] private double rotationAngle;

    // Accent ring thickness (Rune pattern): 2px when selected, 0 otherwise.
    public Thickness RingThickness => IsSelected ? new Thickness(2) : new Thickness(0);
    public double DisplayBoxWidth => IsQuarterTurn ? BoxHeight : BoxWidth;
    public double DisplayBoxHeight => IsQuarterTurn ? BoxWidth : BoxHeight;
    public double RotationCenterX => BoxWidth / 2;
    public double RotationCenterY => BoxHeight / 2;

    public bool HasThumbnail => Thumbnail != null;
    private bool IsQuarterTurn => Math.Abs(RotationAngle % 180) == 90;

    partial void OnThumbnailChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(HasThumbnail));
    }

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(RingThickness));
    }

    partial void OnBoxHeightChanged(double value)
    {
        OnPropertyChanged(nameof(DisplayBoxWidth));
        OnPropertyChanged(nameof(DisplayBoxHeight));
        OnPropertyChanged(nameof(RotationCenterY));
    }

    partial void OnBoxWidthChanged(double value)
    {
        OnPropertyChanged(nameof(DisplayBoxWidth));
        OnPropertyChanged(nameof(DisplayBoxHeight));
        OnPropertyChanged(nameof(RotationCenterX));
    }

    partial void OnRotationAngleChanged(double value)
    {
        OnPropertyChanged(nameof(DisplayBoxWidth));
        OnPropertyChanged(nameof(DisplayBoxHeight));
    }
}
