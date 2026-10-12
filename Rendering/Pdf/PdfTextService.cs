using QuickView.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Rendering.Pdf;

/// <summary>
/// Text extraction and search against the already-open PDFium document.
/// All native work runs under <see cref="Pdfium.Gate"/>.
/// Refactored to use PdfOperationBase for cleaner resource management.
/// </summary>
public static class PdfTextService
{
    public static async Task<IReadOnlyList<PdfSearchResult>> SearchAsync(
        PdfDocumentState state, string query,
        bool caseSensitive = false,
        int rotationDegrees = 0,
        CancellationToken cancellationToken = default)
    {
        if (state == null || string.IsNullOrWhiteSpace(query))
            return Array.Empty<PdfSearchResult>();

        return await Task.Run(
            () => SearchCore(state, query, caseSensitive, rotationDegrees, cancellationToken),
            cancellationToken);
    }

    public static async Task<string> ExtractTextAsync(
        PdfDocumentState state, int? pageIndex = null,
        CancellationToken cancellationToken = default)
    {
        if (state == null) return string.Empty;

        return await Task.Run(
            () => ExtractTextCore(state, pageIndex, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Gets the character index at the given (x, y) device coordinates on a page.
    /// Returns -1 if no character is at that position.
    /// Used for text selection: map mouse click to character index.
    /// </summary>
    public static async Task<int> GetCharIndexAtPositionAsync(
        PdfDocumentState state, int pageIndex, double deviceX, double deviceY,
        CancellationToken cancellationToken = default)
    {
        if (state == null || pageIndex < 0 || pageIndex >= state.PageCount)
            return -1;

        return await PdfOperationBase.ExecuteTextPageOperationAsync(state, pageIndex, (doc, page, textPage) =>
        {
            // Use 100% zoom (1.0x) for coordinate calculations - search is zoom-independent
            var (pageW, pageH) = PdfOperationBase.GetPageDimensions(page, PdfRenderer.GetRenderDpi(1.0f));
            
            // Convert device coordinates to PDF page coordinates
            var (pageX, pageY) = PdfOperationBase.DeviceToPageCoordinates(page, pageW, pageH, 0, deviceX, deviceY);

            // Tolerance in PDF points (1pt = 1/72 inch)
            // Use ~15pt tolerance for easier clicking (about 5mm on screen)
            double tolerance = 15.0;
            int charIndex = Pdfium.FPDFText_GetCharIndexAtPos(textPage, pageX, pageY, tolerance, tolerance);
            
            // Log for debugging
            if (charIndex >= 0)
            {
                Infrastructure.Logging.Logger.Log($"[PDF Text] Found char {charIndex} at device({deviceX:F0},{deviceY:F0}) → page({pageX:F2},{pageY:F2})");
            }
            
            return charIndex;
        }, cancellationToken);
    }

    /// <summary>
    /// Extracts text between two character indices on a page.
    /// Used for copying selected text.
    /// </summary>
    public static async Task<string> ExtractTextRangeAsync(
        PdfDocumentState state, int pageIndex, int startCharIndex, int endCharIndex,
        CancellationToken cancellationToken = default)
    {
        if (state == null || pageIndex < 0 || pageIndex >= state.PageCount)
            return string.Empty;

        if (startCharIndex < 0 || endCharIndex < startCharIndex)
            return string.Empty;

        return await PdfOperationBase.ExecuteTextPageOperationAsync(state, pageIndex, (doc, page, textPage) =>
        {
            int charCount = endCharIndex - startCharIndex + 1;
            if (charCount <= 0) return string.Empty;

            var chars = new ushort[charCount + 1];
            int retrieved = Pdfium.FPDFText_GetText(textPage, startCharIndex, charCount, chars);
            
            if (retrieved <= 0) return string.Empty;

            int len = retrieved - 1; // Exclude null terminator
            while (len > 0 && chars[len - 1] == 0) len--;

            return System.Text.Encoding.Unicode.GetString(
                MemoryMarshalAsBytes(chars, len));
        }, cancellationToken);
    }

    private static IReadOnlyList<PdfSearchResult> SearchCore(
        PdfDocumentState state, string query, bool caseSensitive, int rotationDegrees, CancellationToken ct)
    {
        var results = new List<PdfSearchResult>();
        Pdfium.EnsureInitialized();

        // FEAT-003: Convert rotation degrees to PDFium rotation parameter (0, 1, 2, 3)
        int rotateParam = (rotationDegrees / 90) % 4;

        lock (Pdfium.Gate)
        {
            IntPtr doc = state.Handle;
            if (doc == IntPtr.Zero) return results;

            int pageCount = state.PageCount;
            uint flags = caseSensitive ? (uint)Pdfium.MatchCase : 0;

            for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                ct.ThrowIfCancellationRequested();

                IntPtr page = Pdfium.FPDF_LoadPage(doc, pageIndex);
                if (page == IntPtr.Zero) continue;
                try
                {
                    float pageWidthPt  = Pdfium.FPDF_GetPageWidthF(page);
                    float pageHeightPt = Pdfium.FPDF_GetPageHeightF(page);
                    // Use 100% zoom (1.0x) for text extraction - text content is zoom-independent
                    float renderDpi = PdfRenderer.GetRenderDpi(1.0f);
                    int pageW = (int)Math.Max(1, Math.Round(pageWidthPt  * renderDpi / 96.0));
                    int pageH = (int)Math.Max(1, Math.Round(pageHeightPt * renderDpi / 96.0));

                    IntPtr textPage = Pdfium.FPDFText_LoadPage(page);
                    if (textPage == IntPtr.Zero) continue;
                    try
                    {
                        IntPtr search = Pdfium.FPDFText_FindStart(textPage, query, flags, 0);
                        if (search == IntPtr.Zero) continue;
                        try
                        {
                            while (Pdfium.FPDFText_FindNext(search) != 0)
                            {
                                ct.ThrowIfCancellationRequested();

                                int charIndex = Pdfium.FPDFText_GetSchResultIndex(search);
                                int length    = Pdfium.FPDFText_GetSchCount(search);
                                // FEAT-003: Pass rotation parameter to coordinate transform
                                var rects = CollectRangeRects(textPage, page, pageW, pageH, charIndex, length, rotateParam);
                                if (rects.Count > 0)
                                    results.Add(new PdfSearchResult(pageIndex, charIndex, length, rects));
                            }
                        }
                        finally { Pdfium.FPDFText_FindClose(search); }
                    }
                    finally { Pdfium.FPDFText_ClosePage(textPage); }
                }
                finally { Pdfium.FPDF_ClosePage(page); }
            }
        }

        return results;
    }

    private static string ExtractTextCore(
        PdfDocumentState state, int? requestedPage, CancellationToken ct)
    {
        Pdfium.EnsureInitialized();
        lock (Pdfium.Gate)
        {
            IntPtr doc = state.Handle;
            if (doc == IntPtr.Zero) return string.Empty;

            var output = new StringBuilder();
            int pageCount = state.PageCount;
            int first = requestedPage ?? 0;
            int last  = requestedPage ?? (pageCount - 1);

            for (int i = first; i <= last && i < pageCount; i++)
            {
                ct.ThrowIfCancellationRequested();

                IntPtr page = Pdfium.FPDF_LoadPage(doc, i);
                if (page == IntPtr.Zero) continue;
                try
                {
                    IntPtr textPage = Pdfium.FPDFText_LoadPage(page);
                    if (textPage == IntPtr.Zero) continue;
                    try
                    {
                        int count = Pdfium.FPDFText_CountChars(textPage);
                        if (count > 0)
                        {
                            var chars = new ushort[count + 1];
                            Pdfium.FPDFText_GetText(textPage, 0, count, chars);
                            if (output.Length > 0) output.AppendLine();
                            int len = count;
                            while (len > 0 && chars[len - 1] == 0) len--;
                            output.Append(Encoding.Unicode.GetString(
                                MemoryMarshalAsBytes(chars, len)));
                        }
                    }
                    finally { Pdfium.FPDFText_ClosePage(textPage); }
                }
                finally { Pdfium.FPDF_ClosePage(page); }
            }
            return output.ToString();
        }
    }

    private static byte[] MemoryMarshalAsBytes(ushort[] chars, int charCount)
    {
        var bytes = new byte[charCount * 2];
        Buffer.BlockCopy(chars, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static List<PdfSearchRect> CollectRangeRects(
        IntPtr textPage, IntPtr page, int pageW, int pageH,
        int start, int count, int rotateParam = 0)
    {
        var rects = new List<PdfSearchRect>();
        if (count <= 0) return rects;

        int rectCount = Pdfium.FPDFText_CountRects(textPage, start, count);
        for (int i = 0; i < rectCount; i++)
        {
            Pdfium.FPDFText_GetRect(textPage, i, out double pLeft, out double pTop, out double pRight, out double pBottom);

            // FEAT-003: Pass rotation parameter to FPDF_PageToDevice for correct coordinate transform
            Pdfium.FPDF_PageToDevice(page, 0, 0, pageW, pageH, rotateParam, pLeft,  pTop,    out int x1, out int y1);
            Pdfium.FPDF_PageToDevice(page, 0, 0, pageW, pageH, rotateParam, pRight, pBottom, out int x2, out int y2);

            double x = Math.Min(x1, x2);
            double y = Math.Min(y1, y2);
            double w = Math.Abs(x2 - x1);
            double h = Math.Abs(y2 - y1);

            if (w >= 1 && h >= 1)
                rects.Add(new PdfSearchRect(x, y, w, h));
        }
        return rects;
    }
}
