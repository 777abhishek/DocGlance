using DocGlance.Core.Models;
using DocGlance.Infrastructure.Logging;
using DocGlance.Rendering.Docx;
using DocGlance.Rendering.Pptx;
using DocGlance.Rendering.Pdf.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace DocGlance.Features.Viewer;

public sealed partial class ViewerPage
{
    private readonly List<Rectangle> _selectionOverlays = new();
    private readonly List<Rectangle> _officeHighlightOverlays = new();
    private string? _selectedText;
    private OfficeTextPosition? _officeSelectionAnchor;
    private OfficeTextPosition? _officeSelectionFocus;
    private bool _isSelectingOfficeText;
    private uint _officeSelectionPointerId;

    private readonly record struct OfficeTextPosition(int PageIndex, int Offset);

    private void InitializeOfficeTextSelection()
    {
        AttachOfficeTextHandlers(PdfViewerControl.PdfViewportCanvasControl);
        AttachOfficeTextHandlers(DocxViewerControl.Canvas);
        AttachOfficeTextHandlers(PptxViewerControl.Canvas);
    }

    private void AttachOfficeTextHandlers(Panel contentPanel)
    {
        contentPanel.PointerPressed += OnOfficeContentPointerPressed;
        contentPanel.PointerMoved += OnOfficeContentPointerMoved;
        contentPanel.PointerReleased += OnOfficeContentPointerReleased;
        contentPanel.PointerCaptureLost += OnOfficeContentPointerCaptureLost;
        contentPanel.RightTapped += OnOfficeContentRightTapped;
        var copyAccelerator = new KeyboardAccelerator
        {
            Key = VirtualKey.C,
            Modifiers = VirtualKeyModifiers.Control,
        };
        copyAccelerator.Invoked += (_, args) =>
        {
            if (string.IsNullOrEmpty(_selectedText) ||
                (_docxDocument == null && _pptxDocument == null))
                return;

            CopyOfficeTextToClipboard();
            args.Handled = true;
        };
        ScrollViewer scrollViewer = contentPanel == DocxViewerControl.Canvas
            ? DocxViewerControl.ScrollViewer
            : contentPanel == PptxViewerControl.Canvas
                ? PptxViewerControl.ScrollViewer
                : PdfViewerControl.DocumentScrollViewerControl;
        scrollViewer.KeyboardAccelerators.Add(copyAccelerator);
    }

    private void OnOfficeContentPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_docxDocument == null && _pptxDocument == null)
            return;
        if (sender is not UIElement element ||
            !e.GetCurrentPoint(element).Properties.IsLeftButtonPressed)
            return;

        Point point = e.GetCurrentPoint(PdfViewportCanvas).Position;
        if (!IsPointInsideOfficePage(point))
            return;
        if (!TryGetOfficeTextPosition(point, out OfficeTextPosition position))
            return;

        ClearOfficeSelection();
        _selectedText = null;
        _officeSelectionAnchor = position;
        _officeSelectionFocus = position;
        _isSelectingOfficeText = true;
        _officeSelectionPointerId = e.Pointer.PointerId;
        element.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnOfficeContentPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isSelectingOfficeText || e.Pointer.PointerId != _officeSelectionPointerId)
            return;

        Point point = e.GetCurrentPoint(PdfViewportCanvas).Position;
        if (TryGetOfficeTextPosition(point, out OfficeTextPosition position, extendSelection: true))
        {
            _officeSelectionFocus = position;
            UpdateOfficeDragOverlay();
        }
    }

    private void OnOfficeContentPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isSelectingOfficeText || e.Pointer.PointerId != _officeSelectionPointerId)
            return;

        if (sender is UIElement element)
            element.ReleasePointerCapture(e.Pointer);

        Point point = e.GetCurrentPoint(PdfViewportCanvas).Position;
        if (TryGetOfficeTextPosition(point, out OfficeTextPosition position, extendSelection: true))
            _officeSelectionFocus = position;

        _isSelectingOfficeText = false;
        FinalizeOfficeTextSelection();
        e.Handled = true;
    }

    private void OnOfficeContentPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_isSelectingOfficeText && e.Pointer.PointerId == _officeSelectionPointerId)
            _isSelectingOfficeText = false;
    }

    private bool TryGetOfficeTextPosition(
        Point canvasPoint,
        out OfficeTextPosition position,
        bool extendSelection = false)
    {
        const double PtToLp = 96.0 / 72.0;

        if (_docxDocument != null)
        {
            int pageIndex = FindDocxPage(canvasPoint.Y);
            if (pageIndex < 0)
            {
                position = default;
                return false;
            }

            var (widthPt, heightPt) = _docxDocument.GetPageSize(pageIndex);
            Point pagePoint = PageRotation.UnrotatePoint(
                new Point(canvasPoint.X, canvasPoint.Y - GetDocxPageTopLp(pageIndex)),
                widthPt * PtToLp,
                heightPt * PtToLp,
                _rotation);
            var hit = _docxDocument.HitTest(
                pageIndex, pagePoint.X / PtToLp, pagePoint.Y / PtToLp);
            if (hit is not { } docxHit)
            {
                position = default;
                return false;
            }

            position = new OfficeTextPosition(docxHit.PageIndex, docxHit.Offset);
            return true;
        }

        if (_pptxDocument != null)
        {
            int slideIndex = PptxViewerControl.FindSlide(canvasPoint.Y);
            if (slideIndex < 0)
            {
                position = default;
                return false;
            }
            double slideTop = GetPptxPageTopLp(slideIndex);
            var (widthPt, heightPt) = _pptxDocument.GetSlideSize(slideIndex);
            Point pagePoint = PageRotation.UnrotatePoint(
                new Point(canvasPoint.X, canvasPoint.Y - slideTop),
                widthPt * PtToLp,
                heightPt * PtToLp,
                _rotation);
            var map = GetPptxTextMap(slideIndex);
            int offset = map.HitTest(
                pagePoint.X / PtToLp,
                pagePoint.Y / PtToLp,
                allowDistantHorizontalHit: extendSelection);
            if (offset < 0)
            {
                position = default;
                return false;
            }

            position = new OfficeTextPosition(slideIndex, offset);
            return true;
        }

        position = default;
        return false;
    }

    private bool IsPointInsideOfficePage(Point canvasPoint)
    {
        if (_docxDocument != null)
        {
            int pageIndex = FindDocxPage(canvasPoint.Y);
            return pageIndex >= 0 &&
                canvasPoint.Y >= GetDocxPageTopLp(pageIndex) &&
                canvasPoint.Y <= GetDocxPageTopLp(pageIndex) +
                    DocxViewerControl.GetPageHeightLp(pageIndex);
        }

        if (_pptxDocument != null)
        {
            int slideIndex = PptxViewerControl.FindSlide(canvasPoint.Y);
            if (slideIndex < 0)
                return false;
            double top = GetPptxPageTopLp(slideIndex);
            return canvasPoint.Y >= top &&
                canvasPoint.Y <= top + PptxViewerControl.GetSlideHeightLp(slideIndex);
        }
        return false;
    }

    private int FindDocxPage(double canvasY)
    {
        if (_docxDocument == null)
            return -1;

        return DocxViewerControl.FindPage(canvasY);
    }

    private readonly Dictionary<int, PdfPageTextMap> _pptxTextMaps = new();

    private PdfPageTextMap GetPptxTextMap(int slideIndex)
    {
        if (_pptxDocument == null)
            throw new InvalidOperationException("No presentation is open.");
        if (!_pptxTextMaps.TryGetValue(slideIndex, out var map))
        {
            if (_pptxTextMaps.Count >= 8)
                _pptxTextMaps.Remove(_pptxTextMaps.Keys.First());
            map = _pptxDocument.GetSlideTextMap(slideIndex);
            _pptxTextMaps.Add(slideIndex, map);
        }
        return map;
    }

    private void UpdateOfficeDragOverlay()
    {
        ClearOfficeSelection();
        if (_officeSelectionAnchor is not { } anchor || _officeSelectionFocus is not { } focus)
            return;

        var (start, end) = CompareOfficePositions(anchor, focus) <= 0
            ? (anchor, focus)
            : (focus, anchor);

        for (int page = start.PageIndex; page <= end.PageIndex; page++)
        {
            int offsetCount = _docxDocument != null
                ? _docxDocument.GetPageTextOffsetCount(page)
                : GetPptxTextMap(page).GlyphCount;
            int rangeStart = page == start.PageIndex ? start.Offset : 0;
            int rangeEnd = page == end.PageIndex ? end.Offset : offsetCount;
            rangeStart = Math.Clamp(rangeStart, 0, offsetCount);
            rangeEnd = Math.Clamp(rangeEnd, rangeStart, offsetCount);
            if (rangeStart == rangeEnd)
                continue;

            if (_docxDocument != null)
            {
                foreach (var rect in _docxDocument.GetSelectionRects(page, rangeStart, rangeEnd))
                    AddDocxSelectionOverlay(rect);
            }
            else
            {
                foreach (var rect in GetPptxTextMap(page).GetSelectionRects(rangeStart, rangeEnd))
                    AddPptxSelectionOverlay(page, rect);
            }
        }
    }

    private void FinalizeOfficeTextSelection()
    {
        if (_officeSelectionAnchor is not { } anchor || _officeSelectionFocus is not { } focus)
            return;

        try
        {
            var (start, end) = CompareOfficePositions(anchor, focus) <= 0
                ? (anchor, focus)
                : (focus, anchor);
            if (start == end)
            {
                ClearOfficeSelection();
                _selectedText = null;
                return;
            }

            ClearOfficeSelection();
            var text = new StringBuilder();
            int selectedRectangles = 0;
            for (int page = start.PageIndex; page <= end.PageIndex; page++)
            {
                int offsetCount = _docxDocument != null
                    ? _docxDocument.GetPageTextOffsetCount(page)
                    : GetPptxTextMap(page).GlyphCount;
                int rangeStart = page == start.PageIndex ? start.Offset : 0;
                int rangeEnd = page == end.PageIndex ? end.Offset : offsetCount;
                rangeStart = Math.Clamp(rangeStart, 0, offsetCount);
                rangeEnd = Math.Clamp(rangeEnd, rangeStart, offsetCount);
                if (page == start.PageIndex && page == end.PageIndex && rangeStart == rangeEnd)
                    continue;

                string pageText = _docxDocument != null
                    ? _docxDocument.GetTextRange(page, rangeStart, rangeEnd)
                    : _pptxDocument!.GetSlideTextRange(page, rangeStart, rangeEnd);
                if (page > start.PageIndex && text.Length > 0)
                    text.AppendLine();
                text.Append(pageText);

                if (_docxDocument != null)
                {
                    foreach (var rect in _docxDocument.GetSelectionRects(page, rangeStart, rangeEnd))
                    {
                        selectedRectangles++;
                        AddDocxSelectionOverlay(rect);
                    }
                }
                else
                {
                    foreach (var rect in GetPptxTextMap(page).GetSelectionRects(rangeStart, rangeEnd))
                    {
                        selectedRectangles++;
                        AddPptxSelectionOverlay(page, rect);
                    }
                }
            }

            _selectedText = text.ToString();
            if (!string.IsNullOrEmpty(_selectedText))
            {
                Logger.Log(
                    $"[OfficeSelection] Copied start={start.PageIndex}:{start.Offset}, end={end.PageIndex}:{end.Offset}, " +
                    $"selectionRects={selectedRectangles}, copiedLines={_selectedText.Count(character => character == '\n') + 1}, " +
                    $"chars={_selectedText.Length}");
                CopyOfficeTextToClipboard();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("[OfficeSelection] Failed to extract selection", ex);
            ClearOfficeSelection();
            ShowInfoBar($"Could not select document text: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private static int CompareOfficePositions(OfficeTextPosition left, OfficeTextPosition right)
    {
        int pageComparison = left.PageIndex.CompareTo(right.PageIndex);
        return pageComparison != 0 ? pageComparison : left.Offset.CompareTo(right.Offset);
    }

    private void AddDocxSelectionOverlay(DocxDocument.DocxMatch rect)
    {
        const double PtToLp = 96.0 / 72.0;
        var (widthPt, heightPt) = _docxDocument!.GetPageSize(rect.PageIndex);
        Rect rotated = PageRotation.RotateRect(
            new Rect(rect.X * PtToLp, rect.Y * PtToLp,
                rect.Width * PtToLp, rect.Height * PtToLp),
            widthPt * PtToLp,
            heightPt * PtToLp,
            _rotation);
        AddOfficeSelectionOverlay(new Rect(
            rotated.X,
            GetDocxPageTopLp(rect.PageIndex) + rotated.Y,
            rotated.Width,
            rotated.Height));
    }

    private void AddPptxSelectionOverlay(int slideIndex, PdfSearchRect rect)
    {
        const double PtToLp = 96.0 / 72.0;
        var (widthPt, heightPt) = _pptxDocument!.GetSlideSize(slideIndex);
        Rect rotated = PageRotation.RotateRect(
            new Rect(rect.X * PtToLp, rect.Y * PtToLp,
                rect.Width * PtToLp, rect.Height * PtToLp),
            widthPt * PtToLp,
            heightPt * PtToLp,
            _rotation);
        AddOfficeSelectionOverlay(new Rect(
            rotated.X,
            GetPptxPageTopLp(slideIndex) + rotated.Y,
            rotated.Width,
            rotated.Height));
    }

    private void AddOfficeSelectionOverlay(Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var overlay = new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(105, 0, 120, 215)),
            Width = rect.Width,
            Height = rect.Height,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(overlay, rect.X);
        Canvas.SetTop(overlay, rect.Y);
        Canvas.SetZIndex(overlay, 15);
        PdfViewportCanvas.Children.Add(overlay);
        _selectionOverlays.Add(overlay);
    }

    private void ClearOfficeSelection()
    {
        foreach (var overlay in _selectionOverlays)
            PdfViewportCanvas.Children.Remove(overlay);
        _selectionOverlays.Clear();
    }

    private void OnOfficeContentRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedText) ||
            (_docxDocument == null && _pptxDocument == null))
            return;

        var flyout = new MenuFlyout();
        var copyItem = new MenuFlyoutItem
        {
            Text = "Copy",
            Icon = new SymbolIcon(Symbol.Copy),
        };
        copyItem.Click += (_, _) => CopyOfficeTextToClipboard();
        flyout.Items.Add(copyItem);
        var translateItem = new MenuFlyoutItem
        {
            Text = "Translate selection",
            Icon = new SymbolIcon(Symbol.Globe),
        };
        translateItem.Click += (_, _) => _ = ShowTranslationDialogAsync(_selectedText);
        flyout.Items.Add(translateItem);
        var highlightItem = new MenuFlyoutItem
        {
            Text = "Highlight",
            Icon = new SymbolIcon(Symbol.Highlight),
        };
        highlightItem.Click += (_, _) => HighlightOfficeTextSelection();
        flyout.Items.Add(highlightItem);
        flyout.ShowAt(PdfViewportCanvas, e.GetPosition(PdfViewportCanvas));
        e.Handled = true;
    }

    private void HighlightOfficeTextSelection()
    {
        if (_officeSelectionAnchor is not { } anchor ||
            _officeSelectionFocus is not { } focus ||
            CompareOfficePositions(anchor, focus) == 0)
            return;

        ClearOfficeSelection();
        foreach (Rect rect in GetOfficeSelectionRectangles(anchor, focus))
        {
            var overlay = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(105, 255, 216, 0)),
                Width = rect.Width,
                Height = rect.Height,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(overlay, rect.X);
            Canvas.SetTop(overlay, rect.Y);
            Canvas.SetZIndex(overlay, 14);
            PdfViewportCanvas.Children.Add(overlay);
            _officeHighlightOverlays.Add(overlay);
        }
    }

    private IReadOnlyList<Rect> GetOfficeSelectionRectangles(
        OfficeTextPosition anchor,
        OfficeTextPosition focus)
    {
        var (start, end) = CompareOfficePositions(anchor, focus) <= 0
            ? (anchor, focus)
            : (focus, anchor);
        var rectangles = new List<Rect>();
        for (int page = start.PageIndex; page <= end.PageIndex; page++)
        {
            int offsetCount = _docxDocument != null
                ? _docxDocument.GetPageTextOffsetCount(page)
                : GetPptxTextMap(page).GlyphCount;
            int rangeStart = page == start.PageIndex ? start.Offset : 0;
            int rangeEnd = page == end.PageIndex ? end.Offset : offsetCount;
            rangeStart = Math.Clamp(rangeStart, 0, offsetCount);
            rangeEnd = Math.Clamp(rangeEnd, rangeStart, offsetCount);
            if (rangeStart == rangeEnd)
                continue;

            if (_docxDocument != null)
            {
                rectangles.AddRange(_docxDocument.GetSelectionRects(page, rangeStart, rangeEnd)
                    .Select(rect =>
                    {
                        var (widthPt, heightPt) = _docxDocument.GetPageSize(rect.PageIndex);
                        Rect rotated = PageRotation.RotateRect(
                            new Rect(
                                rect.X * 96.0 / 72.0,
                                rect.Y * 96.0 / 72.0,
                                rect.Width * 96.0 / 72.0,
                                rect.Height * 96.0 / 72.0),
                            widthPt * 96.0 / 72.0,
                            heightPt * 96.0 / 72.0,
                            _rotation);
                        return new Rect(
                            rotated.X,
                            GetDocxPageTopLp(rect.PageIndex) + rotated.Y,
                            rotated.Width,
                            rotated.Height);
                    }));
            }
            else
            {
                rectangles.AddRange(GetPptxTextMap(page).GetSelectionRects(rangeStart, rangeEnd)
                    .Select(rect =>
                    {
                        const double PtToLp = 96.0 / 72.0;
                        var (widthPt, heightPt) = _pptxDocument!.GetSlideSize(page);
                        Rect rotated = PageRotation.RotateRect(
                            new Rect(
                                rect.X * PtToLp,
                                rect.Y * PtToLp,
                                rect.Width * PtToLp,
                                rect.Height * PtToLp),
                            widthPt * PtToLp,
                            heightPt * PtToLp,
                            _rotation);
                        return new Rect(
                            rotated.X,
                            GetPptxPageTopLp(page) + rotated.Y,
                            rotated.Width,
                            rotated.Height);
                    }));
            }
        }
        return rectangles;
    }

    private void ClearOfficeHighlights()
    {
        foreach (var overlay in _officeHighlightOverlays)
            PdfViewportCanvas.Children.Remove(overlay);
        _officeHighlightOverlays.Clear();
    }

    private void CopyOfficeTextToClipboard()
    {
        if (string.IsNullOrEmpty(_selectedText))
            return;
        try
        {
            var data = new DataPackage();
            data.SetText(_selectedText);
            Clipboard.SetContent(data);
            Clipboard.Flush();
        }
        catch (Exception ex)
        {
            Logger.Error("[OfficeSelection] Could not copy selection", ex);
            ShowInfoBar($"Could not copy selected text: {ex.Message}", InfoBarSeverity.Error);
        }
    }
}
