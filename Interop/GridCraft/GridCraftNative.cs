using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DocGlance.Interop.GridCraft;

/// <summary>
/// P/Invoke bindings for gridcraft_ffi.dll — GridCraft XLSX parser and engine.
/// Memory contract:
///   - Rust allocates PDF buffers (gc_export_sheet_pdf); C# MUST call gc_free_pdf_buffer.
///   - Rust allocates documents (gc_open_xlsx); C# MUST call gc_free_document.
/// </summary>
internal static partial class GridCraftNative
{
    private const string Dll = "gridcraft_ffi";

    /// <summary>
    /// Checks whether gridcraft_ffi.dll is available.
    /// If false, spreadsheet files fall back to an info card.
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
    /// Opaque handle to a parsed XLSX document.
    /// Obtained via gc_open_xlsx, freed via gc_free_document.
    /// </summary>
    internal readonly struct GcDocumentHandle
    {
        public readonly IntPtr Ptr;
        public GcDocumentHandle(IntPtr ptr) => Ptr = ptr;
        public bool IsValid => Ptr != IntPtr.Zero;
    }

    /// <summary>
    /// Exported PDF buffer returned by gc_export_sheet_pdf.
    /// C# MUST call gc_free_pdf_buffer after copying bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GcPdfBuffer
    {
        public IntPtr Data;       // *mut u8
        public nuint Len;
        public uint PageCount;
    }

    // ── Imports ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse an XLSX file from bytes.
    /// Returns null handle on failure.
    /// </summary>
    [LibraryImport(Dll)]
    internal static unsafe partial GcDocumentHandle gc_open_xlsx(
        byte* data,
        nuint len);

    /// <summary>Returns number of sheets in the workbook.</summary>
    [LibraryImport(Dll)]
    internal static partial uint gc_sheet_count(GcDocumentHandle doc);

    /// <summary>
    /// Copy sheet name into caller's buffer. Returns bytes written.
    /// </summary>
    [LibraryImport(Dll)]
    internal static unsafe partial nuint gc_get_sheet_name(
        GcDocumentHandle doc,
        uint index,
        byte* buf,
        nuint bufLen);

    /// <summary>
    /// Writes estimated dimensions of the sheet (in PDF points) into out-parameters.
    /// </summary>
    [LibraryImport(Dll)]
    internal static unsafe partial void gc_get_sheet_size(
        GcDocumentHandle doc,
        uint index,
        double* widthPt,
        double* heightPt);

    /// <summary>
    /// Exports sheet `sheetIndex` (or entire workbook if uint.MaxValue) to PDF bytes.
    /// C# MUST call gc_free_pdf_buffer.
    /// </summary>
    [LibraryImport(Dll)]
    internal static partial GcPdfBuffer gc_export_sheet_pdf(
        GcDocumentHandle doc,
        uint sheetIndex);

    /// <summary>
    /// Free a PDF buffer returned by gc_export_sheet_pdf.
    /// </summary>
    [LibraryImport(Dll)]
    internal static partial void gc_free_pdf_buffer(GcPdfBuffer buf);

    /// <summary>
    /// Extract text from sheet `index` into caller-provided buffer (UTF-8).
    /// </summary>
    [LibraryImport(Dll)]
    internal static unsafe partial nuint gc_extract_sheet_text(
        GcDocumentHandle doc,
        uint index,
        byte* buf,
        nuint bufLen);

    /// <summary>
    /// Search for text across all sheets.
    /// Returns semicolon-separated list: "sheetIndex,row,col;..."
    /// Results written to `buf` (UTF-8). Returns bytes written.
    /// </summary>
    [LibraryImport(Dll)]
    internal static unsafe partial nuint gc_search_text(
        GcDocumentHandle doc,
        byte* query,
        nuint queryLen,
        [MarshalAs(UnmanagedType.U1)] bool caseSensitive,
        byte* buf,
        nuint bufLen);

    /// <summary>
    /// Free document handle returned by gc_open_xlsx.
    /// </summary>
    [LibraryImport(Dll)]
    internal static partial void gc_free_document(GcDocumentHandle doc);

    // ── JSON sheet-data export ────────────────────────────────────────────────

    /// <summary>
    /// Returned by gc_get_sheet_data_json. Must be freed with gc_free_json_buffer.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GcJsonBuffer
    {
        public IntPtr Data;   // *mut u8
        public nuint  Len;
    }

    /// <summary>
    /// Exports rich cell data for sheet <paramref name="index"/> as compact JSON.
    /// C# MUST call gc_free_json_buffer after copying.
    /// </summary>
    [LibraryImport(Dll)]
    internal static partial GcJsonBuffer gc_get_sheet_data_json(
        GcDocumentHandle doc,
        uint index);

    /// <summary>
    /// Free a JSON buffer returned by gc_get_sheet_data_json.
    /// </summary>
    [LibraryImport(Dll)]
    internal static partial void gc_free_json_buffer(GcJsonBuffer buf);
}
