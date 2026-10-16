using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DocGlance.Interop.PdfCraft;

internal static partial class PdfCraftNative
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

    [StructLayout(LayoutKind.Sequential)]
    internal struct PcPngBuffer
    {
        internal IntPtr Data;
        internal nuint Len;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PcPdfBuffer
    {
        internal IntPtr Data;
        internal nuint Len;
    }

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr pc_open_pdf_from_path(IntPtr path, nuint pathLen);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr pc_open_pdf_from_bytes(IntPtr data, nuint len);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial uint pc_page_count(IntPtr doc);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void pc_get_page_size(
        IntPtr doc, uint index, out double widthPt, out double heightPt);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial PcImage pc_render_page(IntPtr doc, uint index, double dpi);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void pc_free_image(PcImage image);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial PcPngBuffer pc_export_to_png(IntPtr doc, uint pageIndex, double dpi);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void pc_free_png_buffer(PcPngBuffer buffer);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial PcPdfBuffer pc_merge_pdfs(IntPtr inputPaths, nuint inputPathsLen);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void pc_free_pdf_buffer(PcPdfBuffer buffer);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial uint pc_split_pdf(IntPtr doc, IntPtr outputDir, nuint outputDirLen);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial uint pc_split_pdf_selected(
        IntPtr doc, IntPtr pageIndices, nuint pageIndicesLen,
        IntPtr outputDir, nuint outputDirLen);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint pc_extract_page_text(
        IntPtr doc, uint index, IntPtr buffer, nuint bufferLen);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial uint pc_get_page_char_count(IntPtr doc, uint pageIndex);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint pc_get_page_glyph_rects(
        IntPtr doc, uint pageIndex, IntPtr buffer, nuint bufferCapacity);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint pc_get_text_range(
        IntPtr doc, uint pageIndex, uint startOffset, uint endOffset,
        IntPtr buffer, nuint bufferLen);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial PcPdfBuffer pc_add_text_annotation(
        IntPtr doc, uint pageIndex, uint kind, IntPtr rects, nuint rectsLen,
        IntPtr contents, nuint contentsLen, IntPtr color, float opacity);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial PcPdfBuffer pc_add_ink_annotation(
        IntPtr doc, uint pageIndex, IntPtr points, nuint pointsLen,
        IntPtr color, float opacity, float width);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial PcPdfBuffer pc_add_image_annotation(
        IntPtr doc, uint pageIndex, IntPtr rect, nuint rectLen,
        IntPtr jpeg, nuint jpegLen, uint pixelWidth, uint pixelHeight);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial PcPdfBuffer pc_get_document_outline(IntPtr doc);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial PcPdfBuffer pc_get_text_annotations(IntPtr doc);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial uint pc_count_removable_annotations(
        IntPtr doc, uint pageIndex, IntPtr rects, nuint rectsLen);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial PcPdfBuffer pc_delete_text_annotations(
        IntPtr doc, uint pageIndex, IntPtr rects, nuint rectsLen);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint pc_search_text(
        IntPtr doc, IntPtr query, nuint queryLen, [MarshalAs(UnmanagedType.U1)] bool caseSensitive,
        IntPtr buffer, nuint bufferLen);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void pc_free_document(IntPtr doc);
}
