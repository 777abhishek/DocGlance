using QuickView.Core.Models;
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace QuickView.Rendering.Pdf;

/// <summary>
/// Extracts PDF document metadata (title, author, dates, etc.) using PDFium.
/// </summary>
public static class PdfMetadataService
{
    public static PdfMetadata GetMetadata(PdfDocumentState state)
    {
        if (state == null)
            return new PdfMetadata();

        Pdfium.EnsureInitialized();
        lock (Pdfium.Gate)
        {
            IntPtr doc = state.Handle;
            if (doc == IntPtr.Zero)
                return new PdfMetadata();

            return new PdfMetadata
            {
                Title = GetMetaString(doc, "Title"),
                Author = GetMetaString(doc, "Author"),
                Subject = GetMetaString(doc, "Subject"),
                Keywords = GetMetaString(doc, "Keywords"),
                Creator = GetMetaString(doc, "Creator"),
                Producer = GetMetaString(doc, "Producer"),
                CreationDate = ParsePdfDate(GetMetaString(doc, "CreationDate")),
                ModificationDate = ParsePdfDate(GetMetaString(doc, "ModDate")),
            };
        }
    }

    private static string GetMetaString(IntPtr doc, string tag)
    {
        // Two-call pattern: first get byte count
        uint len = Pdfium.FPDF_GetMetaText(doc, tag, IntPtr.Zero, 0);
        if (len <= 2) // No data or just null terminator
            return string.Empty;

        // Second call: fill buffer
        var buffer = new byte[len];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            uint actualLen = Pdfium.FPDF_GetMetaText(doc, tag, handle.AddrOfPinnedObject(), len);
            // Validate that the second call succeeded and returned data
            if (actualLen <= 2)
                return string.Empty;
            
            // FPDF_GetMetaText returns byte count INCLUDING null terminator
            // Subtract 2 bytes for the UTF-16 null terminator
            return Encoding.Unicode.GetString(buffer, 0, (int)actualLen - 2);
        }
        finally
        {
            handle.Free();
        }
    }

    private static DateTime? ParsePdfDate(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;

        // PDF date format: D:YYYYMMDDHHmmss+HH'mm'
        // Strip "D:" prefix if present
        if (s.StartsWith("D:", StringComparison.Ordinal))
            s = s.Substring(2);

        // Take first 14 characters (YYYYMMDDHHmmss)
        if (s.Length < 14)
            return null;

        string datePart = s.Substring(0, 14);

        if (DateTime.TryParseExact(
            datePart,
            "yyyyMMddHHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTime result))
        {
            return result;
        }

        return null;
    }
}
