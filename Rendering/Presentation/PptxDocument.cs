using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Rendering.Presentation;

/// <summary>
/// Managed wrapper around a DeckCraft PPTX document.
/// Handles memory lifetime, thread pooling, and RGBA→BGRA conversion.
/// </summary>
public sealed class PptxDocument : IDisposable
{
    private DeckCraftNative.DcDocumentHandle _handle;
    private readonly SemaphoreSlim _renderLock = new(1, 1);
    private bool _disposed;

    private PptxDocument(DeckCraftNative.DcDocumentHandle handle)
    {
        _handle = handle;
    }

    public int SlideCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return (int)DeckCraftNative.dc_slide_count(_handle);
        }
    }

    /// <summary>
    /// Parse a PPTX/PPT file.
    /// Returns null if the file cannot be opened or deckcraft_ffi.dll is missing.
    /// </summary>
    public static async Task<PptxDocument?> OpenAsync(string filePath, CancellationToken ct = default)
    {
        if (!DeckCraftNative.IsAvailable)
            return null;

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(filePath, ct);
        }
        catch
        {
            return null;
        }

        var handle = await Task.Run(() =>
        {
            unsafe
            {
                fixed (byte* ptr = bytes)
                {
                    return DeckCraftNative.dc_open_pptx(ptr, (nuint)bytes.Length);
                }
            }
        }, ct);

        return handle.IsValid ? new PptxDocument(handle) : null;
    }

    /// <summary>
    /// Returns slide dimensions in PDF points (e.g., 720 × 405 for standard widescreen).
    /// </summary>
    public (double widthPt, double heightPt) GetSlideSize(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        unsafe
        {
            double w, h;
            DeckCraftNative.dc_get_slide_size(_handle, (uint)index, &w, &h);
            return (w, h);
        }
    }

    /// <summary>
    /// Render slide at `scale` pixels-per-point.
    /// scale = 1.0 → ~720×405 px
    /// scale = 2.0 → 1440×810 px (recommended)
    /// scale = 3.0 → 2160×1215 px (HiDPI)
    /// Returns null on render failure.
    /// Thread-safe: only one render per document at a time.
    /// </summary>
    public async Task<WriteableBitmap?> RenderSlideAsync(int index, double scale, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _renderLock.WaitAsync(ct);

        try
        {
            // Render on thread pool (CPU-bound work)
            var img = await Task.Run(() => DeckCraftNative.dc_render_slide(_handle, (uint)index, scale), ct);

            if (img.Pixels == IntPtr.Zero)
                return null;

            try
            {
                // Convert to WriteableBitmap - must happen on UI thread
                return CreateBitmapFromRgba(img);
            }
            finally
            {
                DeckCraftNative.dc_free_image(img);
            }
        }
        finally
        {
            _renderLock.Release();
        }
    }

    /// <summary>
    /// Get the title text of a slide (first text block, usually the title placeholder).
    /// Returns empty string if the slide has no title.
    /// </summary>
    public string GetSlideTitle(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        unsafe
        {
            Span<byte> buf = stackalloc byte[512];
            fixed (byte* ptr = buf)
            {
                var written = (int)DeckCraftNative.dc_get_slide_title(_handle, (uint)index, ptr, (nuint)buf.Length);
                if (written == 0)
                    return string.Empty;
                return Encoding.UTF8.GetString(buf.Slice(0, written));
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_handle.IsValid)
        {
            DeckCraftNative.dc_free_document(_handle);
            _handle = default;
        }

        _renderLock.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Convert DcImage (straight RGBA8) → WriteableBitmap (premul BGRA8).
    /// Must run on UI thread.
    /// </summary>
    private static unsafe WriteableBitmap CreateBitmapFromRgba(DeckCraftNative.DcImage img)
    {
        var w = (int)img.Width;
        var h = (int)img.Height;
        var bmp = new WriteableBitmap(w, h);

        using var stream = bmp.PixelBuffer.AsStream();
        var bgra = new byte[img.Len];

        // Convert RGBA → BGRA (swap R ↔ B) + straight → premultiplied
        var rgba = new Span<byte>((void*)img.Pixels, (int)img.Len);
        for (int i = 0; i < rgba.Length; i += 4)
        {
            byte r = rgba[i];
            byte g = rgba[i + 1];
            byte b = rgba[i + 2];
            byte a = rgba[i + 3];

            // Premultiply: C' = C * A / 255
            if (a != 0 && a != 255)
            {
                r = (byte)((r * a + 127) / 255);
                g = (byte)((g * a + 127) / 255);
                b = (byte)((b * a + 127) / 255);
            }

            // BGRA layout
            bgra[i]     = b;
            bgra[i + 1] = g;
            bgra[i + 2] = r;
            bgra[i + 3] = a;
        }

        stream.Write(bgra, 0, bgra.Length);
        bmp.Invalidate();
        return bmp;
    }
}
