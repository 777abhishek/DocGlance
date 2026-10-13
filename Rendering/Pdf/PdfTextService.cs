using QuickView.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Rendering.Pdf;

/// <summary>Searches PDF text through the PdfCraft native API.</summary>
public static class PdfTextService
{
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
