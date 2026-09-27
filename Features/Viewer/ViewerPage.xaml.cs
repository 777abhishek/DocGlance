// ViewerPage.xaml.cs — shell, state, lifecycle, public API
// Split into partial files:
//   ViewerPage.Pdf.cs     — PDF loading + rendering
//   ViewerPage.Toolbar.cs — toolbar button handlers
//   ViewerPage.Sidebar.cs — sidebar tab switching + thumbnail click
//   ViewerPage.Zoom.cs    — zoom pill + fit-width / fit-page

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using QuickView.Models;
using QuickView.Rendering.Pdf;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace QuickView.Features.Viewer;

public sealed partial class ViewerPage : Page
{
    // ── Shared state (accessed by all partial files) ──────────────────────────
    internal DocumentItem?            _doc;
    internal PdfDocumentState?        _pdfState;
    internal double                   _zoom     = 1.0;
    internal int                      _page     = 1;
    internal int                      _total    = 1;
    internal double                   _rotation = 0;
    internal CancellationTokenSource? _cts;
    internal List<OutlineItem>        _outline  = new();

    // Unified view mode: true = Recent, false = All Documents
#pragma warning disable CS0169  // reserved for future recent/all toggle
    private bool _showingRecent;
#pragma warning restore CS0169

    public ObservableCollection<PageThumbnailItem> Thumbnails { get; } = new();

    // ── Construction ─────────────────────────────────────────────────────────
    public ViewerPage()
    {
        InitializeComponent();

        // Wire viewport-resize → reapply active fit mode.
        // Whenever the viewport width changes (window resize, sidebar toggle)
        // re-compute the zoom so the page still fills the available space.
        DocumentScrollViewer.SizeChanged += (_, _) => ReapplyFitMode();

        // Ctrl+Wheel zoom anchored at the cursor — handled on the ScrollViewer
        // so it intercepts the event before the ScrollViewer's own zoom logic
        // (which would raster-scale the content instead of re-rendering crisp).
        DocumentScrollViewer.PointerWheelChanged += OnViewerPointerWheelChanged;
    }

    // ── Navigation ───────────────────────────────────────────────────────────
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is DocumentItem doc)
            _ = LoadDocumentAsync(doc);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        CancelLoad();
    }

    // Called by TitleBarWindow when the tab is closed
    public void CancelLoad()
    {
        _cts?.Cancel();
        _prefetchCts?.Cancel();
        _reRenderCts?.Cancel();
        _searchCts?.Cancel();
        _pdfState?.Dispose();
        _pdfState = null;
    }

    // Public entry point called by TitleBarWindow.OpenDocument
    public void LoadDocument(DocumentItem doc) => _ = LoadDocumentAsync(doc);

    // ── Shared helpers ────────────────────────────────────────────────────────

    internal void ShowLoading(bool on)
    {
        LoadingRing.IsActive   = on;
        LoadingRing.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Public API (used by legacy MainWindow and TitleBarWindow) ─────────────
    public int           CurrentPage     => _page;
    public int           TotalPages      => _total;
    public double        CurrentZoom     => _zoom;
    public DocumentItem? CurrentDocument => _doc;

    public void NavigateToPage(int n)  => GoToPage(n);
    public void ZoomIn()               => SetZoom(_zoom * 1.25);
    public void ZoomOut()              => SetZoom(_zoom / 1.25);
    public void FitWidth()             => FitWidthButton_Click(this, new RoutedEventArgs());
    public void RotateClockwise()      => Rotate(+90);
    public void ShowSearch()           => FindButton_Click(this, new RoutedEventArgs());
}
