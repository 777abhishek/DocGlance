using DocGlance.Core.Models;
using DocGlance.Infrastructure.Logging;
using DocGlance.Rendering.Document;
using DocGlance.Rendering.Presentation;
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
    private string? _selectedText;
    private OfficeTextPosition? _officeSelectionAnchor;
    private OfficeTextPosition? _officeSelectionFocus;
    private bool _isSelectingOfficeText;
    private uint _officeSelectionPointerId;

    private readonly record struct OfficeTextPosition(int PageIndex, int Offset);

    private void InitializeOfficeTextSelection()
    {
        if (DocumentScrollViewer.Content is not Panel contentPanel)
            return;

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
        DocumentScrollViewer.KeyboardAccelerators.Add(copyAccelerator);
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
        if (TryGetOfficeTextPosition(point, out OfficeTextPosition position))
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
        if (TryGetOfficeTextPosition(point, out OfficeTextPosition position))
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

    private bool TryGetOfficeTextPosition(Point canvasPoint, out OfficeTextPosition position)
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

            double pageY = (canvasPoint.Y - GetDocxPageTopLp(pageIndex)) / PtToLp;
            var hit = _docxDocument.HitTest(pageIndex, canvasPoint.X / PtToLp, pageY);
            if (hit is not { } docxHit)
            {
                position = default;
                return false;
            }

            position = new OfficeTextPosition(docxHit.PageIndex, docxHit.Offset);
            return true;
        }

        if (_pptxDocument != null && _pptxDocument.SlideCount > 0)
        {
            var (_, heightPt) = _pptxDocument.GetSlideSize(0);
            double slideHeightLp = heightPt * PtToLp;
            double stride = slideHeightLp + 12;
            int slideIndex = Math.Clamp((int)Math.Floor((canvasPoint.Y - 20) / stride), 0, _pptxDocument.SlideCount - 1);
            double slideTop = 20 + slideIndex * stride;
            var map = GetPptxTextMap(slideIndex);
            int offset = map.HitTest(canvasPoint.X / PtToLp, (canvasPoint.Y - slideTop) / PtToLp);
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
        const double PtToLp = 96.0 / 72.0;
        if (_docxDocument != null)
        {
            int pageIndex = FindDocxPage(canvasPoint.Y);
            return pageIndex >= 0 &&
                canvasPoint.Y >= GetDocxPageTopLp(pageIndex) &&
                canvasPoint.Y <= GetDocxPageTopLp(pageIndex) + _docxPageHeightLp[pageIndex];
        }

        if (_pptxDocument != null && _pptxDocument.SlideCount > 0)
        {
            var (_, heightPt) = _pptxDocument.GetSlideSize(0);
            double slideHeight = heightPt * PtToLp;
            double stride = slideHeight + 12;
            int slideIndex = (int)Math.Floor((canvasPoint.Y - 20) / stride);
            if ((uint)slideIndex >= (uint)_pptxDocument.SlideCount)
                return false;
            double top = 20 + slideIndex * stride;
            return canvasPoint.Y >= top && canvasPoint.Y <= top + slideHeight;
        }
        return false;
    }

    private int FindDocxPage(double canvasY)
    {
        if (_docxDocument == null)
            return -1;

        int nearestPage = -1;
        double nearestDistance = double.MaxValue;
        for (int i = 0; i < _docxPageTopLp.Length; i++)
        {
            double top = _docxPageTopLp[i];
            double bottom = top + _docxPageHeightLp[i];
            if (canvasY >= top && canvasY <= bottom)
                return i;

            double distance = canvasY < top ? top - canvasY : canvasY - bottom;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestPage = i;
            }
        }
        return nearestPage;
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
                        AddDocxSelectionOverlay(rect);
                }
                else
                {
                    foreach (var rect in GetPptxTextMap(page).GetSelectionRects(rangeStart, rangeEnd))
                        AddPptxSelectionOverlay(page, rect);
                }
            }

            _selectedText = text.ToString();
            if (!string.IsNullOrEmpty(_selectedText))
                CopyOfficeTextToClipboard();
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
        AddOfficeSelectionOverlay(new Rect(
            rect.X * PtToLp,
            GetDocxPageTopLp(rect.PageIndex) + rect.Y * PtToLp,
            rect.Width * PtToLp,
            rect.Height * PtToLp));
    }

    private void AddPptxSelectionOverlay(int slideIndex, PdfSearchRect rect)
    {
        const double PtToLp = 96.0 / 72.0;
        var (_, heightPt) = _pptxDocument!.GetSlideSize(slideIndex);
        double slideTop = 20 + slideIndex * (heightPt * PtToLp + 12);
        AddOfficeSelectionOverlay(new Rect(
            rect.X * PtToLp,
            slideTop + rect.Y * PtToLp,
            rect.Width * PtToLp,
            rect.Height * PtToLp));
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
        flyout.ShowAt(PdfViewportCanvas, e.GetPosition(PdfViewportCanvas));
        e.Handled = true;
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
