using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using DocGlance.Rendering.Pptx;

namespace DocGlance.Features.Viewer.Formats.Pptx;

public sealed partial class PptxViewer : UserControl, IDisposable
{
    private readonly PptxDocument _document;
    private int _currentSlideIndex;
    private CancellationTokenSource? _cts;

    public ObservableCollection<SlideItem> Slides { get; } = new();

    public PptxViewer(PptxDocument document)
    {
        _document = document;
        InitializeComponent();

        Loaded += PptxViewer_Loaded;
        KeyDown += PptxViewer_KeyDown;
    }

    private void PptxViewer_Loaded(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[PptxViewer] Loaded event fired. SlideCount: {_document.SlideCount}");
        
        _cts = new CancellationTokenSource();

        // Populate slide list
        for (int i = 0; i < _document.SlideCount; i++)
        {
            Slides.Add(new SlideItem
            {
                Index = i,
                Label = $"Slide {i + 1}"
            });
        }

        System.Diagnostics.Debug.WriteLine($"[PptxViewer] Added {Slides.Count} slides to collection");
        
        ThumbnailList.ItemsSource = Slides;

        // Load first slide at high quality (fire and forget)
        System.Diagnostics.Debug.WriteLine("[PptxViewer] Starting to load first slide...");
        _ = LoadSlideAsync(0);

        // Load thumbnails in background
        _ = LoadThumbnailsAsync(_cts.Token);
    }

    private async void ThumbnailList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SlideItem item)
        {
            await LoadSlideAsync(item.Index);
        }
    }

    private void PptxViewer_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Left && _currentSlideIndex > 0)
        {
            _ = LoadSlideAsync(_currentSlideIndex - 1);
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Right && _currentSlideIndex < _document.SlideCount - 1)
        {
            _ = LoadSlideAsync(_currentSlideIndex + 1);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Load and display slide at `index` at 2.0× scale (high quality).
    /// </summary>
    private async Task LoadSlideAsync(int index)
    {
        System.Diagnostics.Debug.WriteLine($"[PptxViewer] LoadSlideAsync called for slide {index}");
        
        if (_cts == null || _cts.IsCancellationRequested)
        {
            System.Diagnostics.Debug.WriteLine("[PptxViewer] LoadSlideAsync: CTS is null or cancelled");
            return;
        }

        LoadingRing.Visibility = Visibility.Visible;
        LoadingRing.IsActive = true;

        try
        {
            System.Diagnostics.Debug.WriteLine($"[PptxViewer] Rendering slide {index} at 2.0x scale...");
            // Render at 2.0× for crisp display on 1080p screens
            var bitmap = await _document.RenderSlideAsync(index, scale: 2.0, _cts.Token);
            
            System.Diagnostics.Debug.WriteLine($"[PptxViewer] Render result: {(bitmap != null ? $"{bitmap.PixelWidth}x{bitmap.PixelHeight}" : "NULL")}");
            
            if (bitmap == null)
            {
                System.Diagnostics.Debug.WriteLine("[PptxViewer] Render failed - bitmap is null");
                // Render failed — keep previous slide visible
                LoadingRing.Visibility = Visibility.Collapsed;
                return;
            }

            SlideImage.Source = bitmap;
            _currentSlideIndex = index;
            PageLabel.Text = $"Slide {index + 1} of {_document.SlideCount}";

            System.Diagnostics.Debug.WriteLine("[PptxViewer] Successfully set bitmap as SlideImage.Source");

            // Update selected thumbnail
            ThumbnailList.SelectedIndex = index;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PptxViewer] LoadSlideAsync exception: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[PptxViewer] Stack trace: {ex.StackTrace}");
        }
        finally
        {
            LoadingRing.Visibility = Visibility.Collapsed;
            LoadingRing.IsActive = false;
        }
    }

    /// <summary>
    /// Background task: render all slides as thumbnails at 0.3× scale.
    /// </summary>
    private async Task LoadThumbnailsAsync(CancellationToken ct)
    {
        for (int i = 0; i < _document.SlideCount; i++)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                // Render at 0.3× for small thumbnails (~200×110 px)
                var thumb = await _document.RenderSlideAsync(i, scale: 0.3, ct);
                if (thumb != null && i < Slides.Count)
                {
                    Slides[i].Thumbnail = thumb;
                }
            }
            catch
            {
                // Skip failed thumbnails
            }

            // Brief pause to not monopolize CPU
            await Task.Delay(50, ct);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _document?.Dispose();
    }
}

/// <summary>
/// Data model for one slide in the thumbnail list.
/// </summary>
public sealed class SlideItem
{
    public int Index { get; set; }
    public string Label { get; set; } = string.Empty;
    public WriteableBitmap? Thumbnail { get; set; }
}
