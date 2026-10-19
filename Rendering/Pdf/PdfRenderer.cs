using Microsoft.UI.Xaml.Media.Imaging;
using DocGlance.Common.Validators;
using DocGlance.Interop.PdfCraft;
using DocGlance.Rendering.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace DocGlance.Rendering.Pdf;

public readonly record struct PdfOpenResult(PdfDocumentState? Document, string? Error, bool NeedsPassword = false)
{
    public bool Ok => Document != null;
}

/// <summary>PDF rendering via PdfCraft, with Windows.Data.Pdf for encrypted documents.</summary>
public static class PdfRenderer
{
    public const float ScreenDpi = 96f;
    public const float ThumbnailDpi = RenderBudget.ThumbnailDpi;

    public static bool IsAvailable
        => File.Exists(Path.Combine(AppContext.BaseDirectory, "pdfcraft_ffi.dll"));

    public static float GetRenderDpi(float zoomFactor)
        => ScreenDpi * Math.Clamp(zoomFactor, 0.5f, 8.0f) * RenderBudget.TextOversample;

    public static void EnsureInitialized()
    {
        if (!IsAvailable)
            throw new DllNotFoundException("pdfcraft_ffi.dll is not available.");
    }

    public static async Task<PdfOpenResult> OpenDocumentAsync(
        string filePath, CancellationToken ct = default, string? password = null)
    {
        if (!FileSizeValidator.IsFileValidAndSafe(filePath, out var validationError))
            return new PdfOpenResult(null, $"Validation failed: {validationError}");

        if (password != null)
            return await OpenPasswordProtectedDocumentAsync(filePath, password, ct);

        // Open by path so the managed heap does not hold a second copy of the file.
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            EnsureInitialized();
            byte[] pathBytes = Encoding.UTF8.GetBytes(filePath);
            IntPtr document;
            unsafe
            {
                fixed (byte* path = pathBytes)
                    document = PdfCraftNative.pc_open_pdf_from_path((IntPtr)path, (nuint)pathBytes.Length);
            }

            if (document == IntPtr.Zero)
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                return OpenDocumentFromBytesCore(bytes, ct);
            }

            return FinishOpen(document);
        }, ct);
    }

    public static Task<PdfOpenResult> OpenDocumentFromBytesAsync(
        byte[] bytes, CancellationToken ct = default, string? password = null)
    {
        if (bytes == null || bytes.Length == 0)
            return Task.FromResult(new PdfOpenResult(null, "PDF bytes are empty"));
        if (password != null)
            return Task.FromResult(new PdfOpenResult(null, "Password-protected PDFs cannot be opened from memory."));

        return Task.Run(() => OpenDocumentFromBytesCore(bytes, ct), ct);
    }

    private static PdfOpenResult OpenDocumentFromBytesCore(byte[] bytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsureInitialized();
        IntPtr document;
        unsafe
        {
            fixed (byte* data = bytes)
                document = PdfCraftNative.pc_open_pdf_from_bytes((IntPtr)data, (nuint)bytes.Length);
        }

        if (document == IntPtr.Zero)
        {
            // PdfCraft does not support encryption; give the user a precise reason instead of
            // a generic parse failure.
            return LooksEncrypted(bytes)
                ? new PdfOpenResult(null, EncryptedMessage, true)
                : new PdfOpenResult(null, "PdfCraft could not parse the PDF.");
        }

        return FinishOpen(document);
    }

    private const string EncryptedMessage =
        "This PDF is password-protected. Enter its password to open it.";

    private static async Task<PdfOpenResult> OpenPasswordProtectedDocumentAsync(
        string filePath, string password, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            var document = await PdfDocument.LoadFromFileAsync(file, password);
            ct.ThrowIfCancellationRequested();

            if (document.PageCount == 0)
                return new PdfOpenResult(null, "PDF contains no readable pages.");
            if (document.PageCount > int.MaxValue)
                return new PdfOpenResult(null, "PDF has too many pages to display.");

            var pageSizes = new List<(double Width, double Height)>((int)document.PageCount);
            for (uint i = 0; i < document.PageCount; i++)
            {
                using var page = document.GetPage(i);
                // WinRT page dimensions are in 1/96-inch units; viewer layout uses PDF points.
                pageSizes.Add((page.Size.Width * 0.75, page.Size.Height * 0.75));
            }

            return new PdfOpenResult(new PdfDocumentState(document, (int)document.PageCount, pageSizes), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Windows.Data.Pdf reports an invalid/missing password as a load failure.
            return new PdfOpenResult(null, "That password did not open the PDF. Please check it and try again.", true);
        }
    }

    /// <summary>
    /// Cheap heuristic: an encrypted PDF carries an <c>/Encrypt</c> entry in a trailer or
    /// cross-reference stream dictionary, found near the start (linearized) or end of the file.
    /// Only used after the native open has already failed.
    /// </summary>
    internal static bool LooksEncrypted(byte[] bytes)
    {
        ReadOnlySpan<byte> needle = "/Encrypt"u8;
        const int head = 4 * 1024;
        const int tail = 64 * 1024;

        var span = bytes.AsSpan();
        if (span[..Math.Min(head, span.Length)].IndexOf(needle) >= 0)
            return true;
        return span[Math.Max(0, span.Length - tail)..].IndexOf(needle) >= 0;
    }

    private static PdfOpenResult FinishOpen(IntPtr document)
    {
        uint nativeCount = PdfCraftNative.pc_page_count(document);
        if (nativeCount == 0)
        {
            PdfCraftNative.pc_free_document(document);
            return new PdfOpenResult(null, "PDF contains no readable pages.");
        }

        var sizes = new List<(double Width, double Height)>((int)nativeCount);
        for (uint i = 0; i < nativeCount; i++)
        {
            PdfCraftNative.pc_get_page_size(document, i, out double width, out double height);
            sizes.Add(width > 0 && height > 0 ? (width, height) : (612, 792));
        }

        return new PdfOpenResult(new PdfDocumentState(document, (int)nativeCount, sizes), null);
    }

    public static async Task<WriteableBitmap?> RenderPageAsync(
        PdfDocumentState state, int pageIndex, float dpi, CancellationToken ct = default)
    {
        if (state == null || ct.IsCancellationRequested ||
            pageIndex < 0 || pageIndex >= state.PageCount)
            return null;

        if (state.ManagedPdfDocument != null)
            return await RenderManagedPdfPageAsync(state, pageIndex, dpi, ct);

        var image = await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            // Lease guarantees the document is not freed while the native render runs.
            var (handle, lease) = state.BeginNativeCall();
            if (lease == null)
                return default(PdfCraftNative.PcImage);

            try
            {
                return PdfCraftNative.pc_render_page(handle, (uint)pageIndex, dpi);
            }
            finally
            {
                lease.Dispose();
            }
        }, ct);

        // No cancellation check past this point: the native render is already paid for,
        // so always copy it out and let the caller decide whether the bitmap is still wanted.
        if (image.Pixels == IntPtr.Zero || image.Width == 0 || image.Height == 0)
        {
            if (image.Pixels != IntPtr.Zero)
                PdfCraftNative.pc_free_image(image);
            return null;
        }

        try
        {
            // Awaited inside the try so the native buffer outlives the UI-thread copy.
            return await NativeBitmap.FromRgbaAsync(image.Pixels, image.Width, image.Height, image.Len);
        }
        finally
        {
            PdfCraftNative.pc_free_image(image);
        }
    }

    private static async Task<WriteableBitmap?> RenderManagedPdfPageAsync(
        PdfDocumentState state, int pageIndex, float dpi, CancellationToken ct)
    {
        var document = state.ManagedPdfDocument;
        if (document == null || ct.IsCancellationRequested)
            return null;

        using var page = document.GetPage((uint)pageIndex);
        double pageWidthPt = state.GetPageSize(pageIndex).Width;
        double pageHeightPt = state.GetPageSize(pageIndex).Height;
        float renderDpi = RenderBudget.CapDpi(dpi, pageWidthPt, pageHeightPt);
        double scale = renderDpi / 72.0;
        const uint maxDimension = 16384;
        uint width = (uint)Math.Clamp(Math.Round(pageWidthPt * scale), 1, maxDimension);
        uint height = (uint)Math.Clamp(Math.Round(pageHeightPt * scale), 1, maxDimension);
        var options = new PdfPageRenderOptions
        {
            DestinationWidth = width,
            DestinationHeight = height
        };

        using var stream = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(stream, options);
        ct.ThrowIfCancellationRequested();
        stream.Seek(0);
        return await NativeBitmap.FromPdfStreamAsync(stream, (int)width, (int)height);
    }
}
