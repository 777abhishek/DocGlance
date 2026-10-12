using QuickView.Rendering.Pdf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;

namespace QuickView.Features.Viewer.TextSelection.Providers;

/// <summary>
/// Text selection provider for PDF documents using PDFium APIs.
/// Provides pixel-perfect selection using FPDFText_GetCharIndexAtPos and coordinate transforms.
/// </summary>
public sealed class PdfTextSelectionProvider : ITextSelectionProvider
{
    private readonly PdfDocumentState _state;
    private int _currentPageIndex;
    private double _zoom = 1.0;
    private double _rotation = 0;
    private Point _scrollOffset;
    private int? _cachedCharCount;

    public PdfTextSelectionProvider(
        PdfDocumentState state,
        int currentPageIndex,
        double zoom = 1.0,
        double rotation = 0)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _currentPageIndex = currentPageIndex;
        _zoom = zoom;
        _rotation = rotation;
    }

    public void UpdateTransform(double zoom, double rotation, Point scrollOffset)
    {
        _zoom = zoom;
        _rotation = rotation;
        _scrollOffset = scrollOffset;
        _cachedCharCount = null; // Invalidate cache on page/zoom change
    }

    public void SetCurrentPage(int pageIndex)
    {
        if (_currentPageIndex != pageIndex)
        {
            _currentPageIndex = pageIndex;
            _cachedCharCount = null;
        }
    }

    public int? GetCharIndexAtPosition(Point viewportPoint)
    {
        if (_state == null || _currentPageIndex < 0 || _currentPageIndex >= _state.PageCount)
            return null;

        // Convert viewport point to device coordinates (accounting for scroll and zoom)
        double deviceX = (viewportPoint.X + _scrollOffset.X) / _zoom;
        double deviceY = (viewportPoint.Y + _scrollOffset.Y) / _zoom;

        // Call PDFium API synchronously (runs under Pdfium.Gate)
        // NOTE: This blocks but is typically fast (<1ms)
        var task = PdfTextService.GetCharIndexAtPositionAsync(
            _state,
            _currentPageIndex,
            deviceX,
            deviceY);

        task.Wait(); // Synchronous wait - acceptable for mouse events
        int charIndex = task.Result;

        return charIndex >= 0 ? charIndex : null;
    }

    public IReadOnlyList<Rect> GetSelectionRectangles(int startIndex, int endIndex)
    {
        if (_state == null || _currentPageIndex < 0 || _currentPageIndex >= _state.PageCount)
            return Array.Empty<Rect>();

        if (startIndex < 0 || endIndex < startIndex)
            return Array.Empty<Rect>();

        // Normalize indices
        int start = Math.Min(startIndex, endIndex);
        int end = Math.Max(startIndex, endIndex);
        int count = end - start + 1;

        // Get rectangles using PDFium
        var pdfRects = GetPdfSelectionRectangles(start, count);

        // Convert from device coordinates to viewport coordinates (accounting for zoom and scroll)
        var viewportRects = new List<Rect>();
        foreach (var pdfRect in pdfRects)
        {
            var viewportRect = new Rect(
                pdfRect.X * _zoom - _scrollOffset.X,
                pdfRect.Y * _zoom - _scrollOffset.Y,
                pdfRect.Width * _zoom,
                pdfRect.Height * _zoom
            );
            viewportRects.Add(viewportRect);
        }

        return viewportRects;
    }

    public string ExtractText(int startIndex, int endIndex)
    {
        if (_state == null || _currentPageIndex < 0 || _currentPageIndex >= _state.PageCount)
            return string.Empty;

        if (startIndex < 0 || endIndex < startIndex)
            return string.Empty;

        // Call PDFium text extraction
        var task = PdfTextService.ExtractTextRangeAsync(
            _state,
            _currentPageIndex,
            Math.Min(startIndex, endIndex),
            Math.Max(startIndex, endIndex));

        task.Wait(); // Synchronous wait
        return task.Result ?? string.Empty;
    }

    public int GetCharacterCount()
    {
        if (_cachedCharCount.HasValue)
            return _cachedCharCount.Value;

        if (_state == null || _currentPageIndex < 0 || _currentPageIndex >= _state.PageCount)
            return 0;

        // Get character count from PDFium
        Pdfium.EnsureInitialized();
        lock (Pdfium.Gate)
        {
            IntPtr page = Pdfium.FPDF_LoadPage(_state.Handle, _currentPageIndex);
            if (page == IntPtr.Zero)
                return 0;

            try
            {
                IntPtr textPage = Pdfium.FPDFText_LoadPage(page);
                if (textPage == IntPtr.Zero)
                    return 0;

                try
                {
                    _cachedCharCount = Pdfium.FPDFText_CountChars(textPage);
                    return _cachedCharCount.Value;
                }
                finally
                {
                    Pdfium.FPDFText_ClosePage(textPage);
                }
            }
            finally
            {
                Pdfium.FPDF_ClosePage(page);
            }
        }
    }

    public void Cleanup()
    {
        _cachedCharCount = null;
    }

    // ── Private Helpers ───────────────────────────────────────────────────────

    private List<Rect> GetPdfSelectionRectangles(int startIndex, int count)
    {
        var rects = new List<Rect>();
        if (count <= 0) return rects;

        Pdfium.EnsureInitialized();
        lock (Pdfium.Gate)
        {
            IntPtr page = Pdfium.FPDF_LoadPage(_state.Handle, _currentPageIndex);
            if (page == IntPtr.Zero)
                return rects;

            try
            {
                IntPtr textPage = Pdfium.FPDFText_LoadPage(page);
                if (textPage == IntPtr.Zero)
                    return rects;

                try
                {
                    float pageWidthPt = Pdfium.FPDF_GetPageWidthF(page);
                    float pageHeightPt = Pdfium.FPDF_GetPageHeightF(page);
                    float renderDpi = PdfRenderer.GetRenderDpi(1.0f); // Use base DPI for coordinate calculations
                    int pageW = (int)Math.Max(1, Math.Round(pageWidthPt * renderDpi / 96.0));
                    int pageH = (int)Math.Max(1, Math.Round(pageHeightPt * renderDpi / 96.0));

                    // Get rotation parameter (0, 1, 2, 3)
                    int rotateParam = ((int)(_rotation / 90)) % 4;

                    // Get selection rectangles from PDFium
                    int rectCount = Pdfium.FPDFText_CountRects(textPage, startIndex, count);
                    for (int i = 0; i < rectCount; i++)
                    {
                        Pdfium.FPDFText_GetRect(textPage, i,
                            out double pLeft, out double pTop,
                            out double pRight, out double pBottom);

                        // Convert PDF coordinates to device coordinates
                        Pdfium.FPDF_PageToDevice(page, 0, 0, pageW, pageH, rotateParam,
                            pLeft, pTop, out int x1, out int y1);
                        Pdfium.FPDF_PageToDevice(page, 0, 0, pageW, pageH, rotateParam,
                            pRight, pBottom, out int x2, out int y2);

                        double x = Math.Min(x1, x2);
                        double y = Math.Min(y1, y2);
                        double w = Math.Abs(x2 - x1);
                        double h = Math.Abs(y2 - y1);

                        if (w >= 1 && h >= 1)
                            rects.Add(new Rect(x, y, w, h));
                    }
                }
                finally
                {
                    Pdfium.FPDFText_ClosePage(textPage);
                }
            }
            finally
            {
                Pdfium.FPDF_ClosePage(page);
            }
        }

        return rects;
    }
}
