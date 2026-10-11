using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Rendering.Document;

/// <summary>
/// A text search match in a spreadsheet.
/// </summary>
public readonly record struct SpreadsheetMatch(int SheetIndex, int Row, int Col);

/// <summary>
/// Managed wrapper around a GridCraft XLSX document.
/// Handles memory lifetime, sheet queries, PDF export, search, and text extraction.
/// </summary>
public sealed class SpreadsheetDocument : IDisposable
{
    private GridCraftNative.GcDocumentHandle _handle;
    private readonly SemaphoreSlim _exportLock = new(1, 1);
    private bool _disposed;

    private SpreadsheetDocument(GridCraftNative.GcDocumentHandle handle)
    {
        _handle = handle;
    }

    public int SheetCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return (int)GridCraftNative.gc_sheet_count(_handle);
        }
    }

    /// <summary>
    /// Parse an XLSX file.
    /// Returns null if the file cannot be opened or gridcraft_ffi.dll is missing.
    /// </summary>
    public static async Task<SpreadsheetDocument?> OpenAsync(string filePath, CancellationToken ct = default)
    {
        if (!GridCraftNative.IsAvailable)
            return null;

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(filePath, ct);
        }
        catch
        {
            return null;
        }

        var handle = await Task.Run(() =>
        {
            unsafe
            {
                fixed (byte* ptr = bytes)
                {
                    return GridCraftNative.gc_open_xlsx(ptr, (nuint)bytes.Length);
                }
            }
        }, ct);

        return handle.IsValid ? new SpreadsheetDocument(handle) : null;
    }

    /// <summary>
    /// Gets the name of sheet `index`.
    /// </summary>
    public string GetSheetName(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var buf = new byte[256];
        unsafe
        {
            fixed (byte* pBuf = buf)
            {
                nuint written = GridCraftNative.gc_get_sheet_name(_handle, (uint)index, pBuf, (nuint)buf.Length);
                return written > 0 ? Encoding.UTF8.GetString(buf, 0, (int)written) : $"Sheet {index + 1}";
            }
        }
    }

    /// <summary>
    /// Returns sheet dimensions in PDF points.
    /// </summary>
    public (double widthPt, double heightPt) GetSheetSize(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        unsafe
        {
            double w, h;
            GridCraftNative.gc_get_sheet_size(_handle, (uint)index, &w, &h);
            return (w, h);
        }
    }

    /// <summary>
    /// Exports sheet `sheetIndex` (or whole workbook if null) to PDF bytes.
    /// </summary>
    public async Task<byte[]?> ExportToPdfAsync(int? sheetIndex = null, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _exportLock.WaitAsync(ct);
        try
        {
            uint idx = sheetIndex.HasValue ? (uint)sheetIndex.Value : uint.MaxValue;

            return await Task.Run(() =>
            {
                var buf = GridCraftNative.gc_export_sheet_pdf(_handle, idx);
                if (buf.Data == IntPtr.Zero || buf.Len == 0)
                    return null;

                try
                {
                    var result = new byte[(int)buf.Len];
                    Marshal.Copy(buf.Data, result, 0, (int)buf.Len);
                    return result;
                }
                finally
                {
                    GridCraftNative.gc_free_pdf_buffer(buf);
                }
            }, ct);
        }
        finally
        {
            _exportLock.Release();
        }
    }

    /// <summary>
    /// Extract text from sheet `index`.
    /// </summary>
    public string ExtractSheetText(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var buf = new byte[128 * 1024]; // 128 KB
        unsafe
        {
            fixed (byte* pBuf = buf)
            {
                nuint written = GridCraftNative.gc_extract_sheet_text(_handle, (uint)index, pBuf, (nuint)buf.Length);
                return written > 0 ? Encoding.UTF8.GetString(buf, 0, (int)written) : string.Empty;
            }
        }
    }

    /// <summary>
    /// Search for `query` across all sheets.
    /// </summary>
    public List<SpreadsheetMatch> SearchText(string query, bool caseSensitive = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrEmpty(query))
            return [];

        var queryBytes = Encoding.UTF8.GetBytes(query);
        var buf = new byte[256 * 1024]; // 256 KB

        unsafe
        {
            fixed (byte* pQuery = queryBytes)
            fixed (byte* pBuf = buf)
            {
                nuint written = GridCraftNative.gc_search_text(
                    _handle,
                    pQuery,
                    (nuint)queryBytes.Length,
                    caseSensitive,
                    pBuf,
                    (nuint)buf.Length);

                if (written == 0)
                    return [];

                string str = Encoding.UTF8.GetString(buf, 0, (int)written);
                var matches = new List<SpreadsheetMatch>();
                var parts = str.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var tokens = part.Split(',');
                    if (tokens.Length >= 3 &&
                        int.TryParse(tokens[0], out int sIdx) &&
                        int.TryParse(tokens[1], out int row) &&
                        int.TryParse(tokens[2], out int col))
                    {
                        matches.Add(new SpreadsheetMatch(sIdx, row, col));
                    }
                }
                return matches;
            }
        }
    }

    /// <summary>
    /// Returns a compact JSON string describing all cells and layout for sheet <paramref name="index"/>.
    /// Returns null if the sheet doesn't exist or the FFI fails.
    /// </summary>
    public async Task<string?> GetSheetDataJsonAsync(int index, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return await Task.Run(() =>
        {
            var buf = GridCraftNative.gc_get_sheet_data_json(_handle, (uint)index);
            if (buf.Data == IntPtr.Zero || buf.Len == 0)
                return null;

            try
            {
                unsafe
                {
                    return Encoding.UTF8.GetString((byte*)buf.Data, (int)buf.Len);
                }
            }
            finally
            {
                GridCraftNative.gc_free_json_buffer(buf);
            }
        }, ct);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_handle.IsValid)
        {
            GridCraftNative.gc_free_document(_handle);
            _handle = default;
        }

        _exportLock.Dispose();
    }
}
