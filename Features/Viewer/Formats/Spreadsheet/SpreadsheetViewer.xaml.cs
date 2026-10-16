// SpreadsheetViewer.xaml.cs
// High-performance Win2D spreadsheet cell grid renderer for DocGlance.
// Win2D CanvasControl instances are created programmatically to avoid WMC9999
// XAML compiler errors with WinAppSDK 2.x.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using DocGlance.Infrastructure.Logging;
using DocGlance.Rendering.Xlsx;
using DocGlance.Core.Models;
using DocGlance.Core.Enums;
using DocGlance.Features.Viewer.Contracts;
using Windows.Foundation;
using Windows.UI;

namespace DocGlance.Features.Viewer.Formats.Spreadsheet;

/// <summary>
/// Win2D-powered spreadsheet viewer — renders cell grids natively without sidebar.
/// All Win2D CanvasControl instances are created in code to avoid WMC9999 issues.
/// </summary>
public sealed partial class SpreadsheetViewer : UserControl,
    IDocumentViewer, ISupportsZoom, ISupportsSearch, ISupportsTextSelection
{
    // ── Win2D controls (created in code) ─────────────────────────────────────
    private CanvasControl? _cellCanvas;
    private CanvasControl? _colHeaderCanvas;
    private CanvasControl? _rowHeaderCanvas;

    // ── Theme colours ────────────────────────────────────────────────────────
    private Color _bgGrid      = Color.FromArgb(255, 255, 255, 255);
    private Color _bgHeader    = Color.FromArgb(255, 243, 243, 243);
    private Color _gridLine    = Color.FromArgb(255, 217, 217, 217);
    private Color _headerText  = Color.FromArgb(255, 80,  80,  80 );
    private Color _cellText    = Color.FromArgb(255, 32,  32,  32 );
    private Color _selection   = Color.FromArgb(60,  0,   120, 215);
    private Color _headerSel   = Color.FromArgb(255, 210, 230, 255);
    private Color _tabActive   = Color.FromArgb(255, 255, 255, 255);
    private Color _tabInactive = Color.FromArgb(255, 237, 237, 237);
    private Color _tabBorder   = Color.FromArgb(255, 196, 196, 196);
    private Color _tabText     = Color.FromArgb(255, 60,  60,  60 );

    // ── State ─────────────────────────────────────────────────────────────────
    private SpreadsheetDocument? _doc;
    private int _sheetLoadGeneration;
    private SheetData?           _data;
    private Dictionary<(int, int), CellData>? _cellMap;
    private int _activeSheet = 0;
    private int _selectedRow = -1;
    private int _selectedCol = -1;
    private int _selectionAnchorRow = -1;
    private int _selectionAnchorCol = -1;
    private int _selectionFocusRow = -1;
    private int _selectionFocusCol = -1;
    private bool _isSelectingRange;
    private uint _selectionPointerId;
    private int _activeSearchRow = -1;
    private int _activeSearchCol = -1;
    private IReadOnlyList<SpreadsheetMatch> _allSearchMatches = Array.Empty<SpreadsheetMatch>();
    private int _activeSearchIndex = -1;
    private HashSet<(int Row, int Col)> _searchCells = new();
    private double _zoom = 1.0;
    private FitMode _fitMode;
    private IReadOnlyList<SpreadsheetMatch> _viewerSearchResults = Array.Empty<SpreadsheetMatch>();

    // Cumulative column/row pixel offsets
    private double[] _colX = Array.Empty<double>();
    private double[] _rowY = Array.Empty<double>();
    private double _totalWidth;
    private double _totalHeight;

    // Merge index
    private Dictionary<(int, int), MergeData> _mergeMap = new();
    private HashSet<(int, int)> _covered = new();

    public SpreadsheetViewer()
    {
        InitializeComponent();
        CreateCanvasControls();
        ActualThemeChanged += OnThemeChanged;
        UpdateTheme();
        Loaded += OnLoaded;
    }

    public event EventHandler<int>? ActiveSheetChanged;
    public event EventHandler<double>? ZoomChanged;
    public event EventHandler? SelectionChanged;
    public event EventHandler? ManualZoomRequested;
    public event EventHandler<string>? SelectionError;
    public event Action<string>? TranslateRequested;

    public double Zoom => _zoom;
    public double CurrentZoom => _zoom;
    public FitMode CurrentFitMode => _fitMode;
    public int ActiveSheetIndex => _activeSheet;
    public SpreadsheetDocument? Document => _doc;
    public IReadOnlyList<SpreadsheetMatch> SearchMatches => _viewerSearchResults;
    public FrameworkElement Control => this;
    public double SheetWidth => _totalWidth;

    public void ResetScrollPosition()
    {
        CellScroll.ChangeView(0, 0, null, disableAnimation: true);
        ColHeaderScroll.ChangeView(0, 0, null, disableAnimation: true);
        RowHeaderScroll.ChangeView(0, 0, null, disableAnimation: true);
    }
    public double SheetHeight => _totalHeight;
    public double ViewportWidth => CellScroll.ViewportWidth;
    public double ViewportHeight => CellScroll.ViewportHeight;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateTheme();
        CreateCanvasControls();
        ApplyCanvasSizes();
        Invalidate();
    }

    private void CreateCanvasControls()
    {
        if (_cellCanvas != null) return;

        // Canvases are VIEWPORT-SIZED overlays: the hosts inside the ScrollViewers keep the
        // full sheet size for scroll extents, but the actual Win2D surfaces only cover the
        // visible area. A full-sheet surface exceeds the GPU's max texture size (16384 px)
        // for large sheets → CanvasImageSource allocation failure → app crash (and, when the
        // exception was previously swallowed, the transparent/blank grid symptom).
        // Cell selection moved to CellScroll.PointerPressed (the overlay is hit-test-invisible).

        // Cell canvas
        _cellCanvas = new CanvasControl { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        _cellCanvas.Draw += CellCanvas_Draw;
        _cellCanvas.IsHitTestVisible = false;
        CellOverlay.Children.Add(_cellCanvas);
        CellScroll.PointerPressed += CellCanvas_PointerPressed;
        CellScroll.PointerMoved += CellCanvas_PointerMoved;
        CellScroll.PointerReleased += CellCanvas_PointerReleased;
        CellScroll.PointerCaptureLost += CellCanvas_PointerCaptureLost;
        CellScroll.RightTapped += CellScroll_RightTapped;
        CellScroll.PointerWheelChanged += CellScroll_PointerWheelChanged;
        CellScroll.SizeChanged += (_, _) => ResizeCanvases();
        var copyAccelerator = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.C,
            Modifiers = Windows.System.VirtualKeyModifiers.Control,
        };
        copyAccelerator.Invoked += (_, args) =>
        {
            CopySelectionToClipboard();
            args.Handled = true;
        };
        CellScroll.KeyboardAccelerators.Add(copyAccelerator);

        // Column header canvas
        _colHeaderCanvas = new CanvasControl { Height = 26, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        _colHeaderCanvas.Draw += ColHeaderCanvas_Draw;
        _colHeaderCanvas.IsHitTestVisible = false;
        ColHeaderOverlay.Children.Add(_colHeaderCanvas);

        // Row header canvas
        _rowHeaderCanvas = new CanvasControl { Width = 50, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        _rowHeaderCanvas.Draw += RowHeaderCanvas_Draw;
        _rowHeaderCanvas.IsHitTestVisible = false;
        RowHeaderOverlay.Children.Add(_rowHeaderCanvas);

        ResizeCanvases();
        ApplyCanvasSizes();
    }

    /// <summary>
    /// Sizes the overlay canvases to the ScrollViewer's viewport (the visible area),
    /// clamped so size × display scale never exceeds the GPU max surface size.
    /// Called on load, layout changes and view changes; cheap no-op when unchanged.
    /// </summary>
    private void ResizeCanvases()
    {
        double vw = Math.Max(1, CellScroll.ViewportWidth);
        double vh = Math.Max(1, CellScroll.ViewportHeight);

        double dpiScale = XamlRoot?.RasterizationScale ?? 1.0;
        _maxSurfaceDip ??= GetMaxSurfaceDip(dpiScale);
        double maxDip = _maxSurfaceDip.Value;
        vw = Math.Min(vw, maxDip);
        vh = Math.Min(vh, maxDip);

        if (_cellCanvas != null)
        {
            _cellCanvas.Width  = vw;
            _cellCanvas.Height = vh;
        }
        if (_colHeaderCanvas != null)
            _colHeaderCanvas.Width = vw;
        if (_rowHeaderCanvas != null)
            _rowHeaderCanvas.Height = vh;
    }

    private double? _maxSurfaceDip;

    private double GetMaxSurfaceDip(double dpiScale)
    {
        const double FallbackMaxPx = 16384;
        double maxPx = FallbackMaxPx;
        try
        {
            maxPx = Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice().MaximumBitmapSizeInPixels;
        }
        catch
        {
            // Device not ready yet — the documented D2D limit is a safe default.
        }
        return maxPx / Math.Max(0.01, dpiScale);
    }

    private void ApplyCanvasSizes()
    {
        double width = _totalWidth * _zoom;
        double height = _totalHeight * _zoom;

        ColumnHeaderRow.Height = new GridLength(26 * _zoom);
        RowHeaderColumn.Width = new GridLength(50 * _zoom);

        // Hosts inside the ScrollViewers define scroll extents — full sheet size.
        CellCanvasHost.Width  = width;
        CellCanvasHost.Height = height;
        ColHeaderCanvasHost.Width  = width;
        ColHeaderCanvasHost.Height = 26 * _zoom;
        RowHeaderCanvasHost.Width = 50 * _zoom;
        RowHeaderCanvasHost.Height = height;

        // Short-axis canvas dims track the zoomed header size; the long axis is
        // viewport-clamped by ResizeCanvases.
        if (_colHeaderCanvas != null)
            _colHeaderCanvas.Height = 26 * _zoom;
        if (_rowHeaderCanvas != null)
            _rowHeaderCanvas.Width = 50 * _zoom;

        ResizeCanvases();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task LoadDocumentAsync(SpreadsheetDocument doc, CancellationToken ct = default)
    {
        _doc = doc;
        _activeSheet = 0;
        _zoom = 1.0;
        CellScroll.ChangeView(0, 0, 1.0f, disableAnimation: true);
        SyncHeaderZoomAndOffsets();
        RebuildTabBar();
        await LoadSheetAsync(0, ct);
        if (!ReferenceEquals(_doc, doc) || _activeSheet != 0) return;
        ActiveSheetChanged?.Invoke(this, _activeSheet);
    }

    public async Task LoadAsync(DocumentItem document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        string filePath = document.FilePath
            ?? throw new ArgumentException("The document has no file path.", nameof(document));

        await CleanupAsync();
        var openedDocument = await SpreadsheetDocument.OpenAsync(filePath, cancellationToken);
        if (openedDocument == null)
            throw new InvalidOperationException("Cannot open spreadsheet — file may be corrupt or unsupported.");

        try
        {
            await LoadDocumentAsync(openedDocument, cancellationToken);
        }
        catch
        {
            Cleanup();
            openedDocument.Dispose();
            throw;
        }
    }

    public Task CleanupAsync()
    {
        var document = _doc;
        Cleanup();
        document?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Selects a specific cell and switches to its sheet if necessary.
    /// Used by search functionality to highlight search results.
    /// </summary>
    public async void SelectCell(int sheetIndex, int row, int col)
    {
        // Switch to the sheet if it's different from the active one
        if (sheetIndex != _activeSheet && sheetIndex >= 0 && _doc != null && sheetIndex < _doc.SheetCount)
        {
            _activeSheet = sheetIndex;
            UpdateTabStyles();
            await LoadSheetAsync(sheetIndex);
            if (_activeSheet != sheetIndex) return;
            ActiveSheetChanged?.Invoke(this, _activeSheet);
        }

        // Select the cell
        SetCellSelection(row, col, row, col);

        // Scroll to make the cell visible (with some padding)
        if (_colX.Length > col + 1 && _rowY.Length > row + 1)
        {
            double cellX = _colX[col] * _zoom;
            double cellY = _rowY[row] * _zoom;
            double cellW = (_colX[col + 1] - _colX[col]) * _zoom;
            double cellH = (_rowY[row + 1] - _rowY[row]) * _zoom;

            // Calculate scroll position to center the cell
            double vpW = CellScroll.ViewportWidth > 0 ? CellScroll.ViewportWidth : 1200;
            double vpH = CellScroll.ViewportHeight > 0 ? CellScroll.ViewportHeight : 800;

            double scrollX = Math.Max(0, cellX - (vpW - cellW) / 2);
            double scrollY = Math.Max(0, cellY - (vpH - cellH) / 2);

            CellScroll.ChangeView(scrollX, scrollY, null, disableAnimation: false);
        }

        Invalidate();
    }

    public async Task SelectSheetAsync(int sheetIndex, CancellationToken ct = default)
    {
        if (_doc == null || sheetIndex < 0 || sheetIndex >= _doc.SheetCount)
            return;
        if (sheetIndex == _activeSheet) return;

        _activeSheet = sheetIndex;
        UpdateTabStyles();
        await LoadSheetAsync(sheetIndex, ct);
        if (_activeSheet != sheetIndex) return;
        ActiveSheetChanged?.Invoke(this, _activeSheet);
    }

    public void SetZoom(double zoom)
    {
        double nextZoom = Math.Clamp(zoom, 0.1, 6.0);
        if (Math.Abs(nextZoom - _zoom) < 0.001) return;

        double oldZoom = Math.Max(0.01, _zoom);
        double anchorX = (CellScroll.HorizontalOffset + CellScroll.ViewportWidth / 2) / oldZoom;
        double anchorY = (CellScroll.VerticalOffset + CellScroll.ViewportHeight / 2) / oldZoom;
        _zoom = nextZoom;
        ApplyCanvasSizes();
        UpdateLayout();

        _syncingZoom = true;
        CellScroll.ChangeView(
            Math.Max(0, anchorX * nextZoom - CellScroll.ViewportWidth / 2),
            Math.Max(0, anchorY * nextZoom - CellScroll.ViewportHeight / 2),
            1.0f,
            disableAnimation: true);
        _syncingZoom = false;
        SyncHeaderZoomAndOffsets();
        
        // Re-invalidate after layout applies to fix blank cells on zoom
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Invalidate);
        
        ZoomChanged?.Invoke(this, _zoom);
    }

    public void SetFitMode(FitMode mode)
    {
        _fitMode = mode;
        if (mode == FitMode.FitWidth)
            SetZoom(GetFitWidthZoom());
        else if (mode == FitMode.FitPage)
            SetZoom(GetFitPageZoom());
    }

    public double GetFitWidthZoom()
    {
        double availableWidth = CellScroll.ViewportWidth;
        return SheetWidth > 0 && availableWidth > 0
            ? availableWidth / SheetWidth
            : _zoom;
    }

    public double GetFitPageZoom()
    {
        double availableWidth = CellScroll.ViewportWidth;
        double availableHeight = CellScroll.ViewportHeight;
        return SheetWidth > 0 && SheetHeight > 0 && availableWidth > 0 && availableHeight > 0
            ? Math.Min(availableWidth / SheetWidth, availableHeight / SheetHeight)
            : _zoom;
    }

    public void SetSearchMatches(IReadOnlyList<SpreadsheetMatch> matches, int activeIndex)
    {
        _allSearchMatches = matches;
        _activeSearchIndex = activeIndex;
        UpdateVisibleSearchMatches();
    }

    public async Task<SearchResult[]> SearchAsync(string query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_doc == null || string.IsNullOrWhiteSpace(query))
        {
            _viewerSearchResults = Array.Empty<SpreadsheetMatch>();
            ClearSearchMatches();
            return Array.Empty<SearchResult>();
        }

        SpreadsheetDocument document = _doc;
        IReadOnlyList<SpreadsheetMatch> matches = await Task.Run(
            () => document.SearchText(query, MatchCase),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(_doc, document))
            return Array.Empty<SearchResult>();

        _viewerSearchResults = matches;
        SetSearchMatches(_viewerSearchResults, _viewerSearchResults.Count > 0 ? 0 : -1);
        var results = new SearchResult[_viewerSearchResults.Count];
        for (int index = 0; index < results.Length; index++)
        {
            SpreadsheetMatch match = _viewerSearchResults[index];
            results[index] = new SearchResult(
                query,
                new SearchLocation[] { new SpreadsheetCellLocation(match.SheetIndex, match.Row, match.Col) });
        }
        return results;
    }

    public bool MatchCase { get; set; }

    public void Highlight(SearchResult result)
    {
        foreach (SearchLocation location in result.Locations)
        {
            if (location is not SpreadsheetCellLocation cell)
                continue;
            int index = -1;
            for (int i = 0; i < _viewerSearchResults.Count; i++)
            {
                SpreadsheetMatch match = _viewerSearchResults[i];
                if (match.SheetIndex == cell.SheetIndex && match.Row == cell.Row && match.Col == cell.Column)
                {
                    index = i;
                    break;
                }
            }
            SetSearchMatches(_viewerSearchResults, index);
            SelectCell(cell.SheetIndex, cell.Row, cell.Column);
            return;
        }
    }

    public void ClearHighlight()
    {
        _viewerSearchResults = Array.Empty<SpreadsheetMatch>();
        ClearSearchMatches();
    }

    public Task<string?> GetSelectedTextAsync() => Task.FromResult(GetSelectionText());

    public Task CopySelectionAsync()
    {
        CopySelectionToClipboard();
        return Task.CompletedTask;
    }

    private void UpdateVisibleSearchMatches()
    {
        _searchCells.Clear();
        _activeSearchRow = -1;
        _activeSearchCol = -1;

        for (int i = 0; i < _allSearchMatches.Count; i++)
        {
            var match = _allSearchMatches[i];
            if (match.SheetIndex != _activeSheet)
                continue;

            _searchCells.Add((match.Row, match.Col));
            if (i == _activeSearchIndex)
            {
                _activeSearchRow = match.Row;
                _activeSearchCol = match.Col;
            }
        }

        Invalidate();
    }

    public void ClearSearchMatches()
    {
        _allSearchMatches = Array.Empty<SpreadsheetMatch>();
        _activeSearchIndex = -1;
        _searchCells.Clear();
        _activeSearchRow = -1;
        _activeSearchCol = -1;
        Invalidate();
    }

    public void Cleanup()
    {
        _sheetLoadGeneration++;
        _doc = null;
        _data = null;
        _cellMap = null;
        _activeSheet = 0;
        _selectedRow = -1;
        _selectedCol = -1;
        _selectionAnchorRow = -1;
        _selectionAnchorCol = -1;
        _selectionFocusRow = -1;
        _selectionFocusCol = -1;
        _mergeMap.Clear();
        _covered.Clear();
        _colX = Array.Empty<double>();
        _rowY = Array.Empty<double>();
        _totalWidth = 0;
        _totalHeight = 0;
        TabBar.Children.Clear();
        ClearSearchMatches();
        ApplyCanvasSizes();
        _cellCanvas?.Invalidate();
        _colHeaderCanvas?.Invalidate();
        _rowHeaderCanvas?.Invalidate();
    }

    // ── Sheet loading ────────────────────────────────────────────────────────

    private async Task LoadSheetAsync(int index, CancellationToken ct = default)
    {
        if (_doc == null) return;

        int generation = ++_sheetLoadGeneration;
        var document = _doc;
        string? json;
        try
        {
            json = await document.GetSheetDataJsonAsync(index, ct);
        }
        catch (ObjectDisposedException) when (!ReferenceEquals(document, _doc))
        {
            return;
        }

        if (generation != _sheetLoadGeneration ||
            !ReferenceEquals(document, _doc) ||
            _activeSheet != index)
            return;

        _data    = SheetData.FromJson(json);
        if (_data == null) return;

        _cellMap = _data.BuildCellMap();
        BuildMergeIndex();
        BuildLayoutArrays();
        UpdateVisibleSearchMatches();

        ApplyCanvasSizes();

        // Reset scroll position
        CellScroll.ChangeView(0, 0, null, disableAnimation: true);
        ColHeaderScroll.ChangeView(0, 0, null, disableAnimation: true);
        RowHeaderScroll.ChangeView(0, 0, null, disableAnimation: true);

        _selectedRow = -1;
        _selectedCol = -1;

        Invalidate();
    }

    private void Invalidate()
    {
        _cellCanvas?.Invalidate();
        _colHeaderCanvas?.Invalidate();
        _rowHeaderCanvas?.Invalidate();
    }

    private void BuildMergeIndex()
    {
        _mergeMap.Clear();
        _covered.Clear();
        if (_data == null) return;

        foreach (var m in _data.Merges)
        {
            _mergeMap[(m.Row, m.Col)] = m;
            // If RowSpan/ColSpan are counts (2 = spans 2 cells), use < not <=
            for (int r = m.Row; r < m.Row + m.RowSpan; r++)
                for (int c = m.Col; c < m.Col + m.ColSpan; c++)
                    if (r != m.Row || c != m.Col)
                        _covered.Add((r, c));
        }
    }

    private void BuildLayoutArrays()
    {
        if (_data == null)
        {
            _colX = Array.Empty<double>();
            _rowY = Array.Empty<double>();
            _totalWidth = 200;
            _totalHeight = 400;
            return;
        }

        int cols = Math.Max(_data.ColCount + 5, 30);
        int rows = Math.Max(_data.RowCount + 5, 60);

        _colX = new double[cols + 1];
        _rowY = new double[rows + 1];

        double x = 0;
        for (int c = 0; c < cols; c++) { _colX[c] = x; x += _data.GetColWidth(c); }
        _colX[cols] = x;
        _totalWidth  = x;

        double y = 0;
        for (int r = 0; r < rows; r++) { _rowY[r] = y; y += _data.GetRowHeight(r); }
        _rowY[rows] = y;
        _totalHeight = y;
    }

    // ── Tab bar ───────────────────────────────────────────────────────────────

    private void RebuildTabBar()
    {
        TabBar.Children.Clear();
        if (_doc == null) return;

        for (int i = 0; i < _doc.SheetCount; i++)
        {
            int idx  = i;
            string name = _doc.GetSheetName(i);
            var btn = new Button
            {
                Content         = name,
                MinWidth        = 80,
                Height          = 26,
                Padding         = new Thickness(12, 0, 12, 0),
                CornerRadius    = new CornerRadius(4, 4, 0, 0),
                Margin          = new Thickness(2, 0, 0, 0),
                FontSize        = 12,
                BorderThickness = new Thickness(1),
            };
            btn.Click += async (_, _) =>
            {
                await SelectSheetAsync(idx);
            };
            TabBar.Children.Add(btn);
        }
        UpdateTabStyles();
    }

    private void UpdateTabStyles()
    {
        for (int i = 0; i < TabBar.Children.Count; i++)
        {
            if (TabBar.Children[i] is Button btn)
            {
                bool active = i == _activeSheet;
                btn.Background = new SolidColorBrush(active ? _tabActive : _tabInactive);
                btn.Foreground = new SolidColorBrush(_tabText);
                btn.BorderBrush = new SolidColorBrush(_tabBorder);
                btn.FontWeight  = active ? FontWeights.SemiBold : FontWeights.Normal;
                if (active)
                    btn.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true });
            }
        }
    }

    // ── Scroll sync ───────────────────────────────────────────────────────────

    private bool _syncingScroll;
    private bool _syncingZoom;

    private void CellScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        // CRITICAL: Invalidate cell canvas on EVERY view change to prevent blank cells
        ResizeCanvases();   // viewport size can change as scrollbars appear/disappear
        _cellCanvas?.Invalidate();

        if (_syncingScroll || _syncingZoom) return;

        double nativeZoom = CellScroll.ZoomFactor;
        if (Math.Abs(nativeZoom - 1.0) > 0.001)
        {
            if (e.IsIntermediate)
            {
                SyncHeadersDuringPinch(nativeZoom);
                return;
            }

            double offsetX = CellScroll.HorizontalOffset;
            double offsetY = CellScroll.VerticalOffset;
            _zoom = Math.Clamp(_zoom * nativeZoom, 0.1, 6.0);
            ApplyCanvasSizes();
            UpdateLayout();

            _syncingZoom = true;
            CellScroll.ChangeView(offsetX, offsetY, 1.0f, disableAnimation: true);
            _syncingZoom = false;

            ManualZoomRequested?.Invoke(this, EventArgs.Empty);
            SyncHeaderZoomAndOffsets();
            Invalidate();
            ZoomChanged?.Invoke(this, _zoom);
        }

        _syncingScroll = true;
        try
        {
            ColHeaderScroll.ChangeView(CellScroll.HorizontalOffset, null, null, disableAnimation: true);
            RowHeaderScroll.ChangeView(null, CellScroll.VerticalOffset, null, disableAnimation: true);
        }
        finally { _syncingScroll = false; }

        _colHeaderCanvas?.Invalidate();
        _rowHeaderCanvas?.Invalidate();
    }

    private void SyncHeaderZoomAndOffsets()
    {
        if (_syncingZoom) return;

        _syncingZoom = true;
        try
        {
            ColHeaderScroll.ChangeView(
                CellScroll.HorizontalOffset,
                null,
                1.0f,
                disableAnimation: true);
            RowHeaderScroll.ChangeView(
                null,
                CellScroll.VerticalOffset,
                1.0f,
                disableAnimation: true);
        }
        finally { _syncingZoom = false; }
    }

    private void SyncHeadersDuringPinch(double nativeZoom)
    {
        _syncingZoom = true;
        try
        {
            ColHeaderScroll.ChangeView(
                CellScroll.HorizontalOffset,
                null,
                (float)nativeZoom,
                disableAnimation: true);
            RowHeaderScroll.ChangeView(
                null,
                CellScroll.VerticalOffset,
                (float)nativeZoom,
                disableAnimation: true);
        }
        finally { _syncingZoom = false; }
    }

    private void CellScroll_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        bool controlDown = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (!controlDown) return;

        int delta = e.GetCurrentPoint(CellScroll).Properties.MouseWheelDelta;
        if (delta == 0) return;

        ManualZoomRequested?.Invoke(this, EventArgs.Empty);
        SetZoom(_zoom * Math.Pow(1.1, delta / 120.0));
        e.Handled = true;
    }

    // ── Win2D draw ─────────────────────────────────────────────────────────────

    // Draw handlers run inside the XAML composition pass: an exception here (device
    // removal, bad glyph run) propagates to Application.UnhandledException, which is
    // now fatal by design. Convert draw failures into logged, recoverable events and
    // trip a circuit breaker if the device is genuinely wedged.
    private int _consecutiveDrawFailures;
    private const int MaxConsecutiveDrawFailures = 10;

    private void CellCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        try { CellCanvas_DrawCore(args); OnDrawSucceeded(); }
        catch (Exception ex) { OnDrawFailed("CellCanvas", ex); }
    }

    private void ColHeaderCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        try { ColHeaderCanvas_DrawCore(args); OnDrawSucceeded(); }
        catch (Exception ex) { OnDrawFailed("ColHeaderCanvas", ex); }
    }

    private void RowHeaderCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        try { RowHeaderCanvas_DrawCore(args); OnDrawSucceeded(); }
        catch (Exception ex) { OnDrawFailed("RowHeaderCanvas", ex); }
    }

    private void OnDrawSucceeded() => _consecutiveDrawFailures = 0;

    private void OnDrawFailed(string canvas, Exception ex)
    {
        _consecutiveDrawFailures++;
        var level = _consecutiveDrawFailures >= MaxConsecutiveDrawFailures
            ? LogLevel.Error
            : LogLevel.Warning;
        Logger.Write(level,
            "Event=Win2dDrawFailed | " +
            $"Canvas={canvas} ConsecutiveFailures={_consecutiveDrawFailures} " +
            $"HResult=0x{ex.HResult:X8}",
            ex);
        // No rethrow, no re-invalidate: Win2D recreates the device/session and retries
        // on the next Invalidate (scroll, zoom, selection). Continuous failure trips
        // the counter and each occurrence is logged with full detail.
    }

    private void CellCanvas_DrawCore(CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        ds.Clear(_bgGrid);
        if (_data == null || _colX.Length < 2 || _rowY.Length < 2) return;

        double scrollX = CellScroll.HorizontalOffset;
        double scrollY = CellScroll.VerticalOffset;
        double vpW     = CellScroll.ViewportWidth > 0 ? CellScroll.ViewportWidth : Math.Max(ActualWidth, 1200);
        double vpH     = CellScroll.ViewportHeight > 0 ? CellScroll.ViewportHeight : Math.Max(ActualHeight, 800);

        double zoom = Math.Max(0.01, _zoom);
        // Viewport-sized canvas: transform maps sheet coords → viewport pixels by
        // scaling with zoom, then translating by the (zoomed) scroll offsets.
        ds.Transform = Matrix3x2.CreateScale((float)zoom)
            * Matrix3x2.CreateTranslation(-(float)scrollX, -(float)scrollY);

        // 0.5-viewport draw margin so fast scrolling never shows blanks.
        int firstCol = ColAt((scrollX - vpW * 0.5) / zoom);
        int lastCol  = ColAt((scrollX + vpW * 1.5) / zoom) + 1;
        int firstRow = RowAt((scrollY - vpH * 0.5) / zoom);
        int lastRow  = RowAt((scrollY + vpH * 1.5) / zoom) + 1;

        firstCol = Math.Clamp(firstCol, 0, _colX.Length - 2);
        lastCol  = Math.Clamp(lastCol,  0, _colX.Length - 2);
        firstRow = Math.Clamp(firstRow, 0, _rowY.Length - 2);
        lastRow  = Math.Clamp(lastRow,  0, _rowY.Length - 2);

        // ── Background fills ─────────────────────────────────────────────────
        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int col = firstCol; col <= lastCol; col++)
            {
                if (_covered.Contains((row, col))) continue;

                var cr = CellRect(row, col);
                bool isSelected = IsCellInSelection(row, col);

                if (_cellMap != null && _cellMap.TryGetValue((row, col), out var cell) && cell.Bg != null
                    && TryParseColor(cell.Bg, out var bg))
                {
                    ds.FillRectangle(ToR(cr), bg);
                }

                if (_searchCells.Contains((row, col)))
                {
                    bool activeSearchHit = row == _activeSearchRow && col == _activeSearchCol;
                    ds.FillRectangle(ToR(cr), activeSearchHit
                        ? Color.FromArgb(105, 255, 105, 97)
                        : Color.FromArgb(90, 255, 224, 102));
                }

                if (isSelected && !_searchCells.Contains((row, col)))
                    ds.FillRectangle(ToR(cr), _selection);
                if (row == _activeSearchRow && col == _activeSearchCol)
                    ds.DrawRectangle(ToR(cr), Color.FromArgb(230, 205, 65, 60), 1.0f);
            }
        }

        // ── Grid lines ──────────────────────────────────────────────────────
        if (_data.ShowGridlines)
        {
            float rowTop    = (float)_rowY[firstRow];
            float rowBottom = (float)(_rowY[Math.Min(lastRow + 1, _rowY.Length - 1)]);
            float colLeft   = (float)_colX[firstCol];
            float colRight  = (float)(_colX[Math.Min(lastCol + 1, _colX.Length - 1)]);

            for (int col = firstCol; col <= lastCol + 1 && col < _colX.Length; col++)
            {
                float x = (float)_colX[col];
                ds.DrawLine(x, rowTop, x, rowBottom, _gridLine, 0.5f);
            }
            for (int row = firstRow; row <= lastRow + 1 && row < _rowY.Length; row++)
            {
                float y = (float)_rowY[row];
                ds.DrawLine(colLeft, y, colRight, y, _gridLine, 0.5f);
            }
        }

        if (HasCellSelection)
        {
            var (selectionFirstRow, selectionLastRow, selectionFirstCol, selectionLastCol) = GetSelectionBounds();
            var selectionRect = new Rect(
                _colX[selectionFirstCol],
                _rowY[selectionFirstRow],
                _colX[selectionLastCol + 1] - _colX[selectionFirstCol],
                _rowY[selectionLastRow + 1] - _rowY[selectionFirstRow]);
            ds.DrawRectangle(ToR(selectionRect), Color.FromArgb(255, 0, 120, 215), 1.5f);
        }

        // ── Cell text ───────────────────────────────────────────────────────
        // PERFORMANCE: Reuse default format for cells without special formatting (huge speedup for large data!)
        using var defaultFmt = new CanvasTextFormat
        {
            FontFamily = "Segoe UI",
            FontSize   = 12f,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
            VerticalAlignment   = CanvasVerticalAlignment.Bottom,
            WordWrapping = CanvasWordWrapping.NoWrap,
            Options = CanvasDrawTextOptions.Clip,
        };

        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int col = firstCol; col <= lastCol; col++)
            {
                if (_covered.Contains((row, col))) continue;
                if (_cellMap == null || !_cellMap.TryGetValue((row, col), out var cell)) continue;
                if (string.IsNullOrEmpty(cell.Value)) continue;

                var cr = CellRect(row, col);

                // Expand for merges
                if (_mergeMap.TryGetValue((row, col), out var merge))
                {
                    // If RowSpan/ColSpan are counts, use -1 to get the last cell index
                    int endR = Math.Min(merge.Row + merge.RowSpan - 1, _rowY.Length - 2);
                    int endC = Math.Min(merge.Col + merge.ColSpan - 1, _colX.Length - 2);
                    cr = new Rect(_colX[col], _rowY[row],
                        _colX[endC + 1] - _colX[col],
                        _rowY[endR + 1] - _rowY[row]);
                }

                // Only create custom format if needed
                CanvasTextFormat? customFmt = null;
                bool needsCustom = cell.Bold || cell.Italic || cell.Wrap ||
                    cell.HAlign != "left" || cell.VAlign != "bottom";

                if (needsCustom)
                {
                    customFmt = new CanvasTextFormat
                    {
                        FontFamily = "Segoe UI",
                        FontSize   = 12f,
                        FontStyle  = cell.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
                        FontWeight = cell.Bold ? FontWeights.Bold : FontWeights.Normal,
                        HorizontalAlignment = cell.HAlign switch
                        {
                            "center" => CanvasHorizontalAlignment.Center,
                            "right"  => CanvasHorizontalAlignment.Right,
                            _        => CanvasHorizontalAlignment.Left,
                        },
                        VerticalAlignment = cell.VAlign switch
                        {
                            "top"    => CanvasVerticalAlignment.Top,
                            "center" => CanvasVerticalAlignment.Center,
                            _        => CanvasVerticalAlignment.Bottom,
                        },
                        WordWrapping = cell.Wrap ? CanvasWordWrapping.EmergencyBreak : CanvasWordWrapping.NoWrap,
                        Options = CanvasDrawTextOptions.Clip,
                    };
                }

                // Theme-aware text color: use cell foreground if specified and not too dark for dark theme
                Color fg = _cellText;
                if (cell.Fg != null && TryParseColor(cell.Fg, out var cellFg))
                {
                    // If dark theme and cell color is too dark (e.g., black text), use theme color instead
                    bool isDarkTheme = ActualTheme == ElementTheme.Dark ||
                        (ActualTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
                    
                    if (isDarkTheme)
                    {
                        // Calculate perceived brightness (0-255)
                        int brightness = (int)(0.299 * cellFg.R + 0.587 * cellFg.G + 0.114 * cellFg.B);
                        // If text is too dark (brightness < 80), use theme text color for readability
                        fg = brightness < 80 ? _cellText : cellFg;
                    }
                    else
                    {
                        fg = cellFg;
                    }
                }
                
                var textRect = new Rect(
                    cr.X + 3,
                    cr.Y + 2,
                    Math.Max(0, cr.Width - 6),
                    Math.Max(0, cr.Height - 4));
                ds.DrawText(cell.Value!, ToR(textRect), fg, customFmt ?? defaultFmt);

                customFmt?.Dispose();
            }
        }
    }

    private void ColHeaderCanvas_DrawCore(CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        ds.Clear(_bgHeader);
        if (_data == null || _colX.Length < 2) return;

        double scrollX = CellScroll.HorizontalOffset;
        double zoom = Math.Max(0.01, _zoom);
        int firstCol   = ColAt(scrollX / zoom);
        int lastCol    = ColAt((scrollX + Math.Max(CellScroll.ViewportWidth, 400)) / zoom) + 1;
        firstCol = Math.Clamp(firstCol, 0, _colX.Length - 2);
        lastCol  = Math.Clamp(lastCol,  0, _colX.Length - 2);

        ds.Transform = Matrix3x2.CreateScale((float)zoom)
            * Matrix3x2.CreateTranslation(-(float)CellScroll.HorizontalOffset, 0);

        using var fmt = new CanvasTextFormat
        {
            FontFamily = "Segoe UI", FontSize = 11f,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment   = CanvasVerticalAlignment.Center,
        };

        for (int col = firstCol; col <= lastCol; col++)
        {
            float x = (float)_colX[col];
            float w = (float)(_colX[col + 1] - _colX[col]);

            if (col == _selectedCol)
                ds.FillRectangle(x, 0, w, 26, _headerSel);

            ds.DrawText(ColName(col), x + w / 2, 13, _headerText, fmt);
            ds.DrawLine(x + w, 0, x + w, 26, _gridLine, 0.5f);
        }
        float headerW = (float)_totalWidth;
        ds.DrawLine(0, 25.5f, headerW, 25.5f, _gridLine, 1f);
    }

    private void RowHeaderCanvas_DrawCore(CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        ds.Clear(_bgHeader);
        if (_data == null || _rowY.Length < 2) return;

        double zoom = Math.Max(0.01, _zoom);
        double scrollY = CellScroll.VerticalOffset;
        int firstRow   = RowAt(scrollY / zoom);
        int lastRow    = RowAt((scrollY + Math.Max(CellScroll.ViewportHeight > 0 ? CellScroll.ViewportHeight : 800, 400)) / zoom) + 1;
        firstRow = Math.Clamp(firstRow, 0, _rowY.Length - 2);
        lastRow  = Math.Clamp(lastRow,  0, _rowY.Length - 2);

        ds.Transform = Matrix3x2.CreateScale((float)zoom)
            * Matrix3x2.CreateTranslation(0, -(float)CellScroll.VerticalOffset);

        using var fmt = new CanvasTextFormat
        {
            FontFamily = "Segoe UI", FontSize = 11f,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment   = CanvasVerticalAlignment.Center,
        };

        for (int row = firstRow; row <= lastRow; row++)
        {
            float y = (float)_rowY[row];
            float h = (float)(_rowY[row + 1] - _rowY[row]);

            if (HasCellSelection && row >= Math.Min(_selectionAnchorRow, _selectionFocusRow) &&
                row <= Math.Max(_selectionAnchorRow, _selectionFocusRow))
                ds.FillRectangle(0, y, 50, h, _headerSel);

            ds.DrawText((row + 1).ToString(), 25, y + h / 2, _headerText, fmt);
            ds.DrawLine(0, y + h, 50, y + h, _gridLine, 0.5f);
        }
        float headerH = (float)_totalHeight;
        ds.DrawLine(49.5f, 0, 49.5f, headerH, _gridLine, 1f);
    }

    // ── Pointer handling ──────────────────────────────────────────────────────

    private void CellCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(CellScroll).Properties.IsLeftButtonPressed)
            return;

        var (row, col) = GetCellAtPointer(e.GetCurrentPoint(CellScroll).Position);
        SetCellSelection(row, col, row, col);

        _isSelectingRange = true;
        _selectionPointerId = e.Pointer.PointerId;
        CellScroll.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void CellCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isSelectingRange || e.Pointer.PointerId != _selectionPointerId)
            return;

        var (row, col) = GetCellAtPointer(e.GetCurrentPoint(CellScroll).Position);
        SetCellSelection(_selectionAnchorRow, _selectionAnchorCol, row, col);
        e.Handled = true;
    }

    private void CellCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isSelectingRange || e.Pointer.PointerId != _selectionPointerId)
            return;

        var (row, col) = GetCellAtPointer(e.GetCurrentPoint(CellScroll).Position);
        SetCellSelection(_selectionAnchorRow, _selectionAnchorCol, row, col);
        _isSelectingRange = false;
        CellScroll.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void CellCanvas_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId == _selectionPointerId)
            _isSelectingRange = false;
    }

    private (int row, int col) GetCellAtPointer(Point viewportPoint)
    {
        int col = ColAt((viewportPoint.X + CellScroll.HorizontalOffset) / _zoom);
        int row = RowAt((viewportPoint.Y + CellScroll.VerticalOffset) / _zoom);
        return (row, col);
    }

    private void SetCellSelection(int anchorRow, int anchorCol, int focusRow, int focusCol)
    {
        _selectionAnchorRow = anchorRow;
        _selectionAnchorCol = anchorCol;
        _selectionFocusRow = focusRow;
        _selectionFocusCol = focusCol;
        _selectedRow = focusRow;
        _selectedCol = focusCol;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool HasCellSelection =>
        _selectionAnchorRow >= 0 && _selectionAnchorCol >= 0 &&
        _selectionFocusRow >= 0 && _selectionFocusCol >= 0 &&
        _rowY.Length > 1 && _colX.Length > 1;

    private bool IsCellInSelection(int row, int col) =>
        HasCellSelection &&
        row >= Math.Min(_selectionAnchorRow, _selectionFocusRow) &&
        row <= Math.Max(_selectionAnchorRow, _selectionFocusRow) &&
        col >= Math.Min(_selectionAnchorCol, _selectionFocusCol) &&
        col <= Math.Max(_selectionAnchorCol, _selectionFocusCol);

    private (int firstRow, int lastRow, int firstCol, int lastCol) GetSelectionBounds() =>
        (Math.Min(_selectionAnchorRow, _selectionFocusRow),
         Math.Max(_selectionAnchorRow, _selectionFocusRow),
         Math.Min(_selectionAnchorCol, _selectionFocusCol),
         Math.Max(_selectionAnchorCol, _selectionFocusCol));

    private void CellScroll_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        Point point = e.GetPosition(CellScroll);
        var (row, col) = GetCellAtPointer(point);
        if (!IsCellInSelection(row, col))
            SetCellSelection(row, col, row, col);

        var flyout = new MenuFlyout();
        var copyItem = new MenuFlyoutItem
        {
            Text = "Copy",
            Icon = new SymbolIcon(Symbol.Copy),
        };
        copyItem.Click += (_, _) => CopySelectionToClipboard();
        flyout.Items.Add(copyItem);
        flyout.ShowAt(CellScroll, point);
        e.Handled = true;
    }

    private void CopySelectionToClipboard()
    {
        if (!HasCellSelection || _data == null)
            return;

        try
        {
            string? text = GetSelectionText();
            if (string.IsNullOrEmpty(text))
                return;

            var data = new DataPackage();
            data.SetText(text);
            Clipboard.SetContent(data);
            Clipboard.Flush();
        }
        catch (Exception ex)
        {
            Logger.Error("[SpreadsheetSelection] Could not copy selected cells", ex);
            SelectionError?.Invoke(this, ex.Message);
        }
    }

    private void TranslateSelection()
    {
        if (!HasCellSelection || _data == null)
            return;

        try
        {
            string? text = GetSelectionText();
            if (!string.IsNullOrWhiteSpace(text))
                TranslateRequested?.Invoke(text);
        }
        catch (Exception ex)
        {
            Logger.Error("[SpreadsheetSelection] Could not extract text for translation", ex);
            SelectionError?.Invoke(this, ex.Message);
        }
    }

    private string? GetSelectionText()
    {
        if (!HasCellSelection || _data == null)
            return null;

        var (firstRow, lastRow, firstCol, lastCol) = GetSelectionBounds();
        long cellCount = (long)(lastRow - firstRow + 1) * (lastCol - firstCol + 1);
        if (cellCount > 100_000)
            throw new InvalidOperationException("The selected range is too large to copy or translate at once.");

        var text = new StringBuilder();
        for (int row = firstRow; row <= lastRow; row++)
        {
            if (row > firstRow)
                text.AppendLine();
            for (int col = firstCol; col <= lastCol; col++)
            {
                if (col > firstCol)
                    text.Append('\t');
                if (_cellMap != null && _cellMap.TryGetValue((row, col), out var cell))
                    text.Append(cell.Value);
            }
        }

        return text.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private int ColAt(double x)
    {
        if (_colX.Length < 2) return 0;
        x = Math.Max(0, x);
        int lo = 0, hi = _colX.Length - 2;
        while (lo < hi) { int mid = (lo + hi + 1) / 2; if (_colX[mid] <= x) lo = mid; else hi = mid - 1; }
        return lo;
    }

    private int RowAt(double y)
    {
        if (_rowY.Length < 2) return 0;
        y = Math.Max(0, y);
        int lo = 0, hi = _rowY.Length - 2;
        while (lo < hi) { int mid = (lo + hi + 1) / 2; if (_rowY[mid] <= y) lo = mid; else hi = mid - 1; }
        return lo;
    }

    private Rect CellRect(int row, int col)
    {
        if (col >= _colX.Length - 1 || row >= _rowY.Length - 1) return Rect.Empty;
        return new Rect(_colX[col], _rowY[row], _colX[col + 1] - _colX[col], _rowY[row + 1] - _rowY[row]);
    }

    private static Windows.Foundation.Rect ToR(Rect r)
        => new((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height);

    private static string ColName(int col)
    {
        string name = "";
        col++;
        while (col > 0) { name = (char)('A' + (col - 1) % 26) + name; col = (col - 1) / 26; }
        return name;
    }

    private static bool TryParseColor(string hex, out Color color)
    {
        color = Colors.Black;
        if (string.IsNullOrEmpty(hex) || hex[0] != '#' || hex.Length != 7) return false;
        try
        {
            byte r = Convert.ToByte(hex[1..3], 16);
            byte g = Convert.ToByte(hex[3..5], 16);
            byte b = Convert.ToByte(hex[5..7], 16);
            color  = Color.FromArgb(255, r, g, b);
            return true;
        }
        catch { return false; }
    }

    // ── Theme ─────────────────────────────────────────────────────────────────

    private void OnThemeChanged(FrameworkElement sender, object args)
    {
        UpdateTheme();
        Invalidate();
        UpdateTabStyles();
    }

    private void UpdateTheme()
    {
        bool dark = ActualTheme == ElementTheme.Dark ||
            (ActualTheme == ElementTheme.Default &&
             Application.Current.RequestedTheme == ApplicationTheme.Dark);

        if (dark)
        {
            _bgGrid     = Color.FromArgb(255, 30,  30,  30 );
            _bgHeader   = Color.FromArgb(255, 44,  44,  44 );
            _gridLine   = Color.FromArgb(255, 65,  65,  65 );
            _headerText = Color.FromArgb(255, 160, 160, 160);
            _cellText   = Color.FromArgb(255, 220, 220, 220);
            _selection  = Color.FromArgb(50,  0,   144, 255);
            _headerSel  = Color.FromArgb(255, 40,  68,  110);
            _tabActive  = Color.FromArgb(255, 44,  44,  44 );
            _tabInactive= Color.FromArgb(255, 30,  30,  30 );
            _tabBorder  = Color.FromArgb(255, 80,  80,  80 );
            _tabText    = Color.FromArgb(255, 200, 200, 200);
        }
        else
        {
            _bgGrid     = Color.FromArgb(255, 255, 255, 255);
            _bgHeader   = Color.FromArgb(255, 243, 243, 243);
            _gridLine   = Color.FromArgb(255, 217, 217, 217);
            _headerText = Color.FromArgb(255, 80,  80,  80 );
            _cellText   = Color.FromArgb(255, 32,  32,  32 );
            _selection  = Color.FromArgb(60,  0,   120, 215);
            _headerSel  = Color.FromArgb(255, 210, 230, 255);
            _tabActive  = Color.FromArgb(255, 255, 255, 255);
            _tabInactive= Color.FromArgb(255, 237, 237, 237);
            _tabBorder  = Color.FromArgb(255, 196, 196, 196);
            _tabText    = Color.FromArgb(255, 60,  60,  60 );
        }
    }
}

public sealed class SpreadsheetViewerFactory : IDocumentViewerFactory
{
    private static readonly IReadOnlyCollection<string> Extensions =
        new[] { ".xlsx", ".xlsm", ".xltx", ".xltm" };

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public IDocumentViewer Create(ViewerViewModel viewModel) => new SpreadsheetViewer();
}
