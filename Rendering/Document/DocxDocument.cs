using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;

namespace QuickView.Rendering.Document;

/// <summary>
/// Managed wrapper around a WordCraft DOCX document.
/// Handles memory lifetime, multi-page rendering, search matching, and RGBA→BGRA conversion.
/// </summary>
public sealed class DocxDocument : IDisposable
{
    private WordCraftNative.WcDocumentHandle _handle;
    private readonly SemaphoreSlim _renderLock = new(1, 1);
    private readonly object _nativeGate = new();
    private bool _disposed;

    private DocxDocument(WordCraftNative.WcDocumentHandle handle)
    {
        _handle = handle;
    }

    public int PageCount
    {
        get
        {
            lock (_nativeGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return (int)WordCraftNative.wc_page_count(_handle);
            }
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
        lock (_nativeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            unsafe
            {
                double w, h;
                WordCraftNative.wc_get_page_size(_handle, (uint)index, &w, &h);
                return (w, h);
            }
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
        await _renderLock.WaitAsync(ct);

        try
        {
            WordCraftNative.WcImage img = await Task.Run(() =>
            {
                lock (_nativeGate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    return WordCraftNative.wc_render_page(_handle, (uint)index, scale);
                }
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
        byte[] buf = new byte[65536];

        lock (_nativeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            unsafe
            {
                fixed (byte* ptr = buf)
                {
                    var written = (int)WordCraftNative.wc_extract_page_text(_handle, (uint)index, ptr, (nuint)buf.Length);
                    return written > 0 ? Encoding.UTF8.GetString(buf, 0, written) : string.Empty;
                }
            }
        }
    }

    public record DocxMatch(int PageIndex, double X, double Y, double Width, double Height);

    public sealed record DocxSearchMatch(int MatchIndex, IReadOnlyList<DocxMatch> Rects);

    /// <summary>
    /// Search for text across all pages and return bounding boxes in page points.
    /// </summary>
    public IReadOnlyList<DocxSearchMatch> SearchText(string query, bool caseSensitive)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<DocxSearchMatch>();

        byte[] queryBytes = Encoding.UTF8.GetBytes(query);
        byte[] buffer = new byte[4096];
        nuint written;

        while (true)
        {
            unsafe
            {
                lock (_nativeGate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    fixed (byte* queryPtr = queryBytes)
                    fixed (byte* bufferPtr = buffer)
                    {
                        written = WordCraftNative.wc_search_text(
                            _handle,
                            queryPtr,
                            (nuint)queryBytes.Length,
                            caseSensitive,
                            bufferPtr,
                            (nuint)buffer.Length);
                    }
                }
            }

            if (written <= (nuint)buffer.Length)
                break;
            if (written > int.MaxValue)
                throw new InvalidOperationException("DOCX search results exceed the supported buffer size.");

            Array.Resize(ref buffer, (int)written);
        }

        if (written == 0)
            return Array.Empty<DocxSearchMatch>();

        string serialized = Encoding.UTF8.GetString(buffer, 0, (int)written);
        var matches = new List<(int MatchIndex, List<DocxMatch> Rects)>();
        var matchIndices = new Dictionary<int, int>();

        foreach (string entry in serialized.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = entry.Split(',');
            if (parts.Length != 6 ||
                !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int matchIndex) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int page) ||
                !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ||
                !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double y) ||
                !double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double width) ||
                !double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double height))
            {
                continue;
            }

            if (!matchIndices.TryGetValue(matchIndex, out int resultIndex))
            {
                resultIndex = matches.Count;
                matchIndices.Add(matchIndex, resultIndex);
                matches.Add((matchIndex, new List<DocxMatch>()));
            }

            if (width > 0 && height > 0)
                matches[resultIndex].Rects.Add(new DocxMatch(page, x, y, width, height));
        }

        return matches
            .Where(match => match.Rects.Count > 0)
            .Select(match => new DocxSearchMatch(match.MatchIndex, match.Rects))
            .ToArray();
    }

    public void Dispose()
    {
        lock (_nativeGate)
        {
            if (_disposed) return;
            _disposed = true;

            if (_handle.IsValid)
            {
                WordCraftNative.wc_free_document(_handle);
                _handle = default;
            }
        }
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

    /// <summary>
    /// Export document to PDF.
    /// Returns PDF bytes, or null on failure.
    /// </summary>
    public async Task<byte[]?> ExportToPdfAsync(CancellationToken ct = default)
    {
        await _renderLock.WaitAsync(ct);

        try
        {
            return await Task.Run(() =>
            {
                WordCraftNative.WcPdfBuffer buf;
                lock (_nativeGate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    buf = WordCraftNative.wc_export_to_pdf(_handle);
                }

                if (buf.Data == IntPtr.Zero || buf.Len == 0)
                    return null;

                try
                {
                    var result = new byte[(int)buf.Len];
                    Marshal.Copy(buf.Data, result, 0, (int)buf.Len);
                    return result;
                }
                finally
                {
                    WordCraftNative.wc_free_pdf_buffer(buf);
                }
            }, ct);
        }
        finally
        {
            _renderLock.Release();
        }
    }
}
