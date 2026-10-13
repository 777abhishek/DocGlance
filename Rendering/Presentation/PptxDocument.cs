using Microsoft.UI.Xaml.Media.Imaging;
using QuickView.Rendering;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Rendering.Presentation;

public readonly record struct PptxSearchRect(double X, double Y, double Width, double Height);

public sealed record PptxSearchMatch(
    int SlideIndex,
    int CharIndex,
    int Length,
    IReadOnlyList<PptxSearchRect> Rects);

/// <summary>
/// Managed wrapper around a DeckCraft PPTX document.
/// Handles memory lifetime, thread pooling, and RGBA→BGRA conversion.
/// </summary>
public sealed class PptxDocument : IDisposable
{
    private DeckCraftNative.DcDocumentHandle _handle;
    private readonly SemaphoreSlim _renderLock = new(1, 1);
    private readonly object _nativeGate = new();
    private bool _disposed;

    private PptxDocument(DeckCraftNative.DcDocumentHandle handle)
    {
        _handle = handle;
    }

    public int SlideCount
    {
        get
        {
            lock (_nativeGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return (int)DeckCraftNative.dc_slide_count(_handle);
            }
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
        lock (_nativeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            unsafe
            {
                double w, h;
                DeckCraftNative.dc_get_slide_size(_handle, (uint)index, &w, &h);
                return (w, h);
            }
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
        await _renderLock.WaitAsync(ct);

        try
        {
            // Render on thread pool (CPU-bound work)
            var img = await Task.Run(() =>
            {
                lock (_nativeGate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    return DeckCraftNative.dc_render_slide(_handle, (uint)index, scale);
                }
            }, ct);

            if (img.Pixels == IntPtr.Zero)
                return null;

            try
            {
                return NativeBitmap.FromBgra(img.Pixels, img.Width, img.Height, img.Len);
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
        lock (_nativeGate)
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
    }

    public IReadOnlyList<PptxSearchMatch> SearchText(string query, bool caseSensitive)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<PptxSearchMatch>();

        byte[] queryBytes = Encoding.UTF8.GetBytes(query);
        byte[] resultBuffer = new byte[4096];
        nuint written;

        while (true)
        {
            lock (_nativeGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                unsafe
                {
                    fixed (byte* queryPtr = queryBytes)
                    fixed (byte* resultPtr = resultBuffer)
                    {
                        written = DeckCraftNative.dc_search_text(
                            _handle,
                            queryPtr,
                            (nuint)queryBytes.Length,
                            caseSensitive,
                            resultPtr,
                            (nuint)resultBuffer.Length);
                    }
                }
            }

            if (written <= (nuint)resultBuffer.Length)
                break;
            if (written > int.MaxValue)
                throw new InvalidOperationException("PPTX search results exceed the supported buffer size.");

            Array.Resize(ref resultBuffer, (int)written);
        }

        string serialized = Encoding.UTF8.GetString(resultBuffer, 0, (int)written);
        var matches = new List<(int SlideIndex, int CharIndex, int Length, List<PptxSearchRect> Rects)>();
        var matchIndices = new Dictionary<(int SlideIndex, int CharIndex, int Length), int>();
        foreach (string entry in serialized.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = entry.Split(',');
            if (parts.Length == 3 &&
                int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int slideIndex) &&
                int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int charIndex) &&
                int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int length))
            {
                var key = (slideIndex, charIndex, length);
                if (!matchIndices.ContainsKey(key))
                {
                    matchIndices.Add(key, matches.Count);
                    matches.Add((slideIndex, charIndex, length, new List<PptxSearchRect>()));
                }
                continue;
            }

            if (parts.Length == 7 &&
                int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out slideIndex) &&
                int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out charIndex) &&
                int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out length) &&
                double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double y) &&
                double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double width) &&
                double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out double height))
            {
                var key = (slideIndex, charIndex, length);
                if (!matchIndices.TryGetValue(key, out int matchIndex))
                {
                    matchIndex = matches.Count;
                    matchIndices.Add(key, matchIndex);
                    matches.Add((slideIndex, charIndex, length, new List<PptxSearchRect>()));
                }

                if (width > 0 && height > 0)
                    matches[matchIndex].Rects.Add(new PptxSearchRect(x, y, width, height));
            }
        }

        return matches
            .Select(match => new PptxSearchMatch(
                match.SlideIndex,
                match.CharIndex,
                match.Length,
                match.Rects))
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
                DeckCraftNative.dc_free_document(_handle);
                _handle = default;
            }
        }

        // A queued render may still need to release the semaphore after the
        // native call finishes; leave the managed semaphore for GC.
    }
}
