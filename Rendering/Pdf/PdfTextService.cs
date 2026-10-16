using DocGlance.Core.Models;
using DocGlance.Interop.PdfCraft;
using DocGlance.Rendering.Pdf.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DocGlance.Rendering.Pdf;

/// <summary>Searches PDF text through the PdfCraft native API.</summary>
public static class PdfTextService
{
    public static Task<PdfPageTextMap> GetPageTextMapAsync(
        PdfDocumentState state,
        int pageIndex,
        CancellationToken cancellationToken = default)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        if ((uint)pageIndex >= (uint)state.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        if (state.ManagedPdfDocument != null)
            throw new NotSupportedException("Text selection is unavailable for password-protected PDFs.");

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (handle, lease) = state.BeginNativeCall();
            if (lease == null)
                throw new ObjectDisposedException(nameof(PdfDocumentState));

            try
            {
                uint glyphCount = PdfCraftNative.pc_get_page_char_count(handle, (uint)pageIndex);
                if (glyphCount > int.MaxValue / 4)
                    throw new InvalidOperationException("The PDF page contains too many text glyphs.");

                int floatCount = checked((int)glyphCount * 4);
                if (floatCount == 0)
                    return new PdfPageTextMap(pageIndex, Array.Empty<PdfSearchRect>());

                int byteCount = checked(floatCount * sizeof(float));
                IntPtr outputPtr = Marshal.AllocHGlobal(byteCount);
                try
                {
                    nuint written = PdfCraftNative.pc_get_page_glyph_rects(
                        handle, (uint)pageIndex, outputPtr, (nuint)floatCount);
                    if (written != (nuint)floatCount)
                        throw new InvalidOperationException(
                            $"PDF text geometry changed while loading page {pageIndex + 1}.");

                    float[] values = new float[floatCount];
                    Marshal.Copy(outputPtr, values, 0, values.Length);
                    var rects = new PdfSearchRect[(int)glyphCount];
                    for (int i = 0; i < rects.Length; i++)
                    {
                        rects[i] = new PdfSearchRect(
                            values[i * 4],
                            values[i * 4 + 1],
                            values[i * 4 + 2],
                            values[i * 4 + 3]);
                    }
                    return new PdfPageTextMap(pageIndex, rects);
                }
                finally
                {
                    Marshal.FreeHGlobal(outputPtr);
                }
            }
            finally
            {
                lease.Dispose();
            }
        }, cancellationToken);
    }

    public static Task<string> GetTextRangeAsync(
        PdfDocumentState state,
        int pageIndex,
        int start,
        int endExclusive,
        CancellationToken cancellationToken = default)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        if ((uint)pageIndex >= (uint)state.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        if (start < 0 || endExclusive < start)
            throw new ArgumentOutOfRangeException(nameof(start));
        if (state.ManagedPdfDocument != null)
            throw new NotSupportedException("Text copying is unavailable for password-protected PDFs.");
        if (start == endExclusive)
            return Task.FromResult(string.Empty);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (handle, lease) = state.BeginNativeCall();
            if (lease == null)
                throw new ObjectDisposedException(nameof(PdfDocumentState));

            try
            {
                nuint required = PdfCraftNative.pc_get_text_range(
                    handle, (uint)pageIndex, (uint)start, (uint)endExclusive,
                    IntPtr.Zero, 0);
                if (required == 0)
                    return string.Empty;
                if (required > int.MaxValue)
                    throw new InvalidOperationException("The selected PDF text is too large to copy.");

                IntPtr outputPtr = Marshal.AllocHGlobal((int)required);
                try
                {
                    nuint written = PdfCraftNative.pc_get_text_range(
                        handle, (uint)pageIndex, (uint)start, (uint)endExclusive,
                        outputPtr, required);
                    if (written != required)
                        throw new InvalidOperationException("The selected PDF text changed while copying.");

                    byte[] bytes = new byte[(int)written];
                    Marshal.Copy(outputPtr, bytes, 0, bytes.Length);
                    return Encoding.UTF8.GetString(bytes);
                }
                finally
                {
                    Marshal.FreeHGlobal(outputPtr);
                }
            }
            finally
            {
                lease.Dispose();
            }
        }, cancellationToken);
    }

    public static async Task<IReadOnlyList<PdfSearchResult>> SearchAsync(
        PdfDocumentState state,
        string query,
        bool caseSensitive = false,
        CancellationToken cancellationToken = default)
    {
        if (state == null || string.IsNullOrWhiteSpace(query))
            return Array.Empty<PdfSearchResult>();

        return await Task.Run(() =>
        {
            byte[] queryBytes = Encoding.UTF8.GetBytes(query);
            byte[] output = new byte[4096];

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Lease guarantees the document is not freed while the native search runs.
                var (handle, lease) = state.BeginNativeCall();
                if (lease == null)
                    return Array.Empty<PdfSearchResult>();

                IntPtr queryPtr = Marshal.AllocHGlobal(queryBytes.Length);
                IntPtr outputPtr = Marshal.AllocHGlobal(output.Length);
                try
                {
                    Marshal.Copy(queryBytes, 0, queryPtr, queryBytes.Length);
                    nuint written = PdfCraftNative.pc_search_text(
                        handle,
                        queryPtr,
                        (nuint)queryBytes.Length,
                        caseSensitive,
                        outputPtr,
                        (nuint)output.Length);

                    if (written <= (nuint)output.Length)
                    {
                        byte[] result = new byte[(int)written];
                        Marshal.Copy(outputPtr, result, 0, result.Length);
                        return ParseResults(Encoding.UTF8.GetString(result));
                    }

                    output = new byte[checked((int)written)];
                }
                finally
                {
                    lease.Dispose();
                    Marshal.FreeHGlobal(queryPtr);
                    Marshal.FreeHGlobal(outputPtr);
                }
            }
        }, cancellationToken);
    }

    private static IReadOnlyList<PdfSearchResult> ParseResults(string value)
    {
        var results = new List<PdfSearchResult>();
        var grouped = new Dictionary<(int PageIndex, int MatchIndex), List<PdfSearchRect>>();
        foreach (string item in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = item.Split(',');
            if (fields.Length != 6 ||
                !int.TryParse(fields[0], out int matchIndex) ||
                !int.TryParse(fields[1], out int pageIndex) ||
                !double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ||
                !double.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double y) ||
                !double.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double width) ||
                !double.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double height))
                continue;

            var key = (pageIndex, matchIndex);
            if (!grouped.TryGetValue(key, out var rects))
            {
                rects = new List<PdfSearchRect>();
                grouped.Add(key, rects);
            }
            rects.Add(new PdfSearchRect(x, y, width, height));
        }

        foreach (var pair in grouped)
            results.Add(new PdfSearchResult(pair.Key.PageIndex, pair.Key.MatchIndex, 0, pair.Value));

        results.Sort((left, right) =>
        {
            int pageComparison = left.PageIndex.CompareTo(right.PageIndex);
            return pageComparison != 0
                ? pageComparison
                : left.CharIndex.CompareTo(right.CharIndex);
        });
        return results;
    }
}
