using DocGlance.Core.Models;
using DocGlance.Infrastructure.Logging;
using DocGlance.Rendering.Pdf;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace DocGlance.Features.Viewer;

public sealed partial class ViewerPage
{
    private const int PdfTextMapCacheCapacity = 12;
    private readonly Dictionary<int, PdfPageTextMap> _pdfTextPageMaps = new();
    private readonly HashSet<int> _pdfTextMapsLoading = new();
    private readonly List<Rectangle> _pdfSelectionOverlays = new();
    private readonly DispatcherTimer _touchSelectionTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private int _pdfTextMapGeneration;
    private int _pdfSelectionPage = -1;
    private int _pdfSelectionAnchor = -1;
    private int _pdfSelectionFocus = -1;
    private bool _isPdfSelecting;
    private bool _pdfSelectionMoved;
    private bool _touchSelectionRequested;
    private Pointer? _activePdfPointer;
    private Pointer? _touchPointer;
    private Point _touchOrigin;
    private Point _selectionOrigin;
    private ScrollMode _previousHorizontalScrollMode;
    private ScrollMode _previousVerticalScrollMode;
    private bool _ownsTouchScroll;
    private Pointer? _pendingMousePointer;
    private int _pendingMousePage = -1;
    private Point _pendingMouseStart;
    private Point _pendingMouseEnd;
    private bool _pendingMouseReleased;

    private bool HasPdfTextSelection =>
        _pdfSelectionPage >= 0 && _pdfSelectionAnchor >= 0 && _pdfSelectionFocus >= 0;

    private void InitializePdfTextSelection()
    {
        PdfViewportCanvas.PointerPressed += PdfTextCanvas_PointerPressed;
        PdfViewportCanvas.PointerMoved += PdfTextCanvas_PointerMoved;
        PdfViewportCanvas.PointerReleased += PdfTextCanvas_PointerReleased;
        PdfViewportCanvas.PointerCanceled += PdfTextCanvas_PointerCanceled;
        PdfViewportCanvas.PointerCaptureLost += PdfTextCanvas_PointerCaptureLost;
        PdfViewportCanvas.RightTapped += PdfTextCanvas_RightTapped;
        _touchSelectionTimer.Tick += TouchSelectionTimer_Tick;

        var copyAccelerator = new KeyboardAccelerator
        {
            Key = VirtualKey.C,
            Modifiers = VirtualKeyModifiers.Control,
        };
        copyAccelerator.Invoked += (sender, args) =>
        {
            if (!HasPdfTextSelection || _pdfState == null)
                return;
            _ = CopyPdfTextSelectionAsync();
            args.Handled = true;
        };
        DocumentScrollViewer.KeyboardAccelerators.Add(copyAccelerator);
    }

    private void EnsurePdfTextPageMap(int pageIndex)
    {
        if (_pdfState == null || _pdfState.ManagedPdfDocument != null ||
            (uint)pageIndex >= (uint)_pdfState.PageCount ||
            _pdfTextPageMaps.ContainsKey(pageIndex) ||
            !_pdfTextMapsLoading.Add(pageIndex))
            return;

        _ = LoadPdfTextPageMapAsync(_pdfState, pageIndex, _pdfTextMapGeneration, _cts?.Token ?? default);
    }

    private async Task LoadPdfTextPageMapAsync(
        PdfDocumentState state,
        int pageIndex,
        int generation,
        CancellationToken cancellationToken)
    {
        try
        {
            PdfPageTextMap map = await PdfTextService.GetPageTextMapAsync(
                state, pageIndex, cancellationToken);
            if (!ReferenceEquals(_pdfState, state) || generation != _pdfTextMapGeneration)
                return;

            _pdfTextPageMaps[pageIndex] = map;
            TrimPdfTextPageMaps();
            TryStartPendingMouseSelection(pageIndex);
            TryStartTouchSelection();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.Error($"[PDFSelection] Failed to load text geometry for page {pageIndex + 1}", ex);
        }
        finally
        {
            _pdfTextMapsLoading.Remove(pageIndex);
        }
    }

    private void TrimPdfTextPageMaps()
    {
        if (_viewportState == null)
            return;

        int currentPage = _viewportState.CurrentPageIndex;
        while (_pdfTextPageMaps.Count > PdfTextMapCacheCapacity)
        {
            int pageToRemove = _pdfTextPageMaps.Keys
                .Where(page => page != currentPage && page != _pdfSelectionPage)
                .OrderByDescending(page => Math.Abs(page - currentPage))
                .FirstOrDefault(-1);
            if (pageToRemove < 0)
                break;
            _pdfTextPageMaps.Remove(pageToRemove);
        }
    }

    private void PdfTextCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_pdfState == null || _pdfState.ManagedPdfDocument != null)
            return;

        Point point = e.GetCurrentPoint(PdfViewportCanvas).Position;
        int page = GetPdfTextPageAt(point.Y);
        if (page < 0)
            return;

        if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
        {
            if (_pdfTextPageMaps.TryGetValue(page, out var map) &&
                TryExtendPdfSelection(point, page, map, e.Pointer))
            {
                e.Handled = true;
                return;
            }

            _touchPointer = e.Pointer;
            _touchOrigin = point;
            _touchSelectionRequested = false;
            _touchSelectionTimer.Start();
            if (!_pdfTextPageMaps.ContainsKey(page))
                EnsurePdfTextPageMap(page);
            return;
        }

        if (!e.GetCurrentPoint(PdfViewportCanvas).Properties.IsLeftButtonPressed)
            return;

        if (!_pdfTextPageMaps.TryGetValue(page, out var pageMap))
        {
            ClearPdfTextSelection();
            _pendingMousePointer = e.Pointer;
            _pendingMousePage = page;
            _pendingMouseStart = point;
            _pendingMouseEnd = point;
            _pendingMouseReleased = false;
            PdfViewportCanvas.CapturePointer(e.Pointer);
            EnsurePdfTextPageMap(page);
            e.Handled = true;
            return;
        }

        int glyph = HitTestPdfText(page, point, pageMap);
        if (glyph < 0)
        {
            ClearPdfTextSelection();
            return;
        }

        StartPdfTextSelection(page, glyph, point, e.Pointer, touch: false);
        e.Handled = true;
    }

    private void PdfTextCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        Point point = e.GetCurrentPoint(PdfViewportCanvas).Position;

        if (_pendingMousePointer?.PointerId == e.Pointer.PointerId)
        {
            _pendingMouseEnd = point;
            e.Handled = true;
            return;
        }

        if (_touchPointer?.PointerId == e.Pointer.PointerId && !_isPdfSelecting)
        {
            if (Math.Abs(point.X - _touchOrigin.X) + Math.Abs(point.Y - _touchOrigin.Y) > 6)
            {
                _touchSelectionTimer.Stop();
                _touchSelectionRequested = false;
                _touchPointer = null;
            }
            return;
        }

        if (!_isPdfSelecting || _activePdfPointer?.PointerId != e.Pointer.PointerId)
            return;

        if (Math.Abs(point.X - _selectionOrigin.X) + Math.Abs(point.Y - _selectionOrigin.Y) > 2)
            _pdfSelectionMoved = true;
        UpdatePdfTextSelection(point);
        e.Handled = true;
    }

    private void PdfTextCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        Point point = e.GetCurrentPoint(PdfViewportCanvas).Position;
        if (_pendingMousePointer?.PointerId == e.Pointer.PointerId)
        {
            _pendingMouseEnd = point;
            _pendingMouseReleased = true;
            PdfViewportCanvas.ReleasePointerCapture(e.Pointer);
            TryStartPendingMouseSelection(_pendingMousePage);
            e.Handled = true;
            return;
        }

        if (_touchPointer?.PointerId == e.Pointer.PointerId && !_isPdfSelecting)
        {
            _touchSelectionTimer.Stop();
            _touchSelectionRequested = false;
            _touchPointer = null;
            return;
        }

        if (!_isPdfSelecting || _activePdfPointer?.PointerId != e.Pointer.PointerId)
            return;

        if (Math.Abs(point.X - _selectionOrigin.X) + Math.Abs(point.Y - _selectionOrigin.Y) > 2)
            _pdfSelectionMoved = true;
        UpdatePdfTextSelection(point);
        FinishPdfTextSelection(cancelled: false);
        e.Handled = true;
    }

    private void PdfTextCanvas_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_pendingMousePointer?.PointerId == e.Pointer.PointerId)
            ResetPendingMouseSelection();
        if (_touchPointer?.PointerId == e.Pointer.PointerId)
        {
            _touchSelectionTimer.Stop();
            _touchSelectionRequested = false;
            _touchPointer = null;
        }
        if (_isPdfSelecting && _activePdfPointer?.PointerId == e.Pointer.PointerId)
            FinishPdfTextSelection(cancelled: true);
    }

    private void PdfTextCanvas_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_pendingMousePointer?.PointerId == e.Pointer.PointerId && !_pendingMouseReleased)
            ResetPendingMouseSelection();
        if (_isPdfSelecting && _activePdfPointer?.PointerId == e.Pointer.PointerId)
            FinishPdfTextSelection(cancelled: true);
    }

    private void TouchSelectionTimer_Tick(object? sender, object e)
    {
        _touchSelectionTimer.Stop();
        _touchSelectionRequested = true;
        TryStartTouchSelection();
    }

    private void TryStartTouchSelection()
    {
        if (!_touchSelectionRequested || _touchPointer == null || _pdfState == null)
            return;

        int page = GetPdfTextPageAt(_touchOrigin.Y);
        if (page < 0 || !_pdfTextPageMaps.TryGetValue(page, out var map))
            return;

        int glyph = HitTestPdfText(page, _touchOrigin, map);
        _touchSelectionRequested = false;
        if (glyph < 0)
        {
            _touchPointer = null;
            return;
        }

        Pointer pointer = _touchPointer;
        _touchPointer = null;
        StartPdfTextSelection(page, glyph, _touchOrigin, pointer, touch: true);
    }

    private void TryStartPendingMouseSelection(int page)
    {
        if (_pendingMousePointer == null || _pendingMousePage != page ||
            !_pdfTextPageMaps.TryGetValue(page, out var map))
            return;

        int anchor = HitTestPdfText(page, _pendingMouseStart, map);
        Pointer? pointer = _pendingMousePointer;
        Point start = _pendingMouseStart;
        Point end = _pendingMouseEnd;
        bool released = _pendingMouseReleased;
        bool moved = Math.Abs(end.X - start.X) + Math.Abs(end.Y - start.Y) > 2;
        ResetPendingMouseSelection();
        if (anchor < 0 || pointer == null)
            return;

        StartPdfTextSelection(page, anchor, start, pointer, touch: false, capturePointer: !released);
        _pdfSelectionMoved = moved;
        UpdatePdfTextSelection(end);
        if (released)
            FinishPdfTextSelection(cancelled: false);
    }

    private void ResetPendingMouseSelection()
    {
        _pendingMousePointer = null;
        _pendingMousePage = -1;
        _pendingMouseReleased = false;
    }

    private bool TryExtendPdfSelection(Point point, int page, PdfPageTextMap map, Pointer pointer)
    {
        if (!HasPdfTextSelection || page != _pdfSelectionPage)
            return false;

        var (x, y) = ToPdfTextPoint(page, point);
        int hit = map.HitTest(x, y);
        int start = Math.Min(_pdfSelectionAnchor, _pdfSelectionFocus);
        int end = Math.Max(_pdfSelectionAnchor, _pdfSelectionFocus);
        if (hit < 0 || !map.GetSelectionRects(start, end + 1).Any(rect =>
                x >= rect.X - 6 && x <= rect.X + rect.Width + 6 &&
                y >= rect.Y - 6 && y <= rect.Y + rect.Height + 6))
            return false;

        int anchor = Math.Abs(hit - start) < Math.Abs(hit - end) ? end : start;
        StartPdfTextSelection(page, hit, point, pointer, touch: true, anchorOverride: anchor);
        return true;
    }

    private void StartPdfTextSelection(
        int page,
        int glyph,
        Point point,
        Pointer pointer,
        bool touch,
        int? anchorOverride = null,
        bool capturePointer = true)
    {
        ClearPdfTextSelection();
        _pdfSelectionPage = page;
        _pdfSelectionAnchor = anchorOverride ?? glyph;
        _pdfSelectionFocus = glyph;
        _selectionOrigin = point;
        _pdfSelectionMoved = false;
        _activePdfPointer = pointer;
        _isPdfSelecting = true;

        if (touch)
        {
            _previousHorizontalScrollMode = DocumentScrollViewer.HorizontalScrollMode;
            _previousVerticalScrollMode = DocumentScrollViewer.VerticalScrollMode;
            DocumentScrollViewer.HorizontalScrollMode = ScrollMode.Disabled;
            DocumentScrollViewer.VerticalScrollMode = ScrollMode.Disabled;
            _ownsTouchScroll = true;
        }

        if (capturePointer)
            PdfViewportCanvas.CapturePointer(pointer);
        if (!touch)
            DocumentScrollViewer.Focus(FocusState.Pointer);
        DrawPdfTextSelection();
    }

    private void UpdatePdfTextSelection(Point point)
    {
        if (_pdfSelectionPage < 0 ||
            !_pdfTextPageMaps.TryGetValue(_pdfSelectionPage, out var map))
            return;

        int focus = HitTestPdfText(_pdfSelectionPage, point, map);
        if (focus < 0)
            return;

        _pdfSelectionFocus = focus;
        DrawPdfTextSelection();
    }

    private int HitTestPdfText(int page, Point canvasPoint, PdfPageTextMap map)
    {
        var (x, y) = ToPdfTextPoint(page, canvasPoint);
        return map.HitTest(x, y);
    }

    private (double X, double Y) ToPdfTextPoint(int page, Point canvasPoint)
    {
        double pageTop = GetPageTopLp(page);
        return (canvasPoint.X * LpToPt, (canvasPoint.Y - pageTop) * LpToPt);
    }

    private int GetPdfTextPageAt(double canvasY)
    {
        if (_pdfState == null)
            return -1;

        for (int page = 0; page < _pdfState.PageCount; page++)
        {
            double top = GetPageTopLp(page);
            double bottom = top + GetRotatedPageSize(page).H * PtToLp;
            if (canvasY <= bottom)
                return page;
        }

        return _pdfState.PageCount - 1;
    }

    private void DrawPdfTextSelection()
    {
        ClearPdfTextSelectionOverlay();
        if (_pdfSelectionPage < 0 ||
            !_pdfTextPageMaps.TryGetValue(_pdfSelectionPage, out var map))
            return;

        int start = Math.Min(_pdfSelectionAnchor, _pdfSelectionFocus);
        int end = Math.Max(_pdfSelectionAnchor, _pdfSelectionFocus) + 1;
        var fill = new SolidColorBrush(Color.FromArgb(105, 0, 120, 215));
        foreach (var rect in map.GetSelectionRects(start, end))
        {
            var overlay = new Rectangle
            {
                Width = rect.Width * PtToLp,
                Height = rect.Height * PtToLp,
                Fill = fill,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(overlay, rect.X * PtToLp);
            Canvas.SetTop(overlay, GetPageTopLp(_pdfSelectionPage) + rect.Y * PtToLp);
            PdfSelectionCanvas.Children.Add(overlay);
            _pdfSelectionOverlays.Add(overlay);
        }
    }

    private void ClearPdfTextSelectionOverlay()
    {
        foreach (Rectangle overlay in _pdfSelectionOverlays)
            PdfSelectionCanvas.Children.Remove(overlay);
        _pdfSelectionOverlays.Clear();
    }

    private void ClearPdfTextSelection()
    {
        _pdfSelectionPage = -1;
        _pdfSelectionAnchor = -1;
        _pdfSelectionFocus = -1;
        ClearPdfTextSelectionOverlay();
    }

    private void FinishPdfTextSelection(bool cancelled)
    {
        Pointer? pointer = _activePdfPointer;
        _activePdfPointer = null;
        _isPdfSelecting = false;
        if (_ownsTouchScroll)
        {
            DocumentScrollViewer.HorizontalScrollMode = _previousHorizontalScrollMode;
            DocumentScrollViewer.VerticalScrollMode = _previousVerticalScrollMode;
            _ownsTouchScroll = false;
        }
        if (pointer != null)
            PdfViewportCanvas.ReleasePointerCapture(pointer);

        if (cancelled || !_pdfSelectionMoved)
        {
            ClearPdfTextSelection();
            return;
        }

        DrawPdfTextSelection();
    }

    private async void PdfTextCanvas_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (!HasPdfTextSelection || _pdfState == null)
            return;

        e.Handled = true;
        PdfDocumentState state = _pdfState;
        int page = _pdfSelectionPage;
        Point menuPosition = e.GetPosition(PdfViewportCanvas);
        IReadOnlyList<PdfSearchRect> rectangles = GetPdfSelectionRects();
        if (rectangles.Count == 0)
            return;

        int deletableCount = 0;
        try
        {
            deletableCount = await PdfAnnotationService.CountRemovableAsync(
                state, page, rectangles, _cts?.Token ?? default);
        }
        catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
        {
            return;
        }
        catch (Exception ex)
        {
            await ShowPdfAnnotationErrorAsync("Could not inspect annotations", ex);
        }

        if (!ReferenceEquals(_pdfState, state) || !HasPdfTextSelection ||
            _pdfSelectionPage != page)
            return;

        var menu = new MenuFlyout();
        var copyItem = new MenuFlyoutItem
        {
            Text = "Copy",
            Icon = new SymbolIcon(Symbol.Copy),
        };
        copyItem.Click += async (_, _) => await CopyPdfTextSelectionAsync();
        menu.Items.Add(copyItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        AddPdfAnnotationMenuItem(menu, "Highlight", PdfTextAnnotationKind.Highlight);
        AddPdfAnnotationMenuItem(menu, "Underline", PdfTextAnnotationKind.Underline);
        AddPdfAnnotationMenuItem(menu, "Strikeout", PdfTextAnnotationKind.Strikeout);
        var noteItem = new MenuFlyoutItem
        {
            Text = "Add note",
            Icon = new SymbolIcon(Symbol.Comment),
        };
        noteItem.Click += async (_, _) => await AddPdfNoteAsync();
        menu.Items.Add(noteItem);
        if (deletableCount > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            var deleteItem = new MenuFlyoutItem
            {
                Text = deletableCount == 1 ? "Delete annotation" : $"Delete annotations ({deletableCount})",
                Icon = new SymbolIcon(Symbol.Delete),
            };
            deleteItem.Click += async (_, _) => await DeletePdfAnnotationsAsync();
            menu.Items.Add(deleteItem);
        }
        menu.ShowAt(PdfViewportCanvas, menuPosition);
    }

    private async Task CopyPdfTextSelectionAsync()
    {
        PdfDocumentState? state = _pdfState;
        int page = _pdfSelectionPage;
        int start = Math.Min(_pdfSelectionAnchor, _pdfSelectionFocus);
        int end = Math.Max(_pdfSelectionAnchor, _pdfSelectionFocus) + 1;
        if (state == null || page < 0 || start < 0 || end <= start)
            return;

        int generation = state.Generation;
        try
        {
            string text = await PdfTextService.GetTextRangeAsync(
                state, page, start, end, _cts?.Token ?? default);
            if (text.Length == 0 || !ReferenceEquals(_pdfState, state) ||
                state.Generation != generation)
                return;

            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
        {
        }
        catch (Exception ex)
        {
            Logger.Error("[PDFSelection] Could not copy selected text", ex);
        }
    }

    private void ClearPdfTextSelectionState()
    {
        _pdfTextMapGeneration++;
        _pdfTextPageMaps.Clear();
        _pdfTextMapsLoading.Clear();
        _touchSelectionTimer.Stop();
        _touchPointer = null;
        _touchSelectionRequested = false;
        Pointer? pendingMousePointer = _pendingMousePointer;
        ResetPendingMouseSelection();
        if (pendingMousePointer != null)
            PdfViewportCanvas.ReleasePointerCapture(pendingMousePointer);
        if (_isPdfSelecting)
            FinishPdfTextSelection(cancelled: true);
        else
            ClearPdfTextSelection();
    }
}
