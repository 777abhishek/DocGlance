using DocGlance.Core.Models;
using DocGlance.Rendering.Pdf.Models;
using DocGlance.Infrastructure.Logging;
using DocGlance.Rendering.Pdf;
using DocGlance.Features.Viewer.Components;
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
    private readonly DispatcherTimer _touchSelectionTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

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

    private void InitializePdfTextSelection()
    {
        PdfViewportCanvas.PointerPressed += PdfTextCanvas_PointerPressed;
        PdfViewportCanvas.PointerMoved += PdfTextCanvas_PointerMoved;
        PdfViewportCanvas.PointerReleased += PdfTextCanvas_PointerReleased;
        PdfViewportCanvas.PointerCanceled += PdfTextCanvas_PointerCanceled;
        PdfViewportCanvas.PointerCaptureLost += PdfTextCanvas_PointerCaptureLost;
        PdfViewportCanvas.RightTapped += PdfTextCanvas_RightTapped;
        _touchSelectionTimer.Tick += TouchSelectionTimer_Tick;
        DocumentScrollViewer.KeyDown += PdfDocumentScrollViewer_KeyDown;
    }

    private void PdfDocumentScrollViewer_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.C || _pdfState == null)
            return;

        var controlState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        if ((controlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == 0)
            return;

        if (PdfViewerControl.HasTextSelection)
            _ = CopyPdfTextSelectionAsync();
        e.Handled = true;
    }

    private void EnsurePdfTextPageMap(int pageIndex)
        => PdfViewerControl.EnsureTextPageMap(pageIndex);

    private Task<string?> GetPdfTextSelectionAsync() =>
        PdfViewerControl.GetSelectedTextAsync(
            PdfViewerControl.SelectedTextPageIndex,
            PdfViewerControl.SelectionAnchor,
            PdfViewerControl.SelectionFocus,
            _cts?.Token ?? default);

    private static void CopyPdfTextToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    private void PdfTextCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_pdfState == null || _pdfState.ManagedPdfDocument != null)
            return;

        Point point = e.GetCurrentPoint(PdfViewportCanvas).Position;
        int page = GetPdfTextPageAt(point.Y);
        if (page < 0)
            return;

        if (PdfViewerControl.ActiveAnnotationTool is PdfAnnotationTool.Note or PdfAnnotationTool.Text)
        {
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch ||
                e.GetCurrentPoint(PdfViewportCanvas).Properties.IsLeftButtonPressed)
            {
                var (x, y) = ToPdfTextPoint(page, point);
                if (PdfViewerControl.ActiveAnnotationTool == PdfAnnotationTool.Note)
                    _ = AddPdfNoteAtAsync(page, x, y);
                else
                    _ = AddPdfTextAtAsync(page, x, y);
                e.Handled = true;
            }
            return;
        }

        if (PdfViewerControl.ActiveAnnotationTool == PdfAnnotationTool.Image)
        {
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch ||
                e.GetCurrentPoint(PdfViewportCanvas).Properties.IsLeftButtonPressed)
            {
                var (x, y) = ToPdfTextPoint(page, point);
                _ = AddPdfImageAtAsync(page, x, y);
                e.Handled = true;
            }
            return;
        }

        if (PdfViewerControl.ActiveAnnotationTool is PdfAnnotationTool.Pen or
            PdfAnnotationTool.Signature or PdfAnnotationTool.Eraser)
        {
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch ||
                e.GetCurrentPoint(PdfViewportCanvas).Properties.IsLeftButtonPressed)
            {
                StartPdfInkStroke(e.Pointer, page, point);
                e.Handled = true;
            }
            return;
        }

        if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
        {
            if (PdfViewerControl.TryGetTextPageMap(page, out var map) &&
                TryExtendPdfSelection(point, page, map, e.Pointer))
            {
                e.Handled = true;
                return;
            }

            _touchPointer = e.Pointer;
            _touchOrigin = point;
            _touchSelectionRequested = false;
            _touchSelectionTimer.Start();
            if (!PdfViewerControl.HasTextPageMap(page))
                EnsurePdfTextPageMap(page);
            return;
        }

        if (!e.GetCurrentPoint(PdfViewportCanvas).Properties.IsLeftButtonPressed)
            return;

        if (!PdfViewerControl.TryGetTextPageMap(page, out var pageMap))
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

        if (_pdfInkPointer?.PointerId == e.Pointer.PointerId)
        {
            UpdatePdfInkStroke(point);
            e.Handled = true;
            return;
        }

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
        if (_pdfInkPointer?.PointerId == e.Pointer.PointerId)
        {
            UpdatePdfInkStroke(point);
            FinishPdfInkStroke(cancelled: false);
            e.Handled = true;
            return;
        }

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
        if (_pdfInkPointer?.PointerId == e.Pointer.PointerId)
            FinishPdfInkStroke(cancelled: true);
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
        if (_pdfInkPointer?.PointerId == e.Pointer.PointerId)
            FinishPdfInkStroke(cancelled: true);
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
        if (page < 0 || !PdfViewerControl.TryGetTextPageMap(page, out var map))
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
            !PdfViewerControl.TryGetTextPageMap(page, out var map))
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
        if (!PdfViewerControl.HasTextSelection || page != PdfViewerControl.SelectedTextPageIndex)
            return false;

        var (x, y) = ToPdfTextPoint(page, point);
        int hit = map.HitTest(x, y);
        int start = Math.Min(PdfViewerControl.SelectionAnchor, PdfViewerControl.SelectionFocus);
        int end = Math.Max(PdfViewerControl.SelectionAnchor, PdfViewerControl.SelectionFocus);
        if (hit < 0 || !PdfViewerControl.GetSelectionRects(page, start, end).Any(rect =>
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
        PdfViewerControl.UpdateTextSelectionRange(page, anchorOverride ?? glyph, glyph);
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
        if (PdfViewerControl.SelectedTextPageIndex < 0 ||
            !PdfViewerControl.TryGetTextPageMap(PdfViewerControl.SelectedTextPageIndex, out var map))
            return;

        var (x, y) = ToPdfTextPoint(PdfViewerControl.SelectedTextPageIndex, point);
        int focus = map.HitTest(x, y, allowDistantHorizontalHit: true);
        if (focus < 0)
            return;

        PdfViewerControl.UpdateTextSelectionRange(
            PdfViewerControl.SelectedTextPageIndex, PdfViewerControl.SelectionAnchor, focus);
        DrawPdfTextSelection();
    }

    private int HitTestPdfText(int page, Point canvasPoint, PdfPageTextMap map)
    {
        var (x, y) = ToPdfTextPoint(page, canvasPoint);
        return map.HitTest(x, y);
    }

    private (double X, double Y) ToPdfTextPoint(int page, Point canvasPoint)
    {
        if (_pdfState == null)
            return (0, 0);
        double pageTop = GetPageTopLp(page);
        var (widthPt, heightPt) = _pdfState!.GetPageSize(page);
        Point pagePoint = PageRotation.UnrotatePoint(
            new Point(canvasPoint.X, canvasPoint.Y - pageTop),
            widthPt * PtToLp,
            heightPt * PtToLp,
            _rotation);
        return (pagePoint.X * LpToPt, pagePoint.Y * LpToPt);
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
        if (PdfViewerControl.SelectedTextPageIndex < 0 ||
            !PdfViewerControl.TryGetTextPageMap(PdfViewerControl.SelectedTextPageIndex, out var map))
        {
            PdfViewerControl.ClearTextSelection();
            return;
        }

        int start = Math.Min(PdfViewerControl.SelectionAnchor, PdfViewerControl.SelectionFocus);
        int end = Math.Max(PdfViewerControl.SelectionAnchor, PdfViewerControl.SelectionFocus) + 1;
        PdfViewerControl.SetTextSelection(
            PdfViewerControl.SelectedTextPageIndex,
            PdfViewerControl.GetSelectionRects(PdfViewerControl.SelectedTextPageIndex, start, end - 1));
    }

    private void ClearPdfTextSelection()
    {
        PdfViewerControl.ClearTextSelectionRange();
        PdfViewerControl.ClearTextSelection();
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
        if (PdfViewerControl.IsMarkupToolActive && PdfViewerControl.HasTextSelection)
            _ = AddPdfMarkupAsync(PdfViewerControl.MarkupKind);
    }

    private async void PdfTextCanvas_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (!PdfViewerControl.HasTextSelection || _pdfState == null)
            return;

        e.Handled = true;
        PdfDocumentState state = _pdfState;
        int page = PdfViewerControl.SelectedTextPageIndex;
        Point menuPosition = e.GetPosition(PdfViewportCanvas);
        IReadOnlyList<PdfSearchRect> rectangles = PdfViewerControl.GetSelectionRects(
            page, PdfViewerControl.SelectionAnchor, PdfViewerControl.SelectionFocus);
        if (rectangles.Count == 0)
            return;

        string? selectedText;
        try
        {
            selectedText = await GetPdfTextSelectionAsync();
        }
        catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
        {
            return;
        }
        catch (Exception ex)
        {
            Logger.Error("[PDFSelection] Could not extract the selected text.", ex);
            ShowInfoBar("Could not read the selected text.", InfoBarSeverity.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(selectedText) ||
            !ReferenceEquals(_pdfState, state) ||
            !PdfViewerControl.HasTextSelection ||
            PdfViewerControl.SelectedTextPageIndex != page)
            return;

        int deletableCount = 0;
        try
        {
            deletableCount = await PdfViewerControl.CountRemovableAnnotationsAsync(
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

        if (!ReferenceEquals(_pdfState, state) || !PdfViewerControl.HasTextSelection ||
            PdfViewerControl.SelectedTextPageIndex != page)
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
        int page = PdfViewerControl.SelectedTextPageIndex;
        int anchor = PdfViewerControl.SelectionAnchor;
        int focus = PdfViewerControl.SelectionFocus;
        try
        {
            string? text = await PdfViewerControl.GetSelectedTextAsync(
                page, anchor, focus, _cts?.Token ?? default);
            if (!string.IsNullOrEmpty(text))
            {
                CopyPdfTextToClipboard(text);
                int selectedLines = PdfViewerControl.GetSelectionRects(page, anchor, focus).Count;
                int copiedLines = text.Count(character => character == '\n') + 1;
                Logger.Log(
                    $"[PDFSelection] Copied page={page + 1}, glyphRange={Math.Min(anchor, focus)}-{Math.Max(anchor, focus) + 1}, " +
                    $"selectionRects={selectedLines}, copiedLines={copiedLines}, chars={text.Length}");
            }
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
        if (_pdfInkPointer != null)
            FinishPdfInkStroke(cancelled: true);
        PdfViewerControl.ResetTextPageMaps();
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
