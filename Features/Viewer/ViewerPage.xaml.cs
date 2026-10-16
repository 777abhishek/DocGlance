// ViewerPage.xaml.cs — shell, state, lifecycle, public API
// Split into partial files:
//   ViewerPage.Pdf.cs     — PDF loading + rendering
//   ViewerPage.Toolbar.cs — toolbar button handlers
//   ViewerPage.Sidebar.cs — sidebar tab switching + thumbnail click
//   ViewerPage.Zoom.cs    — zoom pill + fit-width / fit-page

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using DocGlance.Services;
using DocGlance.Core.Models;
using DocGlance.Core.Enums;
using DocGlance.Rendering.Pdf;
using DocGlance.Rendering.Pdf.Models;
using DocGlance.Rendering.Shared;
using DocGlance.Rendering.Xlsx;
using DocGlance.Rendering.Docx;
using DocGlance.Rendering.Pptx;
using DocGlance.Features.Viewer.Formats.Text;
using DocGlance.Infrastructure.Logging;
using DocGlance.Features.Viewer.Contracts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using DocGlance.Features.Viewer.Formats.Fallback;
using DocGlance.Features.Viewer.Formats.Pdf;
using DocGlance.Features.Viewer.Formats.Docx;
using DocGlance.Features.Viewer.Formats.Pptx;
using DocGlance.Features.Viewer.Formats.Spreadsheet;

namespace DocGlance.Features.Viewer;

public sealed partial class ViewerPage : Page
{
    private readonly DocumentViewerRegistry _viewerRegistry = new(
        new IDocumentViewerFactory[]
        {
            new PdfViewerFactory(),
            new DocxViewerFactory(),
            new PptxViewerFactory(),
            new TextViewerFactory(),
            new SpreadsheetViewerFactory(),
        },
        new FallbackViewerFactory());
    private PdfViewer? _pdfViewerControl;
    private PdfViewer PdfViewerControl =>
        _pdfViewerControl ?? throw new InvalidOperationException("The PDF viewer has not been initialized.");
    private DocxViewer? _docxViewerControl;
    private DocxViewer DocxViewerControl =>
        _docxViewerControl ?? throw new InvalidOperationException("The DOCX viewer has not been initialized.");
    private DocxDocument? _docxDocument => _docxViewerControl?.Document;
    private PptxViewer? _pptxViewerControl;
    private PptxViewer PptxViewerControl =>
        _pptxViewerControl ?? throw new InvalidOperationException("The PPTX viewer has not been initialized.");
    private PptxDocument? _pptxDocument => _pptxViewerControl?.Document;
    private TextViewer TextViewerControl { get; set; } = null!;
    private SpreadsheetViewer SpreadsheetViewerControl { get; set; } = null!;
    private SpreadsheetDocument? _spreadsheetDocument => SpreadsheetViewerControl?.Document;
    private FallbackViewer? _fallbackViewer;
    private IDocumentViewer? _activeViewer;
    private ScrollViewer DocumentScrollViewer =>
        _activeViewer is DocxViewer docxViewer
            ? docxViewer.ScrollViewer
            : _activeViewer is PptxViewer pptxViewer
                ? pptxViewer.ScrollViewer
            : PdfViewerControl.DocumentScrollViewerControl;
    private Canvas PdfViewportCanvas =>
        _activeViewer is DocxViewer docxViewer
            ? docxViewer.Canvas
            : _activeViewer is PptxViewer pptxViewer
                ? pptxViewer.Canvas
            : PdfViewerControl.PdfViewportCanvasControl;
    private Image ViewportImage => PdfViewerControl.ViewportImageControl;
    private Canvas PdfAnnotationCanvas => PdfViewerControl.PdfAnnotationCanvasControl;
    private Canvas PdfSelectionCanvas => PdfViewerControl.PdfSelectionCanvasControl;

    // ── ViewModel ─────────────────────────────────────────────────────────────
    public ViewerViewModel ViewModel { get; } = new();

    // ── Shared state (accessed by all partial files) ──────────────────────────
    // These fields drive PDF rendering math. After every mutation call
    // SyncToViewModel() so XAML bindings (toolbar, title bar) stay in sync.
    internal DocumentItem?            _doc;
    internal PdfDocumentState? _pdfState
    {
        get => PdfViewerControl.DocumentState;
        set => PdfViewerControl.DocumentState = value;
    }
    internal double                   _zoom     = 1.0;
    internal int                      _page     = 1;
    internal int                      _total    = 1;
    internal double                   _rotation = 0;
    internal CancellationTokenSource? _cts;
    internal List<OutlineItem>        _outline  = new();
    internal PdfViewportState? _viewportState
    {
        get => PdfViewerControl.ViewportState;
        set => PdfViewerControl.ViewportState = value;
    }
    internal const double PtToLp = 96.0 / 72.0;
    internal const double LpToPt = 72.0 / 96.0;

    private void InitializeViewportRendering() => PdfViewerControl.InitializeViewportRendering();
    private void UpdateCanvasSize() => PdfViewerControl.UpdateCanvasSize();
    private Task RenderPagesAsync() => PdfViewerControl.RenderPagesAsync();
    private (double W, double H) GetRotatedPageSize(int pageIndex) =>
        PdfViewerControl.GetRotatedPageSize(pageIndex);
    private double GetPageTopLp(int pageIndex) => PdfViewerControl.GetPageTopLp(pageIndex);
    private double GetDocxPageTopLp(int pageIndex) => DocxViewerControl.GetPageTopLp(pageIndex);
    private double GetPptxPageTopLp(int pageIndex) => PptxViewerControl.GetSlideTopLp(pageIndex);

    public ObservableCollection<PageThumbnailItem> Thumbnails { get; } = new();

    // ── UI-10: Resize debounce ───────────────────────────────────────────────
    private DispatcherTimer? _resizeDebounceTimer;

    // ── Construction ─────────────────────────────────────────────────────────
    public ViewerPage()
    {
        InitializeComponent();
        _pdfViewerControl = (PdfViewer)_viewerRegistry.Resolve(".pdf").Create(ViewModel);
        PdfViewerHost.Content = _pdfViewerControl;
        _docxViewerControl = (DocxViewer)_viewerRegistry.Resolve(".docx").Create(ViewModel);
        DocxViewerHost.Content = _docxViewerControl;
        _pptxViewerControl = (PptxViewer)_viewerRegistry.Resolve(".pptx").Create(ViewModel);
        PptxViewerHost.Content = _pptxViewerControl;
        PdfViewerControl.ZoomChanged += (_, zoom) =>
        {
            _zoom = zoom;
            ZoomLabel.Text = $"{Math.Round(_zoom / NaturalZoom * 100.0)}%";
            ViewModel.Zoom = _zoom;
        };
        PdfViewerControl.PageNavigationRequested += (_, page) => UpdateCurrentPage(page);
        PdfViewerControl.OutlineNavigationRequested += (_, page) => GoToPage(page);
        PdfViewerControl.AnnotationsChanged += (_, _) => UpdatePdfNoteList();
        PdfViewerControl.PageChanged += (_, page) => UpdateCurrentPage(page);
        PdfViewerControl.TextPageMapLoaded += (_, pageIndex) =>
        {
            TryStartPendingMouseSelection(pageIndex);
            TryStartTouchSelection();
        };
        PdfViewerControl.ClearTextSelectionRequested += (_, _) => ClearPdfTextSelectionState();
        DocxViewerControl.PageChanged += (_, page) => UpdateCurrentPage(page);
        DocxViewerControl.ZoomChanged += (_, zoom) =>
        {
            _zoom = zoom;
            ZoomLabel.Text = $"{Math.Round(_zoom / NaturalZoom * 100.0)}%";
            SyncToViewModel();
        };
        PptxViewerControl.PageChanged += (_, page) => UpdateCurrentPage(page);
        PptxViewerControl.ZoomChanged += (_, zoom) =>
        {
            _zoom = zoom;
            ZoomLabel.Text = $"{Math.Round(_zoom / NaturalZoom * 100.0)}%";
            SyncToViewModel();
        };
        TextViewerControl = (TextViewer)_viewerRegistry.Resolve(".txt").Create(ViewModel);
        TextViewerHost.Content = TextViewerControl;
        SpreadsheetViewerControl = (SpreadsheetViewer)_viewerRegistry.Resolve(".xlsx").Create(ViewModel);
        SpreadsheetViewerHost.Content = SpreadsheetViewerControl;
        InitializeViewerShortcuts();
        Toolbar.PdfToolSelected += Toolbar_PdfToolSelected;

        InitializePdfTextSelection();

        // Initialize Office text selection (drag-to-select and copy)
        InitializeOfficeTextSelection();
        TextViewerControl.TextBox.RightTapped += PlainTextBlock_RightTapped;
        SpreadsheetViewerControl.TranslateRequested += text => _ = ShowTranslationDialogAsync(text);

        // Wire viewport-resize → reapply active fit mode + re-render (the
        // viewport may have grown — re-render pages.
        // UI-10 fix: debounce to avoid render storm during drag-resize
        DocumentScrollViewer.SizeChanged += (_, _) =>
        {
            ScheduleFitModeReapply();
        };
        DocxViewerControl.SizeChanged += (_, _) => ScheduleFitModeReapply();
        DocxViewerControl.ScrollViewer.PointerWheelChanged += OnViewerPointerWheelChanged;
        PptxViewerControl.SizeChanged += (_, _) => ScheduleFitModeReapply();
        PptxViewerControl.ScrollViewer.PointerWheelChanged += OnViewerPointerWheelChanged;
        PdfViewerControl.SizeChanged += (_, _) => ScheduleFitModeReapply();
        SpreadsheetViewerControl.SizeChanged += (_, _) => ScheduleFitModeReapply();
        SpreadsheetViewerControl.ActiveSheetChanged += (_, sheetIndex) =>
        {
            if (_spreadsheetDocument == null) return;
            UpdateCurrentPage(sheetIndex + 1);
            ReapplyFitMode();
        };
        SpreadsheetViewerControl.ZoomChanged += (_, zoom) =>
        {
            _zoom = zoom;
            ZoomLabel.Text = $"{Math.Round(_zoom * 100)}%";
            SyncToViewModel();
        };
        SpreadsheetViewerControl.SelectionError += (_, message) =>
            ShowInfoBar($"Could not copy selected cells: {message}", InfoBarSeverity.Error);
        SpreadsheetViewerControl.ManualZoomRequested += (_, _) => SetFitMode(FitMode.None);

        // Ctrl+Wheel zoom anchored at the cursor.
        DocumentScrollViewer.PointerWheelChanged += OnViewerPointerWheelChanged;

        // ViewModel commands → ViewerPage actions
        ViewModel.PageChangeRequested  += (_, page)  => GoToPage(page);
        ViewModel.ZoomInRequested      += (_, _)     => { SetFitMode(FitMode.None); SetZoom(_zoom * 1.25); };
        ViewModel.ZoomOutRequested     += (_, _)     => { SetFitMode(FitMode.None); SetZoom(_zoom / 1.25); };
        ViewModel.ZoomResetRequested   += (_, _)     => { SetFitMode(FitMode.None); SetZoom(NaturalZoom); };

        // Subscribe to settings changes for live updates
        Infrastructure.Configuration.AppSettings.SettingsChanged += OnSettingsChanged;
        
        // Unsubscribe when page unloads
        Unloaded += (_, _) => Infrastructure.Configuration.AppSettings.SettingsChanged -= OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        // Apply settings changes immediately to the current viewer
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyViewerSettings();
        });
    }

    private void ApplyViewerSettings()
    {
        // Apply show thumbnails setting via the ThumbnailList
        if (ThumbnailList != null)
        {
            ThumbnailList.Visibility = Infrastructure.Configuration.AppSettings.ShowThumbnails 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }
        
        if (SidebarCard == null)
            return;

        // This runs for EVERY AppSettings change (including ones raised just by opening the
        // Settings page) and on every open viewer tab. Only documents that have a sidebar
        // (multi-page PDF, DOCX, PPTX) may have it opened; spreadsheets, text, Markdown and
        // error/unsupported views must never get it back.
        bool supportsSidebar =
            Toolbar.SidebarButtonVisibility == Visibility.Visible &&
            !IsSpreadsheetViewerActive() &&
            _total > 1;

        if (Infrastructure.Configuration.AppSettings.ShowSidebar)
        {
            if (supportsSidebar && SidebarColumn.Width.Value == 0)
            {
                SidebarColumn.Width = new GridLength(240);
                SidebarCard.Visibility = Visibility.Visible;
                Toolbar.SidebarIsChecked = true;
                ViewModel.SidebarOpen = true;
            }
        }
        else
        {
            SidebarColumn.Width = new GridLength(0);
            SidebarCard.Visibility = Visibility.Collapsed;
            Toolbar.SidebarIsChecked = false;
            ViewModel.SidebarOpen = false;
        }

        ScheduleFitModeReapply();
    }

    private void ScheduleFitModeReapply()
    {
        if (_resizeDebounceTimer == null)
        {
            _resizeDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _resizeDebounceTimer.Tick += (_, _) =>
            {
                _resizeDebounceTimer?.Stop();
                PdfViewerControl.UpdateLayout();
                DocumentScrollViewer.UpdateLayout();
                ReapplyFitMode();
                if (_pdfState != null) RenderPagesAsync().SafeFireAndForget("PdfViewportRender");
            };
        }

        _resizeDebounceTimer.Stop();
        _resizeDebounceTimer.Start();
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
            LoadDocumentAsync(doc).SafeFireAndForget("ViewerOnNavigatedTo");
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

        _searchCts?.Cancel();
        _searchDebounceCts?.Cancel();

        // Release every native document this page owns. Without this, closing a tab
        // leaked the Rust-side document (whole file buffer + renderer + font caches)
        // until GC — the reported "memory grows when opening documents" symptom.
        _ = TextViewerControl.CleanupAsync();
        _ = PptxViewerControl.CleanupAsync();
        _ = DocxViewerControl.CleanupAsync();
        _pptxTextMaps.Clear();
        ClearOfficeSelection();
        _ = SpreadsheetViewerControl?.CleanupAsync();
        if (_fallbackViewer != null)
        {
            _ = _fallbackViewer.CleanupAsync();
            _fallbackViewer = null;
        }

        // The per-page service owns the PDF document — closing it frees the native handle.
        // A fresh service keeps the page usable if it is reattached later.
        _ = PdfViewerControl.CleanupAsync();
        ViewModel.Reset();
    }

    // Public entry point called by TitleBarWindow.OpenDocument
    public void LoadDocument(DocumentItem doc) => LoadDocumentAsync(doc).SafeFireAndForget("ViewerLoadDocument");

    // ── Shared helpers ────────────────────────────────────────────────────────

    // UI-04 fix: animate Opacity instead of toggling Visibility for smooth fade
    internal void ShowLoading(bool on)
    {
        LoadingOverlay.Opacity = on ? 1.0 : 0.0;
        LoadingOverlay.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ViewModel.IsLoading = on;
    }

    private async Task LoadTextFileAsync(DocumentItem document, CancellationToken cancellationToken)
    {
        ShowLoading(true);
        try
        {
            await TextViewerControl.LoadAsync(document, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            _activeViewer = TextViewerControl;
            TextViewerHost.Visibility = Visibility.Visible;
            PdfViewerHost.Visibility = Visibility.Collapsed;
            DocxViewerHost.Visibility = Visibility.Collapsed;
            PptxViewerHost.Visibility = Visibility.Collapsed;
            DocumentScrollViewer.Visibility = Visibility.Collapsed;
            SpreadsheetViewerHost.Visibility = Visibility.Collapsed;
            UnsupportedFileMessage.Visibility = Visibility.Collapsed;
            ErrorMessage.Visibility = Visibility.Collapsed;
            Toolbar.WordWrapVisibility = Visibility.Visible;
            Toolbar.RotationButtonsVisibility = Visibility.Collapsed;
            Toolbar.SidebarButtonVisibility = Visibility.Collapsed;
            Toolbar.FitWidthButtonVisibility = Visibility.Collapsed;
            Toolbar.FitPageButtonVisibility = Visibility.Collapsed;
            Toolbar.IsWordWrapChecked = false;
            SidebarCard.Visibility = Visibility.Collapsed;
            SidebarColumn.Width = new GridLength(0);
            ViewModel.SidebarOpen = false;
            _total = 1;
            _page = 1;
            Toolbar.UpdatePageInfo(1, 1);
        }
        finally
        {
            ShowLoading(false);
        }
    }

    internal void ShowPdfViewer()
    {
        _activeViewer = PdfViewerControl;
        PdfViewerHost.Visibility = Visibility.Visible;
        DocxViewerHost.Visibility = Visibility.Collapsed;
        PptxViewerHost.Visibility = Visibility.Collapsed;
        TextViewerHost.Visibility = Visibility.Collapsed;
        DocumentScrollViewer.Visibility = Visibility.Visible;
        SpreadsheetViewerHost.Visibility = Visibility.Collapsed;
        UnsupportedFileMessage.Visibility = Visibility.Collapsed;
        ErrorMessage.Visibility = Visibility.Collapsed;
        Toolbar.WordWrapVisibility = Visibility.Collapsed;
        Toolbar.RotationButtonsVisibility = Visibility.Visible;
        Toolbar.SidebarButtonVisibility = Visibility.Visible;
        Toolbar.FitWidthButtonVisibility = Visibility.Visible;
        Toolbar.FitPageButtonVisibility = Visibility.Visible;
    }

    internal void ShowDocxViewer()
    {
        _activeViewer = DocxViewerControl;
        PdfViewerHost.Visibility = Visibility.Collapsed;
        DocxViewerHost.Visibility = Visibility.Visible;
        PptxViewerHost.Visibility = Visibility.Collapsed;
        TextViewerHost.Visibility = Visibility.Collapsed;
        SpreadsheetViewerHost.Visibility = Visibility.Collapsed;
        UnsupportedFileMessage.Visibility = Visibility.Collapsed;
        ErrorMessage.Visibility = Visibility.Collapsed;
        Toolbar.WordWrapVisibility = Visibility.Collapsed;
        Toolbar.RotationButtonsVisibility = Visibility.Visible;
        Toolbar.SidebarButtonVisibility = Visibility.Visible;
        Toolbar.FitWidthButtonVisibility = Visibility.Visible;
        Toolbar.FitPageButtonVisibility = Visibility.Visible;
    }

    internal void ShowPptxViewer()
    {
        _activeViewer = PptxViewerControl;
        PdfViewerHost.Visibility = Visibility.Collapsed;
        DocxViewerHost.Visibility = Visibility.Collapsed;
        PptxViewerHost.Visibility = Visibility.Visible;
        TextViewerHost.Visibility = Visibility.Collapsed;
        SpreadsheetViewerHost.Visibility = Visibility.Collapsed;
        UnsupportedFileMessage.Visibility = Visibility.Collapsed;
        ErrorMessage.Visibility = Visibility.Collapsed;
        Toolbar.WordWrapVisibility = Visibility.Collapsed;
        Toolbar.RotationButtonsVisibility = Visibility.Visible;
        Toolbar.SidebarButtonVisibility = Visibility.Visible;
        Toolbar.FitWidthButtonVisibility = Visibility.Visible;
        Toolbar.FitPageButtonVisibility = Visibility.Visible;
    }

    internal void ShowSpreadsheetViewer()
    {
        _activeViewer = SpreadsheetViewerControl;
        PdfViewerHost.Visibility = Visibility.Collapsed;
        DocxViewerHost.Visibility = Visibility.Collapsed;
        PptxViewerHost.Visibility = Visibility.Collapsed;
        TextViewerHost.Visibility = Visibility.Collapsed;
        DocumentScrollViewer.Visibility = Visibility.Collapsed;
        SpreadsheetViewerHost.Visibility = Visibility.Visible;
        UnsupportedFileMessage.Visibility = Visibility.Collapsed;
        ErrorMessage.Visibility = Visibility.Collapsed;
        Toolbar.WordWrapVisibility = Visibility.Collapsed;
        Toolbar.RotationButtonsVisibility = Visibility.Collapsed;
        Toolbar.SidebarButtonVisibility = Visibility.Collapsed;
        Toolbar.FitWidthButtonVisibility = Visibility.Visible;
        Toolbar.FitPageButtonVisibility = Visibility.Visible;
        SidebarCard.Visibility = Visibility.Collapsed;
        SidebarColumn.Width = new GridLength(0);
        ViewModel.SidebarOpen = false;
    }

    private void WordWrapButton_Click(object? sender, EventArgs e)
    {
        TextViewerControl.TextBox.TextWrapping = Toolbar.IsWordWrapChecked
            ? TextWrapping.Wrap
            : TextWrapping.NoWrap;
    }

    private void ShowInfoBar(string message, InfoBarSeverity severity) =>
        Logger.Log($"[InfoBar] {severity}: {message}");

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
