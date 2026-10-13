using System;
using System.Runtime.InteropServices;

namespace QuickView.Rendering.Pdf;

/// <summary>
/// P/Invoke bindings for pdfcraft_ffi.dll — native PDF rendering via PdfCraft + hayro.
/// Replaces PDFium with Rust-based renderer for 70-80% memory reduction.
/// </summary>
internal static class PdfCraftNative
{
    private const string DllName = "pdfcraft_ffi.dll";

    // ═══════════════════════════════════════════════════════════════════════════
    // Types
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// RGBA8 pixel buffer returned by pc_render_page.
    /// C# must call pc_free_image after copying pixels.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PcImage
    {
        public IntPtr pixels;   // *mut u8
        public uint width;
        public uint height;
        public nuint len;       // width * height * 4
    }

    /// <summary>
    /// PNG bytes returned by pc_export_to_png.
    /// C# must call pc_free_png_buffer after copying.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PcPngBuffer
    {
        public IntPtr data;     // *mut u8
        public nuint len;
    }

    /// <summary>
    /// PDF bytes returned by pc_merge_pdfs.
    /// C# must call pc_free_pdf_buffer after copying.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PcPdfBuffer
    {
        public IntPtr data;     // *mut u8
        public nuint len;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Document Operations
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Open PDF from file path.
    /// Returns opaque document handle. Call pc_free_document when done.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pc_open_pdf_from_path(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        nuint path_len);

    /// <summary>
    /// Open PDF from byte array.
    /// Returns opaque document handle. Call pc_free_document when done.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pc_open_pdf_from_bytes(IntPtr data, nuint len);

    /// <summary>
    /// Get number of pages in the document.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint pc_page_count(IntPtr doc);

    /// <summary>
    /// Get page dimensions in PDF points (72 points = 1 inch).
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pc_get_page_size(
        IntPtr doc,
        uint index,
        out double width_pt,
        out double height_pt);

    /// <summary>
    /// Free document handle.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pc_free_document(IntPtr doc);

    // ═══════════════════════════════════════════════════════════════════════════
    // Rendering
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Render page at specified DPI.
    /// Returns RGBA8 pixel buffer. C# MUST call pc_free_image after copying.
    /// 
    /// DPI examples:
    /// - 96.0  = standard screen resolution (1x zoom)
    /// - 144.0 = 1.5x zoom
    /// - 192.0 = 2x zoom
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PcImage pc_render_page(IntPtr doc, uint index, double dpi);

    /// <summary>
    /// Free image buffer returned by pc_render_page.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pc_free_image(PcImage img);

    // ═══════════════════════════════════════════════════════════════════════════
    // Text Operations
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Extract text from a page into UTF-8 buffer.
    /// Returns number of bytes written.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint pc_extract_page_text(
        IntPtr doc,
        uint index,
        IntPtr buf,
        nuint buf_len);

    /// <summary>
    /// Search for text across all pages.
    /// Returns semicolon-separated rectangles: "matchIndex,pageIndex,x,y,width,height;..."
    /// Returns bytes written, or required capacity if buffer too small.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint pc_search_text(
        IntPtr doc,
        IntPtr query,
        nuint query_len,
        [MarshalAs(UnmanagedType.U1)] bool case_sensitive,
        IntPtr buf,
        nuint buf_len);

    // ═══════════════════════════════════════════════════════════════════════════
    // PDF Tools
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Export page to PNG.
    /// Returns PNG bytes. C# MUST call pc_free_png_buffer after copying.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PcPngBuffer pc_export_to_png(IntPtr doc, uint page_index, double dpi);

    /// <summary>
    /// Free PNG buffer returned by pc_export_to_png.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pc_free_png_buffer(PcPngBuffer buf);

    /// <summary>
    /// Merge multiple PDFs into one.
    /// input_paths: semicolon-separated file paths
    /// Returns PDF bytes. C# MUST call pc_free_pdf_buffer.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern PcPdfBuffer pc_merge_pdfs(IntPtr input_paths, nuint input_paths_len);

    /// <summary>
    /// Free PDF buffer returned by pc_merge_pdfs.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pc_free_pdf_buffer(PcPdfBuffer buf);

    /// <summary>
    /// Split PDF into individual pages.
    /// Returns number of pages split, 0 on error.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint pc_split_pdf(
        IntPtr doc,
        IntPtr output_dir,
        nuint output_dir_len);
}
