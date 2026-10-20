using DocGlance.Core.Enums;
using DocGlance.Core.Models;
using DocGlance.Features.Viewer.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace DocGlance.Features.Viewer.Formats.Images;

public sealed partial class ImageViewer : UserControl, IDocumentViewer, ISupportsZoom
{
    private const double MaxDecodedPixels = 32_000_000;
    private const double SidePadding = 32;
    private const double VerticalPadding = 32;
    private double _imageWidth;
    private double _imageHeight;
    private double _zoom = 1;
    private FitMode _fitMode;

    public FrameworkElement Control => this;
    public ScrollViewer ScrollViewer => ImageScrollViewer;
    public double CurrentZoom => _zoom;
    public FitMode CurrentFitMode => _fitMode;
    public event EventHandler<double>? ZoomChanged;

    public ImageViewer()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        ImageScrollViewer.ViewChanged += OnViewChanged;
    }

    public async Task LoadAsync(DocumentItem document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(document.FilePath))
            throw new ArgumentException("The image has no file path.", nameof(document));

        cancellationToken.ThrowIfCancellationRequested();
        PreviewImage.Source = null;
        _imageWidth = 0;
        _imageHeight = 0;
        _fitMode = FitMode.None;
        _zoom = 1;
        ImageScrollViewer.ChangeView(0, 0, 1, disableAnimation: true);

        StorageFile file = await StorageFile.GetFileFromPathAsync(document.FilePath);
        using IRandomAccessStream stream = await file.OpenReadAsync();
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        cancellationToken.ThrowIfCancellationRequested();

        double sourceWidth = decoder.PixelWidth;
        double sourceHeight = decoder.PixelHeight;
        if (sourceWidth <= 0 || sourceHeight <= 0)
            throw new InvalidOperationException("The image has invalid dimensions.");

        double decodeScale = Math.Min(1, Math.Sqrt(MaxDecodedPixels / (sourceWidth * sourceHeight)));
        int decodeWidth = Math.Max(1, (int)Math.Floor(sourceWidth * decodeScale));
        int decodeHeight = Math.Max(1, (int)Math.Floor(sourceHeight * decodeScale));
        stream.Seek(0);

        var bitmap = new BitmapImage
        {
            DecodePixelType = DecodePixelType.Physical,
            DecodePixelWidth = decodeWidth,
            DecodePixelHeight = decodeHeight,
        };
        await bitmap.SetSourceAsync(stream);
        cancellationToken.ThrowIfCancellationRequested();

        PreviewImage.Source = bitmap;
        PreviewImage.Width = bitmap.PixelWidth;
        PreviewImage.Height = bitmap.PixelHeight;
        _imageWidth = bitmap.PixelWidth;
        _imageHeight = bitmap.PixelHeight;
        SetFitMode(FitMode.FitPage);
    }

    public Task CleanupAsync()
    {
        PreviewImage.Source = null;
        PreviewImage.Width = double.NaN;
        PreviewImage.Height = double.NaN;
        _imageWidth = 0;
        _imageHeight = 0;
        _fitMode = FitMode.None;
        _zoom = 1;
        ImageScrollViewer.ChangeView(0, 0, 1, disableAnimation: true);
        return Task.CompletedTask;
    }

    public void SetZoom(double zoom)
    {
        if (!double.IsFinite(zoom) || zoom <= 0)
            throw new ArgumentOutOfRangeException(nameof(zoom));

        _zoom = Math.Clamp(zoom, ImageScrollViewer.MinZoomFactor, ImageScrollViewer.MaxZoomFactor);
        ImageScrollViewer.ChangeView(null, null, (float)_zoom, disableAnimation: true);
        ZoomChanged?.Invoke(this, _zoom);
    }

    public void SetFitMode(FitMode mode)
    {
        _fitMode = mode;
        ApplyFitMode();
    }

    public void ApplyFitMode()
    {
        if (_fitMode == FitMode.None || _imageWidth <= 0 || _imageHeight <= 0)
            return;

        ImageScrollViewer.UpdateLayout();
        double availableWidth = ImageScrollViewer.ViewportWidth - SidePadding;
        double availableHeight = ImageScrollViewer.ViewportHeight - VerticalPadding;
        if (availableWidth <= 0)
            return;

        double zoom = availableWidth / _imageWidth;
        if (_fitMode == FitMode.FitPage && availableHeight > 0)
            zoom = Math.Min(zoom, availableHeight / _imageHeight);

        SetZoom(zoom);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_fitMode != FitMode.None)
            ApplyFitMode();
    }

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        double zoom = ImageScrollViewer.ZoomFactor;
        if (Math.Abs(zoom - _zoom) < 0.001)
            return;

        _zoom = zoom;
        ZoomChanged?.Invoke(this, _zoom);
    }

}

public sealed class ImageViewerFactory : IDocumentViewerFactory
{
    private static readonly IReadOnlyCollection<string> Extensions =
        new[] { ".bmp", ".gif", ".jpeg", ".jpg", ".png", ".tif", ".tiff" };

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public IDocumentViewer Create(ViewerViewModel viewModel) => new ImageViewer();
}
