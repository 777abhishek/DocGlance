using Microsoft.UI.Xaml.Media.Imaging;
using QuickView.Common.Validators;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Rendering.Pdf;

public readonly record struct PdfOpenResult(PdfDocumentState? Document, string? Error, bool NeedsPassword = false)
{
    public bool Ok => Document != null;
}

/// <summary>
/// High-performance PDF page and thumbnail renderer using direct PDFium interop.
/// Rasters into rented BGRA buffers and writes to WriteableBitmap.
/// </summary>
public static class PdfRenderer
{
    public const float RenderDpi    = 192f;
    public const float ViewerDpi    = RenderDpi;
    public const float ThumbnailDpi = 96f;

    /// <summary>Alias for the process-wide PDFium lock.</summary>
    internal static object PdfiumLock => Pdfium.Gate;

    public static void EnsureInitialized() => Pdfium.EnsureInitialized();

    public static async Task<PdfOpenResult> OpenDocumentAsync(string filePath, CancellationToken ct = default, string? password = null)
    {
        // Validate path for security and file size
        if (!FileSizeValidator.IsFileValidAndSafe(filePath, out var validationError))
            return new PdfOpenResult(null, $"Validation failed: {validationError}");

        return await Task.Run(() =>
        {
            EnsureInitialized();
            lock (Pdfium.Gate)
            {
                if (ct.IsCancellationRequested) return new PdfOpenResult(null, null);

                IntPtr doc = Pdfium.FPDF_LoadDocument(filePath, password);
                if (doc == IntPtr.Zero)
                {
                    // F5: Check if password is required
                    uint lastError = Pdfium.FPDF_GetLastError();
                    bool needsPassword = (lastError == Pdfium.ErrPassword);
                    return new PdfOpenResult(null, Pdfium.DescribeLastError(), needsPassword);
                }

                if (ct.IsCancellationRequested)
                {
                    Pdfium.FPDF_CloseDocument(doc);
                    return new PdfOpenResult(null, null);
                }

                int pageCount = Pdfium.FPDF_GetPageCount(doc);
                var pageSizes = new List<(double Width, double Height)>(pageCount);

                for (int i = 0; i < pageCount; i++)
                {
                    if (Pdfium.FPDF_GetPageSizeByIndexF(doc, i, out var size) != 0 && size.Width > 0 && size.Height > 0)
                    {
                        pageSizes.Add((size.Width, size.Height));
                    }
                    else
                    {
                        IntPtr page = Pdfium.FPDF_LoadPage(doc, i);
                        if (page != IntPtr.Zero)
                        {
                            try
                            {
                                pageSizes.Add((Pdfium.FPDF_GetPageWidthF(page), Pdfium.FPDF_GetPageHeightF(page)));
                            }
                            finally
                            {
                                Pdfium.FPDF_ClosePage(page);
                            }
                        }
                        else
                        {
                            pageSizes.Add((612, 792));
                        }
                    }
                }

                return new PdfOpenResult(new PdfDocumentState(doc, pageCount, pageSizes), null);
            }
        }, ct);
    }

    public static async Task<WriteableBitmap?> RenderPageAsync(
        PdfDocumentState state, int pageIndex, float dpi, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested || state == null) return null;

        var renderResult = await Task.Run(() =>
        {
            if (ct.IsCancellationRequested) return default;
            EnsureInitialized();

            lock (Pdfium.Gate)
            {
                if (ct.IsCancellationRequested) return default;
                IntPtr doc = state.Handle;
                if (doc == IntPtr.Zero) return default;

                IntPtr page = Pdfium.FPDF_LoadPage(doc, pageIndex);
                if (page == IntPtr.Zero) return default;

                try
                {
                    float ptW = Pdfium.FPDF_GetPageWidthF(page);
                    float ptH = Pdfium.FPDF_GetPageHeightF(page);

                    int width  = (int)Math.Max(1, Math.Round(ptW * dpi / 96.0));
                    int height = (int)Math.Max(1, Math.Round(ptH * dpi / 96.0));
                    int stride = width * 4;
                    int byteCount = stride * height;

                    byte[] buffer = ArrayPool<byte>.Shared.Rent(byteCount);

                    unsafe
                    {
                        fixed (byte* p = buffer)
                        {
                            IntPtr bitmap = Pdfium.FPDFBitmap_CreateEx(
                                width, height, Pdfium.BitmapBgra, (IntPtr)p, stride);
                            if (bitmap != IntPtr.Zero)
                            {
                                try
                                {
                                    Pdfium.FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);
                                    Pdfium.FPDF_RenderPageBitmap(
                                        bitmap, page, 0, 0, width, height, 0,
                                        Pdfium.RenderAnnot | Pdfium.RenderLcdText);
                                }
                                finally
                                {
                                    Pdfium.FPDFBitmap_Destroy(bitmap);
                                }
                            }
                        }
                    }

                    return (Buffer: buffer, Width: width, Height: height, Stride: stride, ByteCount: byteCount);
                }
                finally
                {
                    Pdfium.FPDF_ClosePage(page);
                }
            }
        }, ct);

        if (renderResult.Buffer == null || ct.IsCancellationRequested)
        {
            if (renderResult.Buffer != null)
                ArrayPool<byte>.Shared.Return(renderResult.Buffer);
            return null;
        }

        try
        {
            lock (Pdfium.Gate)
            {
                if (state.Handle == IntPtr.Zero) return null;
            }

            var writeableBmp = new WriteableBitmap(renderResult.Width, renderResult.Height);
            using (var stream = writeableBmp.PixelBuffer.AsStream())
            {
                stream.Write(renderResult.Buffer, 0, renderResult.ByteCount);
            }
            writeableBmp.Invalidate();
            return writeableBmp;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(renderResult.Buffer);
        }
    }
}
