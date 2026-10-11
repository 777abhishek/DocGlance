// SpreadsheetViewer.xaml.cs
// High-performance Win2D spreadsheet cell grid renderer for QuickView.
// Win2D CanvasControl instances are created programmatically to avoid WMC9999
// XAML compiler errors with WinAppSDK 2.x.

using System;
using System.Collections.Generic;
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
using QuickView.Rendering.Document;
using Windows.Foundation;
using Windows.UI;

namespace QuickView.Features.Viewer.Spreadsheet;

/// <summary>
/// Win2D-powered spreadsheet viewer — renders cell grids natively without sidebar.
/// All Win2D CanvasControl instances are created in code to avoid WMC9999 issues.
/// </summary>
public sealed partial class SpreadsheetViewer : UserControl
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
    private SheetData?           _data;
    private Dictionary<(int, int), CellData>? _cellMap;
    private int _activeSheet = 0;
    private int _selectedRow = -1;
    private int _selectedCol = -1;

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

        // Cell canvas
        _cellCanvas = new CanvasControl { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        _cellCanvas.Draw += CellCanvas_Draw;
        _cellCanvas.PointerPressed += CellCanvas_PointerPressed;
        CellCanvasHost.Children.Add(_cellCanvas);

        // Column header canvas
        _colHeaderCanvas = new CanvasControl { Height = 26, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        _colHeaderCanvas.Draw += ColHeaderCanvas_Draw;
        ColHeaderCanvasHost.Children.Add(_colHeaderCanvas);

        // Row header canvas
        _rowHeaderCanvas = new CanvasControl { Width = 50, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        _rowHeaderCanvas.Draw += RowHeaderCanvas_Draw;
        RowHeaderCanvasHost.Children.Add(_rowHeaderCanvas);

        ApplyCanvasSizes();
    }

    private void ApplyCanvasSizes()
    {
        if (_totalWidth <= 0 && _totalHeight <= 0) return;

        if (_cellCanvas != null)
        {
            _cellCanvas.Width  = _totalWidth;
            _cellCanvas.Height = _totalHeight;
        }
        if (_colHeaderCanvas != null)
        {
            _colHeaderCanvas.Width = _totalWidth;
        }
        if (_rowHeaderCanvas != null)
        {
            _rowHeaderCanvas.Height = _totalHeight;
        }

        CellCanvasHost.Width  = _totalWidth;
        CellCanvasHost.Height = _totalHeight;
        ColHeaderCanvasHost.Width  = _totalWidth;
        RowHeaderCanvasHost.Height = _totalHeight;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task LoadDocumentAsync(SpreadsheetDocument doc, CancellationToken ct = default)
    {
        _doc = doc;
        _activeSheet = 0;
        RebuildTabBar();
        await LoadSheetAsync(0, ct);
    }

    public void Cleanup()
    {
        _doc = null;
        _data = null;
        _cellMap = null;
        _cellCanvas?.Invalidate();
        _colHeaderCanvas?.Invalidate();
        _rowHeaderCanvas?.Invalidate();
    }

    // ── Sheet loading ────────────────────────────────────────────────────────

    private async Task LoadSheetAsync(int index, CancellationToken ct = default)
    {
        if (_doc == null) return;

        var json = await _doc.GetSheetDataJsonAsync(index, ct);
        _data    = SheetData.FromJson(json);
        if (_data == null) return;

        _cellMap = _data.BuildCellMap();
        BuildMergeIndex();
        BuildLayoutArrays();

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
            for (int r = m.Row; r <= m.Row + m.RowSpan; r++)
                for (int c = m.Col; c <= m.Col + m.ColSpan; c++)
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
                _activeSheet = idx;
                UpdateTabStyles();
                await LoadSheetAsync(idx);
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
            }
        }
    }

    // ── Scroll sync ───────────────────────────────────────────────────────────

    private bool _syncingScroll;

    private void CellScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_syncingScroll) return;
        _syncingScroll = true;
        try
        {
            ColHeaderScroll.ChangeView(CellScroll.HorizontalOffset, null, null, disableAnimation: true);
            RowHeaderScroll.ChangeView(null, CellScroll.VerticalOffset, null, disableAnimation: true);
        }
        finally
        {
            _syncingScroll = false;
        }
        _colHeaderCanvas?.Invalidate();
        _rowHeaderCanvas?.Invalidate();
    }

    // ── Win2D draw ─────────────────────────────────────────────────────────────

    private void CellCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        ds.Clear(_bgGrid);
        if (_data == null || _colX.Length < 2 || _rowY.Length < 2) return;

        double scrollX = CellScroll.HorizontalOffset;
        double scrollY = CellScroll.VerticalOffset;
        double vpW     = CellScroll.ViewportWidth > 0 ? CellScroll.ViewportWidth : Math.Max(ActualWidth, 1200);
        double vpH     = CellScroll.ViewportHeight > 0 ? CellScroll.ViewportHeight : Math.Max(ActualHeight, 800);

        int firstCol = ColAt(scrollX);
        int lastCol  = ColAt(scrollX + vpW) + 1;
        int firstRow = RowAt(scrollY);
        int lastRow  = RowAt(scrollY + vpH) + 1;

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
                bool isSelected = (row == _selectedRow || col == _selectedCol);

                if (_cellMap != null && _cellMap.TryGetValue((row, col), out var cell) && cell.Bg != null
                    && TryParseColor(cell.Bg, out var bg))
                {
                    ds.FillRectangle(ToR(cr), bg);
                }

                if (isSelected)
                    ds.FillRectangle(ToR(cr), _selection);
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

        // ── Cell text ───────────────────────────────────────────────────────
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
                    int endR = Math.Min(merge.Row + merge.RowSpan, _rowY.Length - 2);
                    int endC = Math.Min(merge.Col + merge.ColSpan, _colX.Length - 2);
                    cr = new Rect(_colX[col], _rowY[row],
                        _colX[endC + 1] - _colX[col],
                        _rowY[endR + 1] - _rowY[row]);
                }

                using var fmt = new CanvasTextFormat
                {
                    FontFamily = "Segoe UI",
                    FontSize   = 12f,
                    FontStyle  = cell.Italic
                        ? Windows.UI.Text.FontStyle.Italic
                        : Windows.UI.Text.FontStyle.Normal,
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
                    WordWrapping = cell.Wrap
                        ? CanvasWordWrapping.EmergencyBreak
                        : CanvasWordWrapping.NoWrap,
                    Options = CanvasDrawTextOptions.Clip,
                };

                var fg = (cell.Fg != null && TryParseColor(cell.Fg, out var c)) ? c : _cellText;
                var textRect = new Rect(cr.X + 3, cr.Y + 2, Math.Max(0, cr.Width - 6), Math.Max(0, cr.Height - 4));
                ds.DrawText(cell.Value!, ToR(textRect), fg, fmt);
            }
        }
    }

    private void ColHeaderCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        ds.Clear(_bgHeader);
        if (_data == null || _colX.Length < 2) return;

        double scrollX = CellScroll.HorizontalOffset;
        int firstCol   = ColAt(scrollX);
        int lastCol    = ColAt(scrollX + Math.Max(CellScroll.ViewportWidth, 400)) + 1;
        firstCol = Math.Clamp(firstCol, 0, _colX.Length - 2);
        lastCol  = Math.Clamp(lastCol,  0, _colX.Length - 2);

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
        float headerW = (float)(sender.Size.Width > 0 ? sender.Size.Width : _totalWidth);
        ds.DrawLine(0, 25.5f, headerW, 25.5f, _gridLine, 1f);
    }

    private void RowHeaderCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        ds.Clear(_bgHeader);
        if (_data == null || _rowY.Length < 2) return;

        double scrollY = CellScroll.VerticalOffset;
        int firstRow   = RowAt(scrollY);
        int lastRow    = RowAt(scrollY + Math.Max(CellScroll.ViewportHeight > 0 ? CellScroll.ViewportHeight : 800, 400)) + 1;
        firstRow = Math.Clamp(firstRow, 0, _rowY.Length - 2);
        lastRow  = Math.Clamp(lastRow,  0, _rowY.Length - 2);

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

            if (row == _selectedRow)
                ds.FillRectangle(0, y, 50, h, _headerSel);

            ds.DrawText((row + 1).ToString(), 25, y + h / 2, _headerText, fmt);
            ds.DrawLine(0, y + h, 50, y + h, _gridLine, 0.5f);
        }
        float headerH = (float)(sender.Size.Height > 0 ? sender.Size.Height : _totalHeight);
        ds.DrawLine(49.5f, 0, 49.5f, headerH, _gridLine, 1f);
    }

    // ── Pointer handling ──────────────────────────────────────────────────────

    private void CellCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pos  = e.GetCurrentPoint(_cellCanvas).Position;
        _selectedCol = ColAt(pos.X);
        _selectedRow = RowAt(pos.Y);
        Invalidate();
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
