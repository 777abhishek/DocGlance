using System;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Rendering.Pdf;

/// <summary>
/// Static helper for common PDF operation patterns:
/// - PDFium initialization and locking
/// - Page loading and cleanup with try-finally
/// - Document and text page lifecycle management
/// </summary>
internal static class PdfOperationBase
{
    /// <summary>
    /// Executes an operation on a PDF page with automatic resource management.
    /// Handles: lock, page load, operation execution, page cleanup.
    /// </summary>
    internal static async Task<TResult> ExecutePageOperationAsync<TResult>(
        PdfDocumentState state,
        int pageIndex,
        Func<IntPtr, IntPtr, TResult> operation,
        CancellationToken cancellationToken = default)
    {
        if (state == null || pageIndex < 0 || pageIndex >= state.PageCount)
            throw new ArgumentException("Invalid document state or page index");

        return await Task.Run(() =>
        {
            Pdfium.EnsureInitialized();
            lock (Pdfium.Gate)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IntPtr doc = state.Handle;
                if (doc == IntPtr.Zero)
                    throw new InvalidOperationException("Document handle is invalid");

                IntPtr page = Pdfium.FPDF_LoadPage(doc, pageIndex);
                if (page == IntPtr.Zero)
                    throw new InvalidOperationException($"Failed to load page {pageIndex}");

                try
                {
                    return operation(doc, page);
                }
                finally
                {
                    Pdfium.FPDF_ClosePage(page);
                }
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Executes an operation on a PDF page's text layer with automatic resource management.
    /// Handles: lock, page load, text page load, operation execution, cleanup.
    /// </summary>
    internal static async Task<TResult> ExecuteTextPageOperationAsync<TResult>(
        PdfDocumentState state,
        int pageIndex,
        Func<IntPtr, IntPtr, IntPtr, TResult> operation,
        CancellationToken cancellationToken = default)
    {
        if (state == null || pageIndex < 0 || pageIndex >= state.PageCount)
            throw new ArgumentException("Invalid document state or page index");

        return await Task.Run(() =>
        {
            Pdfium.EnsureInitialized();
            lock (Pdfium.Gate)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IntPtr doc = state.Handle;
                if (doc == IntPtr.Zero)
                    throw new InvalidOperationException("Document handle is invalid");

                IntPtr page = Pdfium.FPDF_LoadPage(doc, pageIndex);
                if (page == IntPtr.Zero)
                    throw new InvalidOperationException($"Failed to load page {pageIndex}");

                try
                {
                    IntPtr textPage = Pdfium.FPDFText_LoadPage(page);
                    if (textPage == IntPtr.Zero)
                        throw new InvalidOperationException($"Failed to load text page {pageIndex}");

                    try
                    {
                        return operation(doc, page, textPage);
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
        }, cancellationToken);
    }

    /// <summary>
    /// Executes an operation on the entire document with automatic locking.
    /// </summary>
    internal static async Task<TResult> ExecuteDocumentOperationAsync<TResult>(
        PdfDocumentState state,
        Func<IntPtr, TResult> operation,
        CancellationToken cancellationToken = default)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        return await Task.Run(() =>
        {
            Pdfium.EnsureInitialized();
            lock (Pdfium.Gate)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IntPtr doc = state.Handle;
                if (doc == IntPtr.Zero)
                    throw new InvalidOperationException("Document handle is invalid");

                return operation(doc);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Gets page dimensions in device pixels at specified DPI.
    /// </summary>
    internal static (int Width, int Height) GetPageDimensions(IntPtr page, float dpi)
    {
        float pageWidthPt = Pdfium.FPDF_GetPageWidthF(page);
        float pageHeightPt = Pdfium.FPDF_GetPageHeightF(page);
        
        int pageW = (int)Math.Max(1, Math.Round(pageWidthPt * dpi / 96.0));
        int pageH = (int)Math.Max(1, Math.Round(pageHeightPt * dpi / 96.0));

        return (pageW, pageH);
    }

    /// <summary>
    /// Converts PDF coordinates (points) to device pixels.
    /// </summary>
    internal static (int X, int Y) PageToDeviceCoordinates(
        IntPtr page, int pageW, int pageH, int rotation, double pageX, double pageY)
    {
        Pdfium.FPDF_PageToDevice(page, 0, 0, pageW, pageH, rotation, pageX, pageY, out int x, out int y);
        return (x, y);
    }

    /// <summary>
    /// Converts device pixels to PDF coordinates (points).
    /// </summary>
    internal static (double X, double Y) DeviceToPageCoordinates(
        IntPtr page, int pageW, int pageH, int rotation, double deviceX, double deviceY)
    {
        Pdfium.FPDF_DeviceToPage(page, 0, 0, pageW, pageH, rotation, 
            (int)deviceX, (int)deviceY, out double x, out double y);
        return (x, y);
    }
}
