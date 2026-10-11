using System;
using System.IO;
using System.Runtime.InteropServices;

namespace QuickView.Rendering.Document;

/// <summary>
/// P/Invoke bindings for wordcraft_ffi.dll — WordCraft DOCX parser and renderer.
/// Memory contract:
///   - Rust allocates pixel buffers (wc_render_page); C# MUST call wc_free_image.
///   - Rust allocates documents (wc_open_docx); C# MUST call wc_free_document.
/// </summary>
internal static partial class WordCraftNative
{
    private const string Dll = "wordcraft_ffi";

    /// <summary>
    /// Checks whether wordcraft_ffi.dll is available.
    /// If false, DOCX files fall back to an info card.
    /// </summary>
    public static bool IsAvailable
    {
        get
        {
            var dllPath = Path.Combine(AppContext.BaseDirectory, $"{Dll}.dll");
            return File.Exists(dllPath);
        }
    }

    // ── Opaque handle types ───────────────────────────────────────────────────

    /// <summary>
    /// Opaque handle to a parsed and laid-out DOCX document.
    /// Obtained via wc_open_docx, freed via wc_free_document.
    /// </summary>
    internal readonly struct WcDocumentHandle
    {
        public readonly IntPtr Ptr;
        public WcDocumentHandle(IntPtr ptr) => Ptr = ptr;
        public bool IsValid => Ptr != IntPtr.Zero;
    }

    /// <summary>
    /// Pixel buffer returned by wc_render_page.
    /// C# MUST call wc_free_image after copying the pixels.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WcImage
    {
        public IntPtr Pixels;   // *mut u8 (heap-allocated Vec)
        public uint Width;
        public uint Height;
        public nuint Len;       // == Width * Height * 4
    }

    // ── Imports ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse a DOCX file from bytes and lay it out.
    /// Returns null handle on failure.
    /// </summary>
    [LibraryImport(Dll)]
    internal static unsafe partial WcDocumentHandle wc_open_docx(
        byte* data,
        nuint len);

    /// <summary>Returns the number of pages in the laid out document.</summary>
    [LibraryImport(Dll)]
    internal static partial uint wc_page_count(WcDocumentHandle doc);

    /// <summary>
    /// Writes page dimensions (in PDF points) into out-parameters.
    /// Standard Letter = 612 pt × 792 pt.
    /// </summary>
    [LibraryImport(Dll)]
    internal static unsafe partial void wc_get_page_size(
        WcDocumentHandle doc,
        uint index,
        double* widthPt,
        double* heightPt);

    /// <summary>
    /// Render page `index` at `scale` pixels-per-point.
    /// scale = 1.0 → ~612×792 px
    /// scale = 2.0 → ~1224×1584 px (recommended)
    /// Returns straight RGBA8 pixels.
    /// C# MUST call wc_free_image.
    /// </summary>
    [LibraryImport(Dll)]
    internal static partial WcImage wc_render_page(
        WcDocumentHandle doc,
        uint index,
        double scale);

    /// <summary>
    /// Extract all text on page `index` into `buf` (UTF-8, not null-terminated).
    /// Returns bytes written (0 if no text or buffer too small).
    /// </summary>
    [LibraryImport(Dll)]
    internal static unsafe partial nuint wc_extract_page_text(
        WcDocumentHandle doc,
        uint index,
        byte* buf,
        nuint bufLen);

    /// <summary>
    /// Search for text across all pages.
    /// Returns semicolon-separated matches: "pageIndex,minX,minY,width,height;..."
    /// Results written to `buf` (UTF-8). Returns bytes written.
    /// </summary>
    [LibraryImport(Dll)]
    internal static unsafe partial nuint wc_search_text(
        WcDocumentHandle doc,
        byte* query,
        nuint queryLen,
        [MarshalAs(UnmanagedType.U1)] bool caseSensitive,
        byte* buf,
        nuint bufLen);

    /// <summary>
    /// Free pixel buffer returned by wc_render_page.
    /// MUST be called after copying pixels into a WriteableBitmap.
    /// </summary>
    [LibraryImport(Dll)]
    internal static partial void wc_free_image(WcImage img);

    /// <summary>
    /// Free document handle returned by wc_open_docx.
    /// </summary>
    [LibraryImport(Dll)]
    internal static partial void wc_free_document(WcDocumentHandle doc);
}
