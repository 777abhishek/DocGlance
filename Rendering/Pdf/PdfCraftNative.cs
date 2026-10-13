using System;
using System.Runtime.InteropServices;

namespace QuickView.Rendering.Pdf;

internal static class PdfCraftNative
{
    private const string DllName = "pdfcraft_ffi";

    [StructLayout(LayoutKind.Sequential)]
    internal struct PcImage
    {
        internal IntPtr Pixels;
        internal uint Width;
        internal uint Height;
        internal nuint Len;
    }

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr pc_open_pdf_from_path(IntPtr path, nuint pathLen);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr pc_open_pdf_from_bytes(IntPtr data, nuint len);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint pc_page_count(IntPtr doc);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void pc_get_page_size(
        IntPtr doc, uint index, out double widthPt, out double heightPt);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern PcImage pc_render_page(IntPtr doc, uint index, double dpi);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void pc_free_image(PcImage image);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint pc_extract_page_text(
        IntPtr doc, uint index, IntPtr buffer, nuint bufferLen);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint pc_search_text(
        IntPtr doc, IntPtr query, nuint queryLen, [MarshalAs(UnmanagedType.U1)] bool caseSensitive,
        IntPtr buffer, nuint bufferLen);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void pc_free_document(IntPtr doc);
}
