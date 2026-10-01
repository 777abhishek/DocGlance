using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using QuickView.Infrastructure.Logging;
using QuickView.Rendering.Pdf;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace QuickView.Controls;

/// <summary>
/// Transparent overlay: left- or right-drag selects PDF text; release shows Copy.
/// Right-click without a drag opens the context menu. Highlights use preloaded
/// char boxes so drag never waits on PDFium.
/// </summary>
public sealed partial class PdfTextSelectionOverlay : UserControl
{
    private PdfDocumentState? _documentState;
    private int               _pageIndex;
    private int               _preloadGen;

    private bool   _isSelecting;
    private bool   _rightButtonDrag;
    private bool   _hasDragged;
    private bool   _suppressContextMenu;
    private Point  _pressPoint;
    private Point  _lastPoint;
    private int    _selectionStartCharIndex = -1;
    private int    _selectionEndCharIndex   = -1;
    private string _selectedText            = string.Empty;
    private string _pageText                = string.Empty;

    private List<CharBox>? _charBoxes;
    private const double DragThreshold = 4.0;

    private struct CharBox
    {
        public int    Index;
        public double X, Y, W, H;
        public double HitY, HitH;
    }

    public PdfTextSelectionOverlay() => InitializeComponent();

    public void SetPage(PdfDocumentState? documentState, int pageIndex, double width, double height)
    {
        _documentState = documentState;
        _pageIndex     = pageIndex;
        _charBoxes     = null;
        _pageText      = string.Empty;
        ClearSelection();
        _ = PreloadCharBoxesAsync(++_preloadGen, (int)Math.Max(1, width), (int)Math.Max(1, height));
    }

    private async Task PreloadCharBoxesAsync(int gen, int pageW, int pageH)
    {
        var state = _documentState;
        if (state == null) return;

        try
        {
            var (boxes, text) = await Task.Run(() => LoadCharBoxes(state, _pageIndex, pageW, pageH));
            if (gen != _preloadGen) return;

            NormalizeLineHitBoxes(boxes);
            _charBoxes = boxes;
            _pageText  = text;
            Logger.Log($"[PDF Selection] Preloaded {boxes.Count} char boxes for page {_pageIndex}");
        }
        catch (Exception ex)
        {
            if (gen != _preloadGen) return;
            Logger.Error("[PDF Selection] CharBox preload failed", ex);
            _charBoxes = new List<CharBox>();
            _pageText  = string.Empty;
        }
    }

    private static (List<CharBox> boxes, string text) LoadCharBoxes(
        PdfDocumentState state, int pageIndex, int pageW, int pageH)
    {
        var result = new List<CharBox>();
        string text = string.Empty;

        Pdfium.EnsureInitialized();
        lock (Pdfium.Gate)
        {
            IntPtr doc = state.Handle;
            if (doc == IntPtr.Zero) return (result, text);

            IntPtr page = Pdfium.FPDF_LoadPage(doc, pageIndex);
            if (page == IntPtr.Zero) return (result, text);
            try
            {
                IntPtr textPage = Pdfium.FPDFText_LoadPage(page);
                if (textPage == IntPtr.Zero) return (result, text);
                try
                {
                    int total = Pdfium.FPDFText_CountChars(textPage);
                    if (total <= 0) return (result, text);

                    var chars = new ushort[total + 1];
                    Pdfium.FPDFText_GetText(textPage, 0, total, chars);
                    int len = total;
                    while (len > 0 && chars[len - 1] == 0) len--;
                    var bytes = new byte[len * 2];
                    Buffer.BlockCopy(chars, 0, bytes, 0, bytes.Length);
                    text = Encoding.Unicode.GetString(bytes);

                    for (int i = 0; i < total; i++)
                    {
                        Pdfium.FPDFText_GetCharBox(textPage, i,
                            out double left, out double right,
                            out double bottom, out double top);

                        Pdfium.FPDF_PageToDevice(page, 0, 0, pageW, pageH, 0,
                            left,  top,    out int x1, out int y1);
                        Pdfium.FPDF_PageToDevice(page, 0, 0, pageW, pageH, 0,
                            right, bottom, out int x2, out int y2);

                        double x = Math.Min(x1, x2);
                        double y = Math.Min(y1, y2);
                        double w = Math.Abs(x2 - x1);
                        double h = Math.Abs(y2 - y1);
                        if (w < 0.5) w = 4;
                        if (h < 0.5) h = 10;

                        result.Add(new CharBox { Index = i, X = x, Y = y, W = w, H = h });
                    }
                }
                finally { Pdfium.FPDFText_ClosePage(textPage); }
            }
            finally { Pdfium.FPDF_ClosePage(page); }
        }

        return (result, text);
    }

    private static void NormalizeLineHitBoxes(List<CharBox> boxes)
    {
        if (boxes.Count == 0) return;

        var lines = new Dictionary<int, (double minY, double maxY)>();
        foreach (var b in boxes)
        {
            int key = (int)Math.Round(b.Y / 4.0) * 4;
            if (!lines.TryGetValue(key, out var span))
                span = (b.Y, b.Y + b.H);
            lines[key] = (Math.Min(span.minY, b.Y), Math.Max(span.maxY, b.Y + b.H));
        }

        for (int i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            int key = (int)Math.Round(b.Y / 4.0) * 4;
            var (minY, maxY) = lines[key];
            b.HitY = minY;
            b.HitH = Math.Max(12, maxY - minY);
            boxes[i] = b;
        }
    }

    private int GetCharIndexAtPoint(double x, double y)
    {
        if (_charBoxes == null || _charBoxes.Count == 0) return -1;

        int bestOnLine = -1;
        double bestOnLineDx = double.MaxValue;
        int bestAny = -1;
        double bestAnyDist = double.MaxValue;

        foreach (var b in _charBoxes)
        {
            bool onLine = y >= b.HitY && y <= b.HitY + b.HitH;
            if (onLine && x >= b.X && x <= b.X + b.W)
                return b.Index;

            double dx = x < b.X ? b.X - x : x > b.X + b.W ? x - (b.X + b.W) : 0;
            double dy = y < b.HitY ? b.HitY - y : y > b.HitY + b.HitH ? y - (b.HitY + b.HitH) : 0;

            if (onLine && dx < bestOnLineDx)
            {
                bestOnLineDx = dx;
                bestOnLine = b.Index;
            }

            double dist = dx + dy * 3;
            if (dist < bestAnyDist)
            {
                bestAnyDist = dist;
                bestAny = b.Index;
            }
        }

        if (bestOnLine >= 0) return bestOnLine;
        return bestAnyDist < 80 ? bestAny : -1;
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
        => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.IBeam);

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_isSelecting)
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_documentState == null) return;

        var props = e.GetCurrentPoint(this).Properties;
        bool left  = props.IsLeftButtonPressed && !props.IsRightButtonPressed;
        bool right = props.IsRightButtonPressed && !props.IsLeftButtonPressed;
        if (!left && !right) return;

        CopyFlyout.Hide();

        if (right)
        {
            // Keep an existing selection so right-click Copy works; start a
            // new drag only after the pointer moves past the threshold.
            _rightButtonDrag = true;
            _isSelecting     = true;
            _hasDragged      = false;
            _pressPoint      = e.GetCurrentPoint(this).Position;
            _lastPoint       = _pressPoint;
            return;
        }

        _rightButtonDrag         = false;
        _suppressContextMenu     = false;
        _pressPoint              = e.GetCurrentPoint(this).Position;
        _lastPoint               = _pressPoint;
        _hasDragged              = false;
        _isSelecting             = true;
        _selectionStartCharIndex = -1;
        _selectionEndCharIndex   = -1;
        _selectedText            = string.Empty;
        ClearSelectionVisuals();
        CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isSelecting || _documentState == null) return;

        var props = e.GetCurrentPoint(this).Properties;
        bool held = _rightButtonDrag ? props.IsRightButtonPressed : props.IsLeftButtonPressed;
        if (!held)
        {
            FinishPointer(e, cancelled: true);
            return;
        }

        var pos = e.GetCurrentPoint(this).Position;
        _lastPoint = pos;

        if (!_hasDragged)
        {
            double dx = pos.X - _pressPoint.X;
            double dy = pos.Y - _pressPoint.Y;
            if (dx * dx + dy * dy < DragThreshold * DragThreshold) return;

            _hasDragged = true;
            if (_rightButtonDrag)
            {
                _suppressContextMenu = true;
                CapturePointer(e.Pointer);
                _selectionStartCharIndex = -1;
                _selectionEndCharIndex   = -1;
                _selectedText            = string.Empty;
                ClearSelectionVisuals();
            }

            _selectionStartCharIndex = GetCharIndexAtPoint(_pressPoint.X, _pressPoint.Y);
            if (_selectionStartCharIndex < 0)
                _selectionStartCharIndex = GetCharIndexAtPoint(pos.X, pos.Y);
        }

        int charIdx = GetCharIndexAtPoint(pos.X, pos.Y);
        if (charIdx >= 0 && charIdx != _selectionEndCharIndex)
        {
            _selectionEndCharIndex = charIdx;
            PaintSelection();
        }

        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        bool leftUp  = kind == PointerUpdateKind.LeftButtonReleased;
        bool rightUp = kind == PointerUpdateKind.RightButtonReleased;
        if (!leftUp && !rightUp) return;
        if (!_isSelecting) return;

        bool wasRightDrag = _rightButtonDrag && _hasDragged;
        FinishPointer(e, cancelled: false);

        if (!_hasDragged)
        {
            if (leftUp)
                ClearSelection();
            return;
        }

        int finalIdx = GetCharIndexAtPoint(_lastPoint.X, _lastPoint.Y);
        if (finalIdx >= 0) _selectionEndCharIndex = finalIdx;

        if (!CommitSelectionText())
        {
            ClearSelection();
            return;
        }

        PaintSelection();
        ShowCopyFlyout(_lastPoint);
        Focus(FocusState.Programmatic);
        if (wasRightDrag || leftUp)
            e.Handled = true;
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
        => FinishPointer(e, cancelled: true);

    private void FinishPointer(PointerRoutedEventArgs e, bool cancelled)
    {
        _isSelecting     = false;
        _rightButtonDrag = false;
        ReleasePointerCaptures();
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.IBeam);
        if (cancelled)
            _hasDragged = false;
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (_suppressContextMenu || _hasDragged)
        {
            e.Handled = true;
            _suppressContextMenu = false;
        }
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_pageText) || _charBoxes == null) return;

        var pos = e.GetPosition(this);
        int idx = GetCharIndexAtPoint(pos.X, pos.Y);
        if (idx < 0 || idx >= _pageText.Length) return;

        int start = idx;
        int end   = idx;
        while (start > 0 && IsWordChar(_pageText[start - 1])) start--;
        while (end < _pageText.Length - 1 && IsWordChar(_pageText[end + 1])) end++;

        _selectionStartCharIndex = start;
        _selectionEndCharIndex   = end;
        if (!CommitSelectionText()) return;
        PaintSelection();
        ShowCopyFlyout(pos);
        e.Handled = true;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '\'';

    private bool CommitSelectionText()
    {
        int start = Math.Min(_selectionStartCharIndex, _selectionEndCharIndex);
        int end   = Math.Max(_selectionStartCharIndex, _selectionEndCharIndex);
        if (start < 0 || end < start) return false;

        if (!string.IsNullOrEmpty(_pageText) && start < _pageText.Length)
        {
            int len = Math.Min(end - start + 1, _pageText.Length - start);
            _selectedText = _pageText.Substring(start, len).Trim('\0');
        }
        else
        {
            _selectedText = string.Empty;
        }

        return !string.IsNullOrWhiteSpace(_selectedText);
    }

    private void PaintSelection()
    {
        SelectionCanvas.Children.Clear();
        if (_charBoxes == null) return;

        int start = Math.Min(_selectionStartCharIndex, _selectionEndCharIndex);
        int end   = Math.Max(_selectionStartCharIndex, _selectionEndCharIndex);
        if (start < 0 || end < start) return;

        var bands = new Dictionary<int, (double x0, double x1, double y, double h)>();
        foreach (var b in _charBoxes)
        {
            if (b.Index < start || b.Index > end) continue;
            int key = (int)Math.Round(b.HitY / 4.0) * 4;
            if (!bands.TryGetValue(key, out var band))
                band = (b.X, b.X + b.W, b.HitY, b.HitH);
            else
            {
                band.x0 = Math.Min(band.x0, b.X);
                band.x1 = Math.Max(band.x1, b.X + b.W);
                band.y  = Math.Min(band.y, b.HitY);
                band.h  = Math.Max(band.h, b.HitH);
            }
            bands[key] = band;
        }

        foreach (var band in bands.Values)
        {
            var rect = new Rectangle
            {
                Width            = Math.Max(1, band.x1 - band.x0),
                Height           = Math.Max(8, band.h),
                Fill             = new SolidColorBrush(Color.FromArgb(90, 0, 120, 215)),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(rect, band.x0);
            Canvas.SetTop(rect, band.y);
            SelectionCanvas.Children.Add(rect);
        }
    }

    private void ShowCopyFlyout(Point at)
    {
        try
        {
            CopyFlyout.ShowAt(this, new FlyoutShowOptions
            {
                Position  = at,
                Placement = FlyoutPlacementMode.Top,
            });
        }
        catch (Exception ex)
        {
            Logger.Error("[PDF Selection] Copy flyout failed", ex);
        }
    }

    private void OnContextMenuOpening(object sender, object e)
    {
        if (_suppressContextMenu)
        {
            _suppressContextMenu = false;
            if (sender is MenuFlyout flyout)
                flyout.Hide();
        }
        CopyMenuItem.IsEnabled = !string.IsNullOrEmpty(_selectedText);
    }

    private void OnPopupCopyClick(object sender, RoutedEventArgs e)
    {
        CopyFlyout.Hide();
        CopySelectionToClipboard();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e) => CopySelectionToClipboard();

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        CopyFlyout.Hide();
        if (string.IsNullOrEmpty(_pageText)) return;

        _selectionStartCharIndex = 0;
        _selectionEndCharIndex   = Math.Max(0, _pageText.Length - 1);
        if (!CommitSelectionText()) return;
        PaintSelection();
        CopySelectionToClipboard();
    }

    private void CopySelectionToClipboard()
    {
        if (string.IsNullOrEmpty(_selectedText)) return;
        try
        {
            var pkg = new DataPackage();
            pkg.SetText(_selectedText);
            Clipboard.SetContent(pkg);
            Logger.Log($"[PDF Selection] Copied {_selectedText.Length} chars");
        }
        catch (Exception ex) { Logger.Error("[PDF Selection] Clipboard failed", ex); }
    }

    private void ClearSelection()
    {
        try { CopyFlyout.Hide(); } catch { }
        _selectionStartCharIndex = -1;
        _selectionEndCharIndex   = -1;
        _selectedText            = string.Empty;
        ClearSelectionVisuals();
    }

    private void ClearSelectionVisuals() => SelectionCanvas.Children.Clear();

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        if ((ctrl & Windows.UI.Core.CoreVirtualKeyStates.Down) == 0) return;

        if (e.Key == VirtualKey.C)
        {
            CopySelectionToClipboard();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.A)
        {
            OnSelectAllClick(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }
}
