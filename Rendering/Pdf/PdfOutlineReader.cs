using QuickView.Core.Models;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace QuickView.Rendering.Pdf;

/// <summary>
/// Reads the PDF outline from the already-open PDFium document.
/// </summary>
public static class PdfOutlineReader
{
    public static List<OutlineItem> Read(PdfDocumentState state)
    {
        var result = new List<OutlineItem>();
        if (state == null) return result;

        Pdfium.EnsureInitialized();
        lock (Pdfium.Gate)
        {
            IntPtr doc = state.Handle;
            if (doc == IntPtr.Zero) return result;
            Walk(doc, Pdfium.FPDFBookmark_GetFirstChild(doc, IntPtr.Zero), result, 0);
        }
        return result;
    }

    static void Walk(IntPtr doc, IntPtr bookmark, List<OutlineItem> sink, int level)
    {
        while (bookmark != IntPtr.Zero)
        {
            var item = new OutlineItem
            {
                Title = ReadTitle(bookmark),
                Page  = ReadPage(doc, bookmark),
                Level = level,
            };
            Walk(doc, Pdfium.FPDFBookmark_GetFirstChild(doc, bookmark), item.Children, level + 1);
            sink.Add(item);
            bookmark = Pdfium.FPDFBookmark_GetNextSibling(doc, bookmark);
        }
    }

    static string ReadTitle(IntPtr bookmark)
    {
        uint bytes = Pdfium.FPDFBookmark_GetTitle(bookmark, IntPtr.Zero, 0);
        if (bytes <= 2) return string.Empty;

        IntPtr buf = Marshal.AllocHGlobal((int)bytes);
        try
        {
            Pdfium.FPDFBookmark_GetTitle(bookmark, buf, bytes);
            return Marshal.PtrToStringUni(buf)?.TrimEnd('\0') ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    static int ReadPage(IntPtr doc, IntPtr bookmark)
    {
        IntPtr dest = Pdfium.FPDFBookmark_GetDest(doc, bookmark);
        if (dest == IntPtr.Zero)
        {
            IntPtr action = Pdfium.FPDFBookmark_GetAction(bookmark);
            if (action != IntPtr.Zero)
                dest = Pdfium.FPDFAction_GetDest(doc, action);
        }
        if (dest == IntPtr.Zero) return 0;

        int index = Pdfium.FPDFDest_GetDestPageIndex(doc, dest);
        return index >= 0 ? index + 1 : 0;
    }
}
