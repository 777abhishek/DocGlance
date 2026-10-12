// ViewerPage.xaml.cs — shell, state, lifecycle, public API
// Split into partial files:
//   ViewerPage.Pdf.cs     — PDF loading + rendering
//   ViewerPage.Toolbar.cs — toolbar button handlers
//   ViewerPage.Sidebar.cs — sidebar tab switching + thumbnail click
//   ViewerPage.Zoom.cs    — zoom pill + fit-width / fit-page

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using QuickView.Services;
using QuickView.Core.Models;
using QuickView.Rendering.Pdf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace QuickView.Features.Viewer;

public sealed partial class ViewerPage : Page
{
    // ── ViewModel ─────────────────────────────────────────────────────────────
    public ViewerViewModel ViewModel { get; } = new();

    // ── Shared state (accessed by all partial files) ──────────────────────────
    // These fields drive PDF rendering math. After every mutation call
    // SyncToViewModel() so XAML bindings (toolbar, title bar) stay in sync.
    internal DocumentItem?            _doc;
    internal PdfDocumentState?        _pdfState;
    internal double                   _zoom     = 1.0;
    internal int                      _page     = 1;
    internal int                      _total    = 1;
    internal double                   _rotation = 0;
    internal CancellationTokenSource? _cts;
    internal List<OutlineItem>        _outline  = new();

    // ── PERF-05: WebView2 disposal tracking ──────────────────────────────────
    // Track current renderer and element to ensure proper cleanup
    private Core.Interfaces.IFileRenderer? _currentRenderer;
    private Microsoft.UI.Xaml.UIElement? _currentRenderedElement;

    public ObservableCollection<PageThumbnailItem> Thumbnails { get; } = new();

    // ── UI-10: Resize debounce ───────────────────────────────────────────────
    private DispatcherTimer? _resizeDebounceTimer;

    // ── Construction ─────────────────────────────────────────────────────────
    public ViewerPage()
    {
        InitializeComponent();

        // Wire viewport-resize → reapply active fit mode + re-render (the
        // viewport may have grown — re-render pages.
        // UI-10 fix: debounce to avoid render storm during drag-resize
        DocumentScrollViewer.SizeChanged += (_, _) =>
        {
            if (_resizeDebounceTimer == null)
            {
                _resizeDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _resizeDebounceTimer.Tick += (_, _) =>
                {
                    _resizeDebounceTimer?.Stop();
                    ReapplyFitMode();
                    if (_pdfState != null) _ = RenderPagesAsync();
                };
            }
            _resizeDebounceTimer.Stop();
            _resizeDebounceTimer.Start();
        };

        // Ctrl+Wheel zoom anchored at the cursor.
        DocumentScrollViewer.PointerWheelChanged += OnViewerPointerWheelChanged;

        // ViewModel commands → ViewerPage actions
        ViewModel.PageChangeRequested  += (_, page)  => GoToPage(page);
        ViewModel.ZoomInRequested      += (_, _)     => SetZoom(_zoom * 1.25);
        ViewModel.ZoomOutRequested     += (_, _)     => SetZoom(_zoom / 1.25);
        ViewModel.ZoomResetRequested   += (_, _)     => SetZoom(NaturalZoom);
    }

    // ── ViewModel sync ────────────────────────────────────────────────────────

    /// <summary>
    /// Pushes all internal fields into the ViewModel so XAML bindings stay current.
    /// Call after any mutation to _page, _total, _zoom, _rotation, _doc, or sidebar state.
    /// </summary>
    internal void SyncToViewModel()
    {
        ViewModel.CurrentPage  = _page;
        ViewModel.TotalPages   = _total;
        ViewModel.Zoom         = _zoom;
        ViewModel.NaturalZoom  = NaturalZoom;
        ViewModel.Rotation     = _rotation;
        ViewModel.Document     = _doc;
        ViewModel.SidebarOpen  = SidebarCard?.Visibility == Visibility.Visible;
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
        CleanupViewportRendering();
        CancelLoad();
    }

    // Called by TitleBarWindow when the tab is closed
    public void CancelLoad()
    {
        _cts?.Cancel();

        _searchCts?.Cancel();
        
        // PERF-05: Clean up WebView2 and other resources
        if (_currentRenderer != null && _currentRenderedElement != null)
        {
            try
            {
                _ = _currentRenderer.CleanupAsync(_currentRenderedElement);
            }
            catch
            {
                // Swallow cleanup exceptions (already shutting down)
            }
            _currentRenderer = null;
            _currentRenderedElement = null;
        }

        // Clean up spreadsheet viewer
        try { SpreadsheetViewerControl?.Cleanup(); } catch { }
        
        // Service owns document lifecycle — just clear our reference
        _pdfState = null;
        ViewModel.Reset();
    }

    // Public entry point called by TitleBarWindow.OpenDocument
    public void LoadDocument(DocumentItem doc) => _ = LoadDocumentAsync(doc);

    // ── Shared helpers ────────────────────────────────────────────────────────

    // UI-04 fix: animate Opacity instead of toggling Visibility for smooth fade
    internal void ShowLoading(bool on)
    {
        LoadingRing.IsActive   = on;
        LoadingRing.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        LoadingOverlay.Opacity = on ? 1.0 : 0.0;
        LoadingOverlay.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ViewModel.IsLoading    = on;
    }

    // ── Public API (used by TitleBarWindow) ───────────────────────────────────
    public int           CurrentPage     => _page;
    public int           TotalPages      => _total;
    public double        CurrentZoom     => _zoom;
    public DocumentItem? CurrentDocument => _doc;

    public void NavigateToPage(int n)  => GoToPage(n);
    public void ZoomIn()               => SetZoom(_zoom * 1.25);
    public void ZoomOut()              => SetZoom(_zoom / 1.25);
    public void FitWidth()             => Toolbar_FitWidthRequested(this, EventArgs.Empty);
    public void RotateClockwise()      => Rotate(+90);
    public void ShowSearch()           => Toolbar_FindRequested(this, EventArgs.Empty);
}
