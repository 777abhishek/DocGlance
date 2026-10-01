using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using QuickView.Infrastructure.Logging;

namespace QuickView.Rendering.Pdf;

public enum MarkupKind
{
    Highlight = Pdfium.FPDF_ANNOT_SUBTYPE_HIGHLIGHT,
    Underline = Pdfium.FPDF_ANNOT_SUBTYPE_UNDERLINE,
    Strikeout = Pdfium.FPDF_ANNOT_SUBTYPE_STRIKEOUT,
}

public static class PdfAnnotationService
{
    /// <summary>
    /// Adds a text-markup annotation (highlight/underline/strikeout).
    /// Rectangles must be in DEVICE space (i.e. mouse coords from the UI overlay).
    /// </summary>
    public static async Task AddMarkupAsync(
        PdfDocumentState documentState, 
        int pageIndex, 
        MarkupKind kind, 
        IReadOnlyList<(double X, double Y, double W, double H)> deviceRects, 
        int deviceWidth, 
        int deviceHeight,
        byte r, byte g, byte b, byte a)
    {
        if (deviceRects.Count == 0) return;

        await Task.Run(() =>
        {
            lock (Pdfium.Gate)
            {
                IntPtr doc = documentState.Handle;
                if (doc == IntPtr.Zero) return;

                IntPtr page = Pdfium.FPDF_LoadPage(doc, pageIndex);
                if (page == IntPtr.Zero) return;

                try
                {
                    IntPtr annot = Pdfium.FPDFPage_CreateAnnot(page, (int)kind);
                    if (annot == IntPtr.Zero) return;

                    try
                    {
                        float minL = float.MaxValue, minB = float.MaxValue, maxR = float.MinValue, maxT = float.MinValue;
                        foreach (var rect in deviceRects)
                        {
                            Pdfium.FPDF_DeviceToPage(page, 0, 0, deviceWidth, deviceHeight, 0,
                                (int)Math.Round(rect.X), (int)Math.Round(rect.Y), 
                                out double x1, out double y1);
                                
                            Pdfium.FPDF_DeviceToPage(page, 0, 0, deviceWidth, deviceHeight, 0,
                                (int)Math.Round(rect.X + rect.W), (int)Math.Round(rect.Y + rect.H), 
                                out double x2, out double y2);

                            float left   = (float)Math.Min(x1, x2);
                            float right  = (float)Math.Max(x1, x2);
                            float bottom = (float)Math.Min(y1, y2);
                            float top    = (float)Math.Max(y1, y2);

                            var quad = new Pdfium.FS_QUADPOINTSF
                            {
                                X1 = left,  Y1 = top,
                                X2 = right, Y2 = top,
                                X3 = left,  Y3 = bottom,
                                X4 = right, Y4 = bottom
                            };

                            Pdfium.FPDFAnnot_AppendAttachmentPoints(annot, ref quad);
                            minL = Math.Min(minL, left);
                            minB = Math.Min(minB, bottom);
                            maxR = Math.Max(maxR, right);
                            maxT = Math.Max(maxT, top);
                        }

                        var boundingBox = new Pdfium.FS_RECTF { Left = minL, Bottom = minB, Right = maxR, Top = maxT };
                        Pdfium.FPDFAnnot_SetRect(annot, ref boundingBox);
                        Pdfium.FPDFAnnot_SetColor(annot, 0, r, g, b, a); // 0 = stroke/fill
                        
                        // Print flag (4) ensures it prints and saves properly
                        Pdfium.FPDFAnnot_SetFlags(annot, 4); 
                    }
                    finally
                    {
                        Pdfium.FPDFPage_CloseAnnot(annot);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("[Annotations] Failed to add markup", ex);
                }
                finally
                {
                    Pdfium.FPDF_ClosePage(page);
                }
            }
        });
    }

    /// <summary>
    /// Adds a freehand ink stroke annotation.
    /// Points must be in DEVICE space.
    /// </summary>
    public static async Task AddInkAsync(
        PdfDocumentState documentState, 
        int pageIndex, 
        IReadOnlyList<(double X, double Y)> devicePoints, 
        int deviceWidth, 
        int deviceHeight,
        byte r, byte g, byte b, byte a, 
        float lineWidth)
    {
        if (devicePoints.Count < 2) return;

        await Task.Run(() =>
        {
            lock (Pdfium.Gate)
            {
                IntPtr doc = documentState.Handle;
                if (doc == IntPtr.Zero) return;

                IntPtr page = Pdfium.FPDF_LoadPage(doc, pageIndex);
                if (page == IntPtr.Zero) return;

                try
                {
                    IntPtr annot = Pdfium.FPDFPage_CreateAnnot(page, Pdfium.FPDF_ANNOT_SUBTYPE_INK);
                    if (annot == IntPtr.Zero) return;

                    try
                    {
                        var points = new Pdfium.FS_POINTF[devicePoints.Count];
                        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                        
                        for (int i = 0; i < devicePoints.Count; i++)
                        {
                            Pdfium.FPDF_DeviceToPage(page, 0, 0, deviceWidth, deviceHeight, 0,
                                (int)Math.Round(devicePoints[i].X), (int)Math.Round(devicePoints[i].Y), 
                                out double px, out double py);
                                
                            points[i] = new Pdfium.FS_POINTF { X = (float)px, Y = (float)py };
                            minX = Math.Min(minX, (float)px);
                            minY = Math.Min(minY, (float)py);
                            maxX = Math.Max(maxX, (float)px);
                            maxY = Math.Max(maxY, (float)py);
                        }

                        float pad = lineWidth / 2 + 1;
                        var rect = new Pdfium.FS_RECTF { Left = minX - pad, Bottom = minY - pad, Right = maxX + pad, Top = maxY + pad };
                        Pdfium.FPDFAnnot_SetRect(annot, ref rect);
                        Pdfium.FPDFAnnot_SetColor(annot, 0, r, g, b, a);
                        Pdfium.FPDFAnnot_SetBorder(annot, 0, 0, lineWidth);
                        Pdfium.FPDFAnnot_AddInkStroke(annot, points, (UIntPtr)points.Length);
                        Pdfium.FPDFAnnot_SetFlags(annot, 4);
                    }
                    finally
                    {
                        Pdfium.FPDFPage_CloseAnnot(annot);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("[Annotations] Failed to add ink", ex);
                }
                finally
                {
                    Pdfium.FPDF_ClosePage(page);
                }
            }
        });
    }
}
