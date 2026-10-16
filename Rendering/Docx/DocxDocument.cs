using Microsoft.UI.Xaml.Media.Imaging;
using DocGlance.Interop.WordCraft;
using DocGlance.Rendering.Shared;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Xml.Linq;
using DocGlance.Core.Models;

namespace DocGlance.Rendering.Docx;

/// <summary>
/// Managed wrapper around a WordCraft DOCX document.
/// Handles memory lifetime, multi-page rendering, search matching, and RGBA→BGRA conversion.
/// </summary>
public sealed class DocxDocument : IDisposable
{
    private static readonly XNamespace WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private WordCraftNative.WcDocumentHandle _handle;
    private readonly SemaphoreSlim _renderLock = new(1, 1);
    private readonly object _nativeGate = new();
    private bool _disposed;
    private int _disposeStarted;

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

    public static IReadOnlyList<OutlineItem> ReadOutline(string filePath)
    {
        using var archive = ZipFile.OpenRead(filePath);
        var documentEntry = archive.GetEntry("word/document.xml");
        if (documentEntry == null)
            return Array.Empty<OutlineItem>();

        var styles = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        var stylesEntry = archive.GetEntry("word/styles.xml");
        if (stylesEntry != null)
        {
            using var stylesStream = stylesEntry.Open();
            var styleDocument = XDocument.Load(stylesStream);
            foreach (var style in styleDocument.Descendants(WordNamespace + "style"))
            {
                string? id = (string?)style.Attribute(WordNamespace + "styleId");
                if (!string.IsNullOrEmpty(id))
                    styles[id] = style;
            }
        }

        int? GetStyleLevel(string? styleId, HashSet<string>? visited = null)
        {
            if (string.IsNullOrEmpty(styleId))
                return null;

            string normalized = styleId.Replace(" ", string.Empty, StringComparison.Ordinal);
            int? conventionalLevel =
                normalized.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(normalized.AsSpan("Heading".Length), out int heading) &&
                heading is >= 1 and <= 9
                    ? heading - 1
                    : null;
            if (!styles.TryGetValue(styleId, out var style))
                return conventionalLevel;
            visited ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!visited.Add(styleId))
                return null;

            var outlineLevel = style.Element(WordNamespace + "pPr")?
                .Element(WordNamespace + "outlineLvl")?
                .Attribute(WordNamespace + "val");
            if (outlineLevel != null &&
                int.TryParse(outlineLevel.Value, out int level) &&
                level is >= 0 and <= 8)
                return level;

            string? basedOn = (string?)style.Element(WordNamespace + "basedOn")?
                .Attribute(WordNamespace + "val");
            int? inherited = GetStyleLevel(basedOn, visited);
            if (inherited.HasValue)
                return inherited;
            return conventionalLevel;
        }

        var roots = new List<OutlineItem>();
        var ancestors = new List<OutlineItem>();
        using var documentStream = documentEntry.Open();
        var doc = XDocument.Load(documentStream);
        foreach (var paragraph in doc.Descendants(WordNamespace + "p"))
        {
            var paragraphProperties = paragraph.Element(WordNamespace + "pPr");
            var directLevel = paragraphProperties?
                .Element(WordNamespace + "outlineLvl")?
                .Attribute(WordNamespace + "val");
            int? level = directLevel != null &&
                int.TryParse(directLevel.Value, out int direct) &&
                direct is >= 0 and <= 8
                    ? direct
                    : null;

            string? styleId = (string?)paragraphProperties?
                .Element(WordNamespace + "pStyle")?
                .Attribute(WordNamespace + "val");
            level ??= GetStyleLevel(styleId);
            if (level == null)
                continue;

            var text = new StringBuilder();
            foreach (var element in paragraph.Descendants())
            {
                if (element.Name == WordNamespace + "t")
                    text.Append(element.Value);
                else if (element.Name == WordNamespace + "tab")
                    text.Append('\t');
                else if (element.Name == WordNamespace + "br" || element.Name == WordNamespace + "cr")
                    text.AppendLine();
            }
            string title = string.Join(" ", text.ToString()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (string.IsNullOrWhiteSpace(title))
                continue;

            var item = new OutlineItem { Title = title, Level = level.Value };
            while (ancestors.Count > item.Level)
                ancestors.RemoveAt(ancestors.Count - 1);
            if (ancestors.Count == 0)
                roots.Add(item);
            else
                ancestors[^1].Children.Add(item);
            ancestors.Add(item);
        }
        return roots;
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
                return await NativeBitmap.FromBgraAsync(img.Pixels, img.Width, img.Height, img.Len);
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

        // The native call returns the required size when the buffer is too small, so grow and
        // retry instead of silently truncating long pages.
        while (true)
        {
            nuint written;
            lock (_nativeGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                unsafe
                {
                    fixed (byte* ptr = buf)
                    {
                        written = WordCraftNative.wc_extract_page_text(_handle, (uint)index, ptr, (nuint)buf.Length);
                    }
                }
            }

            if (written <= (nuint)buf.Length)
                return written > 0 ? Encoding.UTF8.GetString(buf, 0, (int)written) : string.Empty;
            if (written > 64 * 1024 * 1024)
                return Encoding.UTF8.GetString(buf); // refuse absurd sizes; keep what we have

            buf = new byte[(int)written];
        }
    }

    public readonly record struct DocxTextPosition(int PageIndex, int Offset);

    public int GetPageTextOffsetCount(int pageIndex)
    {
        lock (_nativeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ValidatePageIndex(pageIndex);
            return checked((int)WordCraftNative.wc_get_page_char_count(_handle, (uint)pageIndex));
        }
    }

    public DocxTextPosition? HitTest(int pageIndex, double xPt, double yPt)
    {
        lock (_nativeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ValidatePageIndex(pageIndex);
            unsafe
            {
                WordCraftNative.WcTextPosition position = default;
                if (!WordCraftNative.wc_hit_test(
                        _handle, (uint)pageIndex, xPt, yPt, &position))
                    return null;
                return new DocxTextPosition((int)position.Page, checked((int)position.Offset));
            }
        }
    }

    public IReadOnlyList<DocxMatch> GetSelectionRects(int pageIndex, int startOffset, int endOffset)
    {
        lock (_nativeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ValidatePageIndex(pageIndex);
            int offsetCount = checked((int)WordCraftNative.wc_get_page_char_count(_handle, (uint)pageIndex));
            ValidateTextRange(startOffset, endOffset, offsetCount);
            if (startOffset == endOffset)
                return Array.Empty<DocxMatch>();

            unsafe
            {
                nuint required = WordCraftNative.wc_get_selection_rects(
                    _handle, (uint)pageIndex, (uint)startOffset, (uint)endOffset, null, 0);
                if (required == 0)
                    return Array.Empty<DocxMatch>();
                if (required > 4_000_000 || required > int.MaxValue || required % 4 != 0)
                    throw new InvalidOperationException("DOCX selection geometry exceeds the supported size.");

                var buffer = new float[(int)required];
                fixed (float* ptr = buffer)
                {
                    nuint written = WordCraftNative.wc_get_selection_rects(
                        _handle, (uint)pageIndex, (uint)startOffset, (uint)endOffset,
                        ptr, (nuint)buffer.Length);
                    if (written != required)
                        throw new InvalidOperationException("DOCX selection geometry changed while being read.");
                }

                var rects = new List<DocxMatch>(buffer.Length / 4);
                for (int i = 0; i < buffer.Length; i += 4)
                {
                    if (buffer[i + 2] > 0 && buffer[i + 3] > 0)
                        rects.Add(new DocxMatch(pageIndex, buffer[i], buffer[i + 1], buffer[i + 2], buffer[i + 3]));
                }
                return rects;
            }
        }
    }

    public string GetTextRange(int pageIndex, int startOffset, int endOffset)
    {
        lock (_nativeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ValidatePageIndex(pageIndex);
            int offsetCount = checked((int)WordCraftNative.wc_get_page_char_count(_handle, (uint)pageIndex));
            ValidateTextRange(startOffset, endOffset, offsetCount);
            if (startOffset == endOffset)
                return string.Empty;

            unsafe
            {
                nuint required = WordCraftNative.wc_get_text_range(
                    _handle, (uint)pageIndex, (uint)startOffset, (uint)endOffset, null, 0);
                if (required == 0)
                    return string.Empty;
                if (required > 64 * 1024 * 1024 || required > int.MaxValue)
                    throw new InvalidOperationException("DOCX selected text exceeds the supported size.");

                var buffer = new byte[(int)required];
                fixed (byte* ptr = buffer)
                {
                    nuint written = WordCraftNative.wc_get_text_range(
                        _handle, (uint)pageIndex, (uint)startOffset, (uint)endOffset,
                        ptr, (nuint)buffer.Length);
                    if (written != required)
                        throw new InvalidOperationException("DOCX selected text changed while being read.");
                }
                return Encoding.UTF8.GetString(buffer);
            }
        }
    }

    private void ValidatePageIndex(int pageIndex)
    {
        if ((uint)pageIndex >= WordCraftNative.wc_page_count(_handle))
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
    }

    private static void ValidateTextRange(int startOffset, int endOffset, int offsetCount)
    {
        if (startOffset < 0 || endOffset < startOffset || endOffset > offsetCount)
            throw new ArgumentOutOfRangeException(nameof(startOffset), "The text range is outside the page.");
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
        if (Interlocked.Exchange(ref _disposeStarted, 1) == 1) return;

        // New callers fail fast; calls already inside the native gate finish first.
        _disposed = true;

        // Free on the thread pool so the caller (usually the UI thread) never blocks
        // waiting for an in-flight native render.
        _ = Task.Run(() =>
        {
            lock (_nativeGate)
            {
                if (_handle.IsValid)
                {
                    WordCraftNative.wc_free_document(_handle);
                    _handle = default;
                }
            }
        });
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
