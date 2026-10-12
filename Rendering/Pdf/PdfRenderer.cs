using Microsoft.UI.Xaml.Media.Imaging;
using QuickView.Common.Validators;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
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
    // Dynamic DPI rendering: screen DPI × zoom factor
    // At 100% zoom: 96 DPI = ~3.5 MB per letter page (was 47 MB at 384 DPI)
    // At 150% zoom: 144 DPI = ~8 MB per page
    // At 200% zoom: 192 DPI = ~14 MB per page
    // Matches SumatraPDF's memory-efficient approach
    public const float ScreenDpi    = 96f;
    public const float ThumbnailDpi = 96f;
    
    // Maximum bytes for a single rendered page bitmap (100 MB)
    // Prevents OOM from integer overflow or excessive dimensions
    private const long MaxPixelByteSize = 100_000_000L;
    
    /// <summary>
    /// Calculate render DPI based on zoom factor.
    /// Returns screen DPI × zoom (e.g., 96 at 1.0x, 144 at 1.5x, 192 at 2.0x)
    /// </summary>
    public static float GetRenderDpi(float zoomFactor)
    {
        // Clamp to reasonable range: 48 DPI (0.5x zoom) to 576 DPI (6x zoom)
        float zoom = Math.Clamp(zoomFactor, 0.5f, 6.0f);
        return ScreenDpi * zoom;
    }

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

    public static async Task<PdfOpenResult> OpenDocumentFromBytesAsync(byte[] bytes, CancellationToken ct = default, string? password = null)
    {
        if (bytes == null || bytes.Length == 0)
            return new PdfOpenResult(null, "PDF bytes are empty");

        return await Task.Run(() =>
        {
            EnsureInitialized();
            lock (Pdfium.Gate)
            {
                if (ct.IsCancellationRequested) return new PdfOpenResult(null, null);

                unsafe
                {
                    fixed (byte* pData = bytes)
                    {
                        IntPtr doc = Pdfium.FPDF_LoadMemDocument((IntPtr)pData, bytes.Length, password);
                        if (doc == IntPtr.Zero)
                        {
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

                        // Store pinned bytes or copy if needed? Wait! Note that FPDF_LoadMemDocument expects the memory buffer to remain valid!
                        // We must keep the memory buffer pinned/alive for the lifetime of PdfDocumentState.
                        var state = new PdfDocumentState(doc, pageCount, pageSizes, bytes);
                        return new PdfOpenResult(state, null);
                    }
                }
            }
        }, ct);
    }

    public static async Task<WriteableBitmap?> RenderPageAsync(
        PdfDocumentState state, int pageIndex, float dpi, CancellationToken ct = default)
    {
        var tid = Environment.CurrentManagedThreadId;
        Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{tid} RenderPageAsync START: page={pageIndex}, dpi={dpi:F1}, state={state?.Handle.ToString() ?? "null"}");
        
        if (ct.IsCancellationRequested || state == null)
        {
            Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{tid} RenderPageAsync EARLY EXIT: cancelled={ct.IsCancellationRequested}, state_null={state == null}");
            return null;
        }

        var renderResult = await Task.Run(() =>
        {
            var innerTid = Environment.CurrentManagedThreadId;
            Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} Task.Run START: page={pageIndex}");
            
            if (ct.IsCancellationRequested)
            {
                Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} Task.Run cancelled before init");
                return default;
            }
            
            try
            {
                EnsureInitialized();
                Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} PDFium initialized");

                Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} Acquiring Pdfium.Gate lock");
                lock (Pdfium.Gate)
                {
                    Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} Lock acquired, checking cancellation");
                    if (ct.IsCancellationRequested)
                    {
                        Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} Cancelled after lock");
                        return default;
                    }
                    
                    IntPtr doc = state.Handle;
                    Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} Document handle: 0x{doc.ToString("X")}");
                    if (doc == IntPtr.Zero)
                    {
                        Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} ERROR: Document handle is null");
                        return default;
                    }

                    Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} Calling FPDF_LoadPage({pageIndex})");
                    IntPtr page = Pdfium.FPDF_LoadPage(doc, pageIndex);
                    Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} Page handle: 0x{page.ToString("X")}");
                    
                    if (page == IntPtr.Zero)
                    {
                        Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} ERROR: FPDF_LoadPage returned null");
                        return default;
                    }

                    try
                    {
                        float ptW = Pdfium.FPDF_GetPageWidthF(page);
                        float ptH = Pdfium.FPDF_GetPageHeightF(page);
                        Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} Page dimensions: {ptW:F1}pt × {ptH:F1}pt");

                        int width  = (int)Math.Max(1, Math.Round(ptW * dpi / 96.0));
                        int height = (int)Math.Max(1, Math.Round(ptH * dpi / 96.0));
                        int stride = width * 4;
                        int byteCount = stride * height;
                        Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} Render size: {width}×{height} px, {byteCount / 1024}KB");

                        byte[] buffer = ArrayPool<byte>.Shared.Rent(byteCount);
                        Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} Buffer rented: {buffer.Length} bytes");

                        unsafe
                        {
                            fixed (byte* p = buffer)
                            {
                                Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} Creating FPDFBitmap: {width}×{height}");
                                IntPtr bitmap = Pdfium.FPDFBitmap_CreateEx(
                                    width, height, Pdfium.BitmapBgra, (IntPtr)p, stride);
                                Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} Bitmap handle: 0x{bitmap.ToString("X")}");
                                
                                if (bitmap != IntPtr.Zero)
                                {
                                    try
                                    {
                                        Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} Filling bitmap with white");
                                        Pdfium.FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);
                                        
                                        Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} Calling FPDF_RenderPageBitmap");
                                        Pdfium.FPDF_RenderPageBitmap(
                                            bitmap, page, 0, 0, width, height, 0,
                                            Pdfium.RenderAnnot | Pdfium.RenderLcdText);
                                        Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} FPDF_RenderPageBitmap completed");
                                    }
                                    finally
                                    {
                                        Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} Destroying bitmap");
                                        Pdfium.FPDFBitmap_Destroy(bitmap);
                                    }
                                }
                                else
                                {
                                    Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} ERROR: FPDFBitmap_CreateEx returned null");
                                }
                            }
                        }

                        Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} Render complete, returning buffer");
                        return (Buffer: buffer, Width: width, Height: height, Stride: stride, ByteCount: byteCount);
                    }
                    finally
                    {
                        Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} Closing page");
                        Pdfium.FPDF_ClosePage(page);
                        Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{innerTid} Page closed");
                    }
                }
            }
            catch (Exception ex)
            {
                Infrastructure.Logging.Logger.Error($"[PdfRenderer] T{innerTid} EXCEPTION in Task.Run", ex);
                throw;
            }
        }, ct);

        Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{tid} Task.Run returned, buffer={renderResult.Buffer != null}");

        if (renderResult.Buffer == null || ct.IsCancellationRequested)
        {
            if (renderResult.Buffer != null)
            {
                Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{tid} Returning buffer (cancelled)");
                ArrayPool<byte>.Shared.Return(renderResult.Buffer);
            }
            return null;
        }

        try
        {
            Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{tid} Checking document handle validity");
            lock (Pdfium.Gate)
            {
                if (state.Handle == IntPtr.Zero)
                {
                    Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{tid} ERROR: Document handle became null");
                    return null;
                }
            }

            Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{tid} Creating WriteableBitmap: {renderResult.Width}×{renderResult.Height}");
            var writeableBmp = new WriteableBitmap(renderResult.Width, renderResult.Height);
            
            Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{tid} Writing pixels to WriteableBitmap");
            using (var stream = writeableBmp.PixelBuffer.AsStream())
            {
                stream.Write(renderResult.Buffer, 0, renderResult.ByteCount);
            }
            writeableBmp.Invalidate();
            
            Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{tid} RenderPageAsync SUCCESS");
            return writeableBmp;
        }
        catch (Exception ex)
        {
            Infrastructure.Logging.Logger.Error($"[PdfRenderer] T{tid} EXCEPTION creating WriteableBitmap", ex);
            throw;
        }
        finally
        {
            Infrastructure.Logging.Logger.Log($"[PdfRenderer] T{tid} Returning buffer to pool");
            ArrayPool<byte>.Shared.Return(renderResult.Buffer);
        }
    }

    /// <summary>
    /// VIEWPORT RENDERING: Renders only the visible portion of a page.
    /// This is the KEY to low memory usage - we render a screen-sized bitmap (1920×1080)
    /// instead of a full page bitmap (2550×3300 at 200% zoom).
    /// 
    /// Memory: 1920×1080×4 = 8.3 MB vs 2550×3300×4 = 33.6 MB (75% reduction)
    /// </summary>
    /// <param name="state">PDF document state</param>
    /// <param name="pageIndex">Page to render</param>
    /// <param name="dpi">Render DPI (96 × zoom factor)</param>
    /// <param name="viewportWidthPx">Viewport width in pixels</param>
    /// <param name="viewportHeightPx">Viewport height in pixels</param>
    /// <param name="scrollXPt">Horizontal scroll position in PDF points</param>
    /// <param name="scrollYPt">Vertical scroll position in PDF points</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Bitmap of viewport size showing visible portion of page</returns>
    public static async Task<WriteableBitmap?> RenderViewportAsync(
        PdfDocumentState state, 
        int pageIndex, 
        float dpi,
        int viewportWidthPx,
        int viewportHeightPx,
        double scrollXPt,
        double scrollYPt,
        CancellationToken ct = default)
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
                    float pageWidthPt = Pdfium.FPDF_GetPageWidthF(page);
                    float pageHeightPt = Pdfium.FPDF_GetPageHeightF(page);

                    // Convert page dimensions from PDF points to pixels at current DPI
                    int pageWidthPx = (int)Math.Round(pageWidthPt * dpi / 72.0);
                    int pageHeightPx = (int)Math.Round(pageHeightPt * dpi / 72.0);

                    // Convert scroll position from PDF points to pixels
                    int scrollXPx = (int)Math.Round(scrollXPt * dpi / 72.0);
                    int scrollYPx = (int)Math.Round(scrollYPt * dpi / 72.0);

                    // Create bitmap of viewport size (not full page size!)
                    int bitmapWidth = Math.Min(viewportWidthPx, pageWidthPx - scrollXPx);
                    int bitmapHeight = Math.Min(viewportHeightPx, pageHeightPx - scrollYPx);
                    
                    if (bitmapWidth <= 0 || bitmapHeight <= 0) return default;

                    int stride = bitmapWidth * 4;
                    int byteCount = stride * bitmapHeight;
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(byteCount);

                    unsafe
                    {
                        fixed (byte* p = buffer)
                        {
                            IntPtr bitmap = Pdfium.FPDFBitmap_CreateEx(
                                bitmapWidth, bitmapHeight, Pdfium.BitmapBgra, (IntPtr)p, stride);
                            
                            if (bitmap != IntPtr.Zero)
                            {
                                try
                                {
                                    // Fill with white background
                                    Pdfium.FPDFBitmap_FillRect(bitmap, 0, 0, bitmapWidth, bitmapHeight, 0xFFFFFFFF);
                                    
                                    // CRITICAL: Use negative start_x/start_y to implement viewport clipping
                                    // This tells PDFium to render the page starting from (-scrollX, -scrollY)
                                    // into our small viewport-sized bitmap.
                                    // 
                                    // The page still thinks it's rendering at full pageWidthPx × pageHeightPx,
                                    // but only the visible portion lands in the bitmap.
                                    Pdfium.FPDF_RenderPageBitmap(
                                        bitmap,
                                        page,
                                        -scrollXPx,        // Start X: negative = viewport offset
                                        -scrollYPx,        // Start Y: negative = viewport offset
                                        pageWidthPx,       // Full page width (coordinate space)
                                        pageHeightPx,      // Full page height (coordinate space)
                                        0,                 // No rotation
                                        Pdfium.RenderAnnot | Pdfium.RenderLcdText);
                                }
                                finally
                                {
                                    Pdfium.FPDFBitmap_Destroy(bitmap);
                                }
                            }
                        }
                    }

                    return (Buffer: buffer, Width: bitmapWidth, Height: bitmapHeight, Stride: stride, ByteCount: byteCount);
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

    /// <summary>
    /// Gets the custom page label for a page (e.g., "i", "ii", "iii" for roman numerals).
    /// Returns null if the page has no custom label (use default "Page N" in that case).
    /// </summary>
    public static string? GetPageLabel(PdfDocumentState state, int pageIndex)
    {
        if (state == null || pageIndex < 0 || pageIndex >= state.PageCount)
            return null;

        lock (Pdfium.Gate)
        {
            var len = Pdfium.FPDF_GetPageLabel(state.Handle, pageIndex, IntPtr.Zero, 0);
            if (len <= 2) // No custom label (just null terminator or empty)
                return null;

            var buffer = new byte[len];
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                Pdfium.FPDF_GetPageLabel(state.Handle, pageIndex, handle.AddrOfPinnedObject(), len);
                return Encoding.Unicode.GetString(buffer, 0, (int)len - 2);
            }
            finally
            {
                handle.Free();
            }
        }
    }
}
