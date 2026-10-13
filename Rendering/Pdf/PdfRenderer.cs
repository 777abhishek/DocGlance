using Microsoft.UI.Xaml.Media.Imaging;
using QuickView.Common.Validators;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Rendering.Pdf;

public readonly record struct PdfOpenResult(PdfDocumentState? Document, string? Error, bool NeedsPassword = false)
{
    public bool Ok => Document != null;
}

/// <summary>PDF rendering backed exclusively by the PdfCraft Rust FFI.</summary>
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
        if (!string.IsNullOrWhiteSpace(password))
            return new PdfOpenResult(null, "Password-protected PDFs are not supported by PdfCraft yet.", true);
        if (!FileSizeValidator.IsFileValidAndSafe(filePath, out var validationError))
            return new PdfOpenResult(null, $"Validation failed: {validationError}");

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
        if (!string.IsNullOrEmpty(password))
            return Task.FromResult(new PdfOpenResult(null, "Password-protected PDFs are not supported by PdfCraft yet.", true));

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
            return new PdfOpenResult(null, "PdfCraft could not parse the PDF.");

        return FinishOpen(document);
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

        var image = await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            return PdfCraftNative.pc_render_page(state.Handle, (uint)pageIndex, dpi);
        }, ct);

        if (image.Pixels == IntPtr.Zero || image.Width == 0 || image.Height == 0 ||
            ct.IsCancellationRequested)
        {
            if (image.Pixels != IntPtr.Zero)
                PdfCraftNative.pc_free_image(image);
            return null;
        }

        try
        {
            return NativeBitmap.FromRgba(image.Pixels, image.Width, image.Height, image.Len);
        }
        finally
        {
            PdfCraftNative.pc_free_image(image);
        }
    }
}
