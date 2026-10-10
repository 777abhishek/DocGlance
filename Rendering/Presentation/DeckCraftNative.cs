using System;
using System.IO;
using System.Runtime.InteropServices;

namespace QuickView.Rendering.Presentation;

/// <summary>
/// P/Invoke bindings for deckcraft_ffi.dll — DeckCraft PPTX parser and renderer.
/// Memory contract:
///   - Rust allocates pixel buffers (dc_render_slide); C# MUST call dc_free_image.
///   - Rust allocates documents (dc_open_pptx); C# MUST call dc_free_document.
/// </summary>
internal static partial class DeckCraftNative
{
    private const string Dll = "deckcraft_ffi";

    /// <summary>
    /// Checks whether deckcraft_ffi.dll is available.
    /// If false, PPT/PPTX files fall back to an info card.
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
    /// Opaque handle to a parsed PPTX document.
    /// Obtained via dc_open_pptx, freed via dc_free_document.
    /// </summary>
    internal readonly struct DcDocumentHandle
    {
        public readonly IntPtr Ptr;
        public DcDocumentHandle(IntPtr ptr) => Ptr = ptr;
        public bool IsValid => Ptr != IntPtr.Zero;
    }

    /// <summary>
    /// Pixel buffer returned by dc_render_slide.
    /// C# MUST call dc_free_image after copying the pixels.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DcImage
    {
        public IntPtr Pixels;   // *mut u8 (heap-allocated Vec)
        public uint Width;
        public uint Height;
        public nuint Len;       // == Width * Height * 4
    }

    // ── Imports ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse a PPTX/PPT file from bytes.
    /// Returns null handle on failure.
    /// </summary>
    [LibraryImport(Dll)]
    internal static unsafe partial DcDocumentHandle dc_open_pptx(
        byte* data,
        nuint len);

    /// <summary>Returns the number of slides in the presentation.</summary>
    [LibraryImport(Dll)]
    internal static partial uint dc_slide_count(DcDocumentHandle doc);

    /// <summary>
    /// Writes slide dimensions (in PDF points) into out-parameters.
    /// Standard widescreen = 720 pt × 405 pt.
    /// </summary>
    [LibraryImport(Dll)]
    internal static unsafe partial void dc_get_slide_size(
        DcDocumentHandle doc,
        uint index,
        double* widthPt,
        double* heightPt);

    /// <summary>
    /// Render slide `index` at `scale` pixels-per-point.
    /// scale = 1.0 → ~720×405 px
    /// scale = 2.0 → 1440×810 px (recommended)
    /// scale = 3.0 → 2160×1215 px (HiDPI)
    /// Returns straight RGBA8 pixels.
    /// C# MUST call dc_free_image.
    /// </summary>
    [LibraryImport(Dll)]
    internal static partial DcImage dc_render_slide(
        DcDocumentHandle doc,
        uint index,
        double scale);

    /// <summary>
    /// Copy slide title text into `buf` (UTF-8, not null-terminated).
    /// Returns bytes written (0 if no title or buffer too small).
    /// </summary>
    [LibraryImport(Dll)]
    internal static unsafe partial nuint dc_get_slide_title(
        DcDocumentHandle doc,
        uint index,
        byte* buf,
        nuint bufLen);

    /// <summary>
    /// Extract all text from slide `index` into `buf` (UTF-8, not null-terminated).
    /// Returns bytes written (0 if no text or buffer too small).
    /// Includes all text shapes, tables, and notes.
    /// </summary>
    [LibraryImport(Dll)]
    internal static unsafe partial nuint dc_extract_slide_text(
        DcDocumentHandle doc,
        uint index,
        byte* buf,
        nuint bufLen);

    /// <summary>
    /// Search for text across all slides.
    /// Returns semicolon-separated matches: "slideIndex,charIndex,length;..."
    /// Results written to `buf` (UTF-8). Returns bytes written.
    /// </summary>
    [LibraryImport(Dll)]
    internal static unsafe partial nuint dc_search_text(
        DcDocumentHandle doc,
        byte* query,
        nuint queryLen,
        [MarshalAs(UnmanagedType.U1)] bool caseSensitive,
        byte* buf,
        nuint bufLen);

    /// <summary>
    /// Free pixel buffer returned by dc_render_slide.
    /// MUST be called after copying pixels into a WriteableBitmap.
    /// </summary>
    [LibraryImport(Dll)]
    internal static partial void dc_free_image(DcImage img);

    /// <summary>
    /// Free document handle returned by dc_open_pptx.
    /// </summary>
    [LibraryImport(Dll)]
    internal static partial void dc_free_document(DcDocumentHandle doc);
}
