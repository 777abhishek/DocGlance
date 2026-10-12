// ViewerPage.OfficeTextSelection.cs — Text selection and copy for DOCX, PPTX, XLSX
// Uses existing FFI search APIs to get character rectangles for drag-to-select and copy

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using QuickView.Infrastructure.Logging;
using QuickView.Rendering.Document;
using QuickView.Rendering.Presentation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace QuickView.Features.Viewer;

public sealed partial class ViewerPage
{
    // ── Selection state ────────────────────────────────────────────────────────
    private Point? _selectionStartPoint;
    private Point? _selectionEndPoint;
    private bool _isSelecting;
    private readonly List<Rectangle> _selectionOverlays = new();
    private string? _selectedText;

    // ── Selection initialization ───────────────────────────────────────────────
    
    private void InitializeOfficeTextSelection()
    {
        // Attach handlers to the DocumentScrollViewer content (where rendered pages live)
        if (DocumentScrollViewer?.Content is Panel contentPanel)
        {
            contentPanel.PointerPressed += OnOfficeContentPointerPressed;
            contentPanel.PointerMoved += OnOfficeContentPointerMoved;
            contentPanel.PointerReleased += OnOfficeContentPointerReleased;
            contentPanel.PointerCaptureLost += OnOfficeContentPointerCaptureLost;
        }
    }

    // ── Pointer handlers ───────────────────────────────────────────────────────

    private void OnOfficeContentPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Only handle left button for text selection
        if (!e.GetCurrentPoint(sender as UIElement).Properties.IsLeftButtonPressed)
            return;

        // Only enable selection in Office documents (not PDF, not text files)
        if (_pptxDocument == null && _docxDocument == null && _spreadsheetDocument == null)
            return;

        var point = e.GetCurrentPoint(sender as UIElement).Position;
        _selectionStartPoint = point;
        _selectionEndPoint = point;
        _isSelecting = true;
        _selectedText = null;

        ClearOfficeSelection();

        if (sender is UIElement element)
            element.CapturePointer(e.Pointer);

        e.Handled = true;
    }

    private void OnOfficeContentPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isSelecting || _selectionStartPoint == null)
            return;

        _selectionEndPoint = e.GetCurrentPoint(sender as UIElement).Position;
        UpdateOfficeSelectionOverlay();
    }

    private void OnOfficeContentPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isSelecting)
            return;

        _isSelecting = false;
        _selectionEndPoint = e.GetCurrentPoint(sender as UIElement).Position;

        if (sender is UIElement element)
            element.ReleasePointerCapture(e.Pointer);

        // Finalize selection and extract text
        FinalizeOfficeSelection();
        e.Handled = true;
    }

    private void OnOfficeContentPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_isSelecting)
        {
            _isSelecting = false;
            Logger.Log("[OfficeSelection] Pointer capture lost");
        }
    }

    // ── Selection overlay ──────────────────────────────────────────────────────

    private void UpdateOfficeSelectionOverlay()
    {
        if (_selectionStartPoint == null || _selectionEndPoint == null)
            return;

        ClearOfficeSelection();

        // Draw a temporary selection rectangle while dragging
        var rect = GetSelectionRectangle(_selectionStartPoint.Value, _selectionEndPoint.Value);
        if (rect.Width > 2 && rect.Height > 2)
        {
            var overlay = new Rectangle
            {
                Fill = new SolidColorBrush(Colors.LightBlue) { Opacity = 0.3 },
                Stroke = new SolidColorBrush(Colors.Blue) { Opacity = 0.5 },
                StrokeThickness = 1,
                Width = rect.Width,
                Height = rect.Height
            };

            Canvas.SetLeft(overlay, rect.X);
            Canvas.SetTop(overlay, rect.Y);

            if (PdfViewportCanvas != null)
            {
                PdfViewportCanvas.Children.Add(overlay);
                _selectionOverlays.Add(overlay);
            }
        }
    }

    private Windows.Foundation.Rect GetSelectionRectangle(Point start, Point end)
    {
        double x = Math.Min(start.X, end.X);
        double y = Math.Min(start.Y, end.Y);
        double width = Math.Abs(end.X - start.X);
        double height = Math.Abs(end.Y - start.Y);
        return new Windows.Foundation.Rect(x, y, width, height);
    }

    private void ClearOfficeSelection()
    {
        if (PdfViewportCanvas != null)
        {
            foreach (var overlay in _selectionOverlays)
                PdfViewportCanvas.Children.Remove(overlay);
        }
        _selectionOverlays.Clear();
    }

    // ── Selection extraction ───────────────────────────────────────────────────

    private void FinalizeOfficeSelection()
    {
        if (_selectionStartPoint == null || _selectionEndPoint == null)
            return;

        var selectionRect = GetSelectionRectangle(_selectionStartPoint.Value, _selectionEndPoint.Value);

        // Minimum drag distance to trigger selection (avoid accidental clicks)
        if (selectionRect.Width < 5 && selectionRect.Height < 5)
        {
            ClearOfficeSelection();
            return;
        }

        try
        {
            if (_pptxDocument != null)
                ExtractPptxSelection(selectionRect);
            else if (_docxDocument != null)
                ExtractDocxSelection(selectionRect);
            else if (_spreadsheetDocument != null)
                ExtractSpreadsheetSelection(selectionRect);
        }
        catch (Exception ex)
        {
            Logger.Log($"[OfficeSelection] Error: {ex.Message}");
        }
    }

    // ── PowerPoint selection ──────────────────────────────────────────────────

    private void ExtractPptxSelection(Windows.Foundation.Rect selectionRect)
    {
        if (_pptxDocument == null) return;

        // Get the current slide index (1-based page to 0-based index)
        int slideIndex = _page - 1;
        if (slideIndex < 0 || slideIndex >= _pptxDocument.SlideCount)
            return;

        // Search for a dummy character to force FFI to return all character rectangles
        // We use the search infrastructure to get character positions
        var allMatches = _pptxDocument.SearchText(" ", caseSensitive: false);

        var selectedChars = new List<(int charIndex, int length, string text)>();

        // Filter matches that intersect with selection rectangle
        foreach (var match in allMatches.Where(m => m.SlideIndex == slideIndex))
        {
            foreach (var rect in match.Rects)
            {
                // Transform rectangle from slide coordinates to screen coordinates
                var screenRect = TransformPptxRectToScreen(rect, slideIndex);

                if (RectanglesIntersect(selectionRect, screenRect))
                {
                    // Extract text at this character position
                    // For simplicity, we'll get the full slide text and extract the range
                    selectedChars.Add((match.CharIndex, match.Length, string.Empty));
                }
            }
        }

        if (selectedChars.Count > 0)
        {
            // Get full slide text
            string slideText = GetPptxSlideText(slideIndex);
            _selectedText = ExtractTextFromCharIndices(slideText, selectedChars);

            if (!string.IsNullOrEmpty(_selectedText))
            {
                CopyToClipboard(_selectedText);
                HighlightSelection(selectedChars, slideIndex, isPptx: true);
            }
        }
    }

    private string GetPptxSlideText(int slideIndex)
    {
        if (_pptxDocument == null) return string.Empty;

        try
        {
            // Use existing GetSlideTitle or implement full text extraction
            return _pptxDocument.GetSlideTitle(slideIndex);
        }
        catch
        {
            return string.Empty;
        }
    }

    private Windows.Foundation.Rect TransformPptxRectToScreen(PptxSearchRect rect, int slideIndex)
    {
        // Transform from slide coordinate system to screen coordinates
        const double PtToLp = 96.0 / 72.0;
        const double PageGapLp = 12.0;

        var (slideWidthPt, slideHeightPt) = _pptxDocument!.GetSlideSize(slideIndex);
        double slideHeightLp = slideHeightPt * PtToLp;

        // Calculate Y offset for this slide
        double yOffset = slideIndex * (slideHeightLp + PageGapLp) + PageGapLp;

        // Apply zoom
        double scale = _zoom;

        return new Windows.Foundation.Rect(
            rect.X * PtToLp * scale,
            (yOffset + rect.Y * PtToLp) * scale,
            rect.Width * PtToLp * scale,
            rect.Height * PtToLp * scale
        );
    }

    // ── Word selection ─────────────────────────────────────────────────────────

    private void ExtractDocxSelection(Windows.Foundation.Rect selectionRect)
    {
        if (_docxDocument == null) return;

        // Similar approach: use search to get all character rectangles
        var allMatches = _docxDocument.SearchText(" ", caseSensitive: false);

        var selectedChars = new List<(int pageIndex, double x, double y)>();

        foreach (var match in allMatches)
        {
            foreach (var rect in match.Rects)
            {
                var screenRect = TransformDocxRectToScreen(rect.PageIndex, rect.X, rect.Y, rect.Width, rect.Height);

                if (RectanglesIntersect(selectionRect, screenRect))
                {
                    selectedChars.Add((rect.PageIndex, rect.X, rect.Y));
                }
            }
        }

        if (selectedChars.Count > 0)
        {
            // Get text from the selected page(s)
            var pageIndices = selectedChars.Select(c => c.pageIndex).Distinct().OrderBy(p => p);
            var textBuilder = new StringBuilder();

            foreach (int pageIndex in pageIndices)
            {
                string pageText = _docxDocument.ExtractPageText(pageIndex);
                textBuilder.AppendLine(pageText);
            }

            _selectedText = textBuilder.ToString().Trim();

            if (!string.IsNullOrEmpty(_selectedText))
            {
                CopyToClipboard(_selectedText);
                HighlightDocxSelection(selectedChars);
            }
        }
    }

    private Windows.Foundation.Rect TransformDocxRectToScreen(int pageIndex, double x, double y, double width, double height)
    {
        const double PtToLp = 96.0 / 72.0;

        // Get the top position of this page
        double pageTopLp = GetDocxPageTopLp(pageIndex);

        // Apply zoom
        double scale = _zoom;

        return new Windows.Foundation.Rect(
            x * PtToLp * scale,
            (pageTopLp + y * PtToLp) * scale,
            width * PtToLp * scale,
            height * PtToLp * scale
        );
    }

    // ── Excel selection ────────────────────────────────────────────────────────

    private void ExtractSpreadsheetSelection(Windows.Foundation.Rect selectionRect)
    {
        if (_spreadsheetDocument == null) return;

        // For spreadsheets, selection works at cell level
        // Get the current sheet
        int sheetIndex = SpreadsheetViewerControl.ActiveSheetIndex;
        string sheetText = _spreadsheetDocument.ExtractSheetText(sheetIndex);

        // For now, copy the full sheet text (cell-level selection is more complex)
        // TODO: Implement cell-based selection using visual coordinates
        _selectedText = sheetText;

        if (!string.IsNullOrEmpty(_selectedText))
        {
            CopyToClipboard(_selectedText);
            Logger.Log($"[OfficeSelection] Copied spreadsheet text: {_selectedText.Length} chars");
        }
    }

    // ── Helper methods ─────────────────────────────────────────────────────────

    private bool RectanglesIntersect(Windows.Foundation.Rect r1, Windows.Foundation.Rect r2)
    {
        return !(r1.Right < r2.Left ||
                 r1.Left > r2.Right ||
                 r1.Bottom < r2.Top ||
                 r1.Top > r2.Bottom);
    }

    private string ExtractTextFromCharIndices(string fullText, List<(int charIndex, int length, string text)> chars)
    {
        if (chars.Count == 0) return string.Empty;

        // Sort by character index
        var sorted = chars.OrderBy(c => c.charIndex).ToList();

        // Merge overlapping or adjacent ranges
        var ranges = new List<(int start, int end)>();
        int currentStart = sorted[0].charIndex;
        int currentEnd = sorted[0].charIndex + sorted[0].length;

        for (int i = 1; i < sorted.Count; i++)
        {
            int start = sorted[i].charIndex;
            int end = start + sorted[i].length;

            if (start <= currentEnd + 1)
            {
                // Merge
                currentEnd = Math.Max(currentEnd, end);
            }
            else
            {
                ranges.Add((currentStart, currentEnd));
                currentStart = start;
                currentEnd = end;
            }
        }
        ranges.Add((currentStart, currentEnd));

        // Extract text
        var result = new StringBuilder();
        foreach (var (start, end) in ranges)
        {
            int length = end - start;
            if (start >= 0 && start < fullText.Length)
            {
                int actualLength = Math.Min(length, fullText.Length - start);
                result.Append(fullText.Substring(start, actualLength));
            }
        }

        return result.ToString();
    }

    private void HighlightSelection(List<(int charIndex, int length, string text)> chars, int slideIndex, bool isPptx)
    {
        // Visual feedback: highlight the selected text
        ClearOfficeSelection();

        // TODO: Draw individual character rectangles
        // For now, we just keep the drag rectangle
        Logger.Log($"[OfficeSelection] Selected {chars.Count} character ranges");
    }

    private void HighlightDocxSelection(List<(int pageIndex, double x, double y)> chars)
    {
        ClearOfficeSelection();
        Logger.Log($"[OfficeSelection] Selected {chars.Count} characters from Word document");
    }

    private void CopyToClipboard(string text)
    {
        try
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(text);
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();

            Logger.Log($"[OfficeSelection] Copied to clipboard: {text.Length} chars");
            
            // Optional: Show a toast notification
            // ShowInfoBar("Text copied to clipboard", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Logger.Log($"[OfficeSelection] Clipboard error: {ex.Message}");
        }
    }
}
