using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Rendering.Document;

/// <summary>
/// Managed wrapper around a WordCraft DOCX document.
/// Handles memory lifetime, multi-page rendering, search matching, and RGBA→BGRA conversion.
/// </summary>
public sealed class DocxDocument : IDisposable
{
    private WordCraftNative.WcDocumentHandle _handle;
    private readonly SemaphoreSlim _renderLock = new(1, 1);
    private bool _disposed;

    private DocxDocument(WordCraftNative.WcDocumentHandle handle)
    {
        _handle = handle;
    }

    public int PageCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return (int)WordCraftNative.wc_page_count(_handle);
        }
    }

    /// <summary>
    /// Parse a DOCX file and compute layout.
    /// Returns null if the file cannot be opened or wordcraft_ffi.dll is missing.
    /// </summary>
    public static async Task<DocxDocument?> OpenAsync(string filePath, CancellationToken ct = default)
    {
        if (!WordCraftNative.IsAvailable)
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
                    return WordCraftNative.wc_open_docx(ptr, (nuint)bytes.Length);
                }
            }
        }, ct);

        return handle.IsValid ? new DocxDocument(handle) : null;
    }

    /// <summary>
    /// Returns page dimensions in points (e.g. 612 × 792 for standard US Letter).
    /// </summary>
    public (double widthPt, double heightPt) GetPageSize(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        unsafe
        {
            double w, h;
            WordCraftNative.wc_get_page_size(_handle, (uint)index, &w, &h);
            return (w, h);
        }
    }

    /// <summary>
    /// Render page at `scale` pixels-per-point.
    /// scale = 1.0 → standard 72 dpi
    /// scale = 2.0 → crisp on high-DPI displays
    /// Returns null on render failure.
    /// </summary>
    public async Task<WriteableBitmap?> RenderPageAsync(int index, double scale, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _renderLock.WaitAsync(ct);

        try
        {
            WordCraftNative.WcImage img = await Task.Run(() =>
            {
                return WordCraftNative.wc_render_page(_handle, (uint)index, scale);
            }, ct);

            if (img.Pixels == IntPtr.Zero || img.Len == 0)
                return null;

            try
            {
                return CreateBitmapFromRgba(img);
            }
            finally
            {
                WordCraftNative.wc_free_image(img);
            }
        }
        finally
        {
            _renderLock.Release();
        }
    }

    /// <summary>
    /// Extract text on page `index` into a managed string.
    /// </summary>
    public string ExtractPageText(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        byte[] buf = new byte[65536];

        unsafe
        {
            fixed (byte* ptr = buf)
            {
                var written = (int)WordCraftNative.wc_extract_page_text(_handle, (uint)index, ptr, (nuint)buf.Length);
                return written > 0 ? Encoding.UTF8.GetString(buf, 0, written) : string.Empty;
            }
        }
    }

    public record DocxMatch(int PageIndex, double X, double Y, double Width, double Height);

    /// <summary>
    /// Search for text across all pages and return bounding boxes in page points.
    /// </summary>
    public System.Collections.Generic.List<DocxMatch> SearchText(string query, bool caseSensitive)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(query)) return new();

        var queryBytes = Encoding.UTF8.GetBytes(query);
        byte[] buf = new byte[65536];

        unsafe
        {
            fixed (byte* qPtr = queryBytes)
            fixed (byte* bPtr = buf)
            {
                var written = (int)WordCraftNative.wc_search_text(
                    _handle,
                    qPtr,
                    (nuint)queryBytes.Length,
                    caseSensitive,
                    bPtr,
                    (nuint)buf.Length);

                if (written == 0) return new();

                var str = Encoding.UTF8.GetString(buf, 0, written);
                var matches = new System.Collections.Generic.List<DocxMatch>();
                var parts = str.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var tokens = part.Split(',');
                    if (tokens.Length >= 5 &&
                        int.TryParse(tokens[0], out int page) &&
                        double.TryParse(tokens[1], System.Globalization.CultureInfo.InvariantCulture, out double x) &&
                        double.TryParse(tokens[2], System.Globalization.CultureInfo.InvariantCulture, out double y) &&
                        double.TryParse(tokens[3], System.Globalization.CultureInfo.InvariantCulture, out double w) &&
                        double.TryParse(tokens[4], System.Globalization.CultureInfo.InvariantCulture, out double h))
                    {
                        matches.Add(new DocxMatch(page, x, y, w, h));
                    }
                    else if (tokens.Length >= 3 &&
                        int.TryParse(tokens[0], out int p))
                    {
                        matches.Add(new DocxMatch(p, 0, 0, 0, 0));
                    }
                }
                return matches;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_handle.IsValid)
        {
            WordCraftNative.wc_free_document(_handle);
            _handle = default;
        }

        _renderLock.Dispose();
    }

    /// <summary>
    /// Convert WcImage (straight RGBA8) → WriteableBitmap (premul BGRA8).
    /// </summary>
    private static unsafe WriteableBitmap CreateBitmapFromRgba(WordCraftNative.WcImage img)
    {
        var w = (int)img.Width;
        var h = (int)img.Height;
        var bmp = new WriteableBitmap(w, h);

        using var stream = bmp.PixelBuffer.AsStream();
        var bgra = new byte[img.Len];

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
