using QuickView.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Rendering.Pdf;

/// <summary>
/// Detects and extracts clickable links from PDF pages using PDFium.
/// Supports web URLs, email links, and internal page references.
/// Refactored to use PdfOperationBase for cleaner resource management.
/// </summary>
public static class PdfLinkService
{
    public static async Task<IReadOnlyList<PdfLink>> GetPageLinksAsync(
        PdfDocumentState state, int pageIndex,
        CancellationToken cancellationToken = default)
    {
        if (state == null || pageIndex < 0 || pageIndex >= state.PageCount)
            return Array.Empty<PdfLink>();

        return await PdfOperationBase.ExecutePageOperationAsync(state, pageIndex, (doc, page) =>
        {
            var links = new List<PdfLink>();
            var (pageW, pageH) = PdfOperationBase.GetPageDimensions(page, PdfRenderer.RenderDpi);

            // 1. Extract web links from text
            IntPtr textPage = Pdfium.FPDFText_LoadPage(page);
            if (textPage != IntPtr.Zero)
            {
                try
                {
                    ExtractWebLinks(textPage, page, pageW, pageH, links);
                }
                finally { Pdfium.FPDFText_ClosePage(textPage); }
            }

            // 2. Extract link annotations (internal page refs, URIs)
            ExtractAnnotationLinks(doc, page, pageW, pageH, links);

            return (IReadOnlyList<PdfLink>)links;
        }, cancellationToken);
    }

    private static void ExtractWebLinks(
        IntPtr textPage, IntPtr page, int pageW, int pageH, List<PdfLink> links)
    {
        IntPtr linkPage = Pdfium.FPDFLink_LoadWebLinks(textPage);
        if (linkPage == IntPtr.Zero) return;

        try
        {
            int count = Pdfium.FPDFLink_CountWebLinks(linkPage);
            for (int i = 0; i < count; i++)
            {
                // Get URL string
                int buflen = Pdfium.FPDFLink_GetURL(linkPage, i, null, 0);
                if (buflen <= 0) continue;

                var buffer = new ushort[buflen];
                Pdfium.FPDFLink_GetURL(linkPage, i, buffer, buflen);
                string url = Encoding.Unicode.GetString(
                    System.Runtime.InteropServices.MemoryMarshal.AsBytes(buffer.AsSpan(0, buflen - 1)));

                if (string.IsNullOrWhiteSpace(url)) continue;

                // Get bounding rectangles
                int rectCount = Pdfium.FPDFLink_CountRects(linkPage, i);
                var rects = new List<PdfLinkRect>();

                for (int r = 0; r < rectCount; r++)
                {
                    if (Pdfium.FPDFLink_GetRect(linkPage, i, r,
                        out double left, out double top, out double right, out double bottom) != 0)
                    {
                        // Convert PDF coordinates to device pixels
                        Pdfium.FPDF_PageToDevice(page, 0, 0, pageW, pageH, 0,
                            left, top, out int x1, out int y1);
                        Pdfium.FPDF_PageToDevice(page, 0, 0, pageW, pageH, 0,
                            right, bottom, out int x2, out int y2);

                        double x = Math.Min(x1, x2);
                        double y = Math.Min(y1, y2);
                        double w = Math.Abs(x2 - x1);
                        double h = Math.Abs(y2 - y1);

                        if (w >= 1 && h >= 1)
                            rects.Add(new PdfLinkRect(x, y, w, h));
                    }
                }

                if (rects.Count > 0)
                {
                    var linkType = url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                        ? PdfLinkType.Email
                        : PdfLinkType.WebUrl;

                    links.Add(new PdfLink
                    {
                        Type = linkType,
                        Url = url,
                        BoundingRects = rects
                    });
                }
            }
        }
        finally { Pdfium.FPDFLink_CloseWebLinks(linkPage); }
    }

    private static void ExtractAnnotationLinks(
        IntPtr doc, IntPtr page, int pageW, int pageH, List<PdfLink> links)
    {
        int annotCount = Pdfium.FPDFPage_GetAnnotCount(page);
        for (int i = 0; i < annotCount; i++)
        {
            IntPtr annot = Pdfium.FPDFPage_GetAnnot(page, i);
            if (annot == IntPtr.Zero) continue;

            try
            {
                // Check if this is a link annotation
                int subtype = Pdfium.FPDFAnnot_GetSubtype(annot);
                if (subtype != Pdfium.FPDF_ANNOT_LINK) continue;

                // Get annotation rectangle
                if (Pdfium.FPDFAnnot_GetRect(annot, out var rect) == 0) continue;

                // Convert to device coordinates
                Pdfium.FPDF_PageToDevice(page, 0, 0, pageW, pageH, 0,
                    rect.Left, rect.Top, out int x1, out int y1);
                Pdfium.FPDF_PageToDevice(page, 0, 0, pageW, pageH, 0,
                    rect.Right, rect.Bottom, out int x2, out int y2);

                double x = Math.Min(x1, x2);
                double y = Math.Min(y1, y2);
                double w = Math.Abs(x2 - x1);
                double h = Math.Abs(y2 - y1);

                if (w < 1 || h < 1) continue;

                var rects = new List<PdfLinkRect> { new PdfLinkRect(x, y, w, h) };

                // Get link action
                IntPtr link = Pdfium.FPDFAnnot_GetLink(annot);
                if (link == IntPtr.Zero) continue;

                IntPtr action = Pdfium.FPDFLink_GetAction(link);
                if (action == IntPtr.Zero) continue;

                uint actionType = Pdfium.FPDFAction_GetType(action);

                if (actionType == Pdfium.FPDF_ACTION_URI)
                {
                    // External URI link
                    uint buflen = Pdfium.FPDFAction_GetURIPath(doc, action, IntPtr.Zero, 0);
                    if (buflen > 0)
                    {
                        var buffer = new byte[buflen];
                        unsafe
                        {
                            fixed (byte* p = buffer)
                            {
                                Pdfium.FPDFAction_GetURIPath(doc, action, (IntPtr)p, buflen);
                            }
                        }
                        string url = Encoding.UTF8.GetString(buffer, 0, (int)buflen - 1);

                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            var linkType = url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                                ? PdfLinkType.Email
                                : PdfLinkType.WebUrl;

                            links.Add(new PdfLink
                            {
                                Type = linkType,
                                Url = url,
                                BoundingRects = rects
                            });
                        }
                    }
                }
                else if (actionType == Pdfium.FPDF_ACTION_GOTO)
                {
                    // Internal page link
                    IntPtr dest = Pdfium.FPDFAction_GetDest(doc, action);
                    if (dest != IntPtr.Zero)
                    {
                        int targetPage = Pdfium.FPDFDest_GetDestPageIndex(doc, dest);
                        if (targetPage >= 0)
                        {
                            links.Add(new PdfLink
                            {
                                Type = PdfLinkType.InternalPage,
                                TargetPageIndex = targetPage,
                                BoundingRects = rects
                            });
                        }
                    }
                }
            }
            finally { Pdfium.FPDFPage_CloseAnnot(annot); }
        }
    }
}
