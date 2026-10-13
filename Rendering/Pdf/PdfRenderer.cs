using Microsoft.UI.Xaml.Media.Imaging;
using QuickView.Common.Validators;
using System;
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

/// <summary>PDF rendering backed exclusively by the PdfCraft Rust FFI.</summary>
public static class PdfRenderer
{
    public const float ScreenDpi = 96f;
    public const float ThumbnailDpi = 96f;

    public static bool IsAvailable
        => File.Exists(Path.Combine(AppContext.BaseDirectory, "pdfcraft_ffi.dll"));

    public static float GetRenderDpi(float zoomFactor)
        => ScreenDpi * Math.Clamp(zoomFactor, 0.5f, 6.0f);

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

        byte[] bytes = await File.ReadAllBytesAsync(filePath, ct);
        return await OpenDocumentFromBytesAsync(bytes, ct);
    }

    public static Task<PdfOpenResult> OpenDocumentFromBytesAsync(
        byte[] bytes, CancellationToken ct = default, string? password = null)
    {
        if (bytes == null || bytes.Length == 0)
            return Task.FromResult(new PdfOpenResult(null, "PDF bytes are empty"));
        if (!string.IsNullOrEmpty(password))
            return Task.FromResult(new PdfOpenResult(null, "Password-protected PDFs are not supported by PdfCraft yet.", true));

        return Task.Run(() =>
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
        }, ct);
    }

    public static async Task<WriteableBitmap?> RenderPageAsync(
        PdfDocumentState state, int pageIndex, float dpi, CancellationToken ct = default)
    {
        if (state == null || ct.IsCancellationRequested ||
            pageIndex < 0 || pageIndex >= state.PageCount)
            return null;

        var result = await Task.Run<(PdfCraftNative.PcImage Image, byte[]? Pixels)>(() =>
        {
            ct.ThrowIfCancellationRequested();
            var image = PdfCraftNative.pc_render_page(state.Handle, (uint)pageIndex, dpi);
            if (image.Pixels == IntPtr.Zero || image.Width == 0 || image.Height == 0)
                return (Image: default(PdfCraftNative.PcImage), Pixels: (byte[]?)null);

            try
            {
                if (image.Len > int.MaxValue)
                    throw new InvalidOperationException("PdfCraft returned an oversized page bitmap.");
                byte[] rgba = new byte[(int)image.Len];
                Marshal.Copy(image.Pixels, rgba, 0, rgba.Length);
                return (Image: image, Pixels: rgba);
            }
            finally
            {
                PdfCraftNative.pc_free_image(image);
            }
        }, ct);

        if (result.Pixels == null || ct.IsCancellationRequested) return null;
        var bitmap = new WriteableBitmap((int)result.Image.Width, (int)result.Image.Height);
        using var stream = bitmap.PixelBuffer.AsStream();
        byte[] bgra = result.Pixels;
        for (int i = 0; i + 3 < bgra.Length; i += 4)
            (bgra[i], bgra[i + 2]) = (bgra[i + 2], bgra[i]);
        stream.Write(bgra, 0, bgra.Length);
        bitmap.Invalidate();
        return bitmap;
    }
}
