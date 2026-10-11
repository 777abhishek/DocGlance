using System;
using System.Collections.Generic;

namespace QuickView.Rendering.Pdf;

/// <summary>
/// Holds the open PDFium document handle and pre-cached page sizes.
/// Native handle is only valid while this instance is not disposed; all
/// PDFium calls must run under <see cref="Pdfium.Gate"/>.
/// </summary>
public sealed class PdfDocumentState : IDisposable
{
    private IntPtr _document;
    private int _generation;
    private bool _disposed;

    public int PageCount { get; }
    public IReadOnlyList<(double Width, double Height)> PageSizes { get; }
    
    /// <summary>Generation counter incremented each time document is opened. Helps detect stale async operations.</summary>
    public int Generation => _generation;

    /// <summary>Native handle. Read only while holding <see cref="Pdfium.Gate"/>.</summary>
    internal IntPtr Handle
    {
        get
        {
            if (_disposed)
            {
                Infrastructure.Logging.Logger.Log($"[PdfDocumentState] WARNING: Handle accessed after disposal! gen={_generation}");
                return IntPtr.Zero;
            }
            return _document;
        }
    }

    internal PdfDocumentState(IntPtr doc, int pageCount, IReadOnlyList<(double Width, double Height)> pageSizes)
    {
        _document = doc;
        PageCount = pageCount;
        PageSizes = pageSizes;
        // Use TickCount64 (signed 64-bit) to avoid wraparound after ~25 days uptime.
        // Environment.TickCount (32-bit) wraps to negative which breaks generation comparison.
        _generation = (int)(Environment.TickCount64 & 0x7FFFFFFF);
        Infrastructure.Logging.Logger.Log($"[PdfDocumentState] Created: handle=0x{doc.ToString("X")}, pages={pageCount}, gen={_generation}");
    }

    public (double Width, double Height) GetPageSize(int pageIndex)
    {
        if (_disposed)
        {
            Infrastructure.Logging.Logger.Log($"[PdfDocumentState] WARNING: GetPageSize called after disposal!");
            return (612, 792);
        }
        
        if (pageIndex >= 0 && pageIndex < PageSizes.Count)
            return PageSizes[pageIndex];
        return (612, 792);
    }

    public void Dispose()
    {
        var tid = Environment.CurrentManagedThreadId;
        Infrastructure.Logging.Logger.Log($"[PdfDocumentState] T{tid} Dispose called: gen={_generation}, disposed={_disposed}");
        
        lock (Pdfium.Gate)
        {
            if (_disposed || _document == IntPtr.Zero)
            {
                Infrastructure.Logging.Logger.Log($"[PdfDocumentState] T{tid} Already disposed or null handle");
                return;
            }
            
            Infrastructure.Logging.Logger.Log($"[PdfDocumentState] T{tid} Calling FPDF_CloseDocument: handle=0x{_document.ToString("X")}");
            Pdfium.FPDF_CloseDocument(_document);
            _document = IntPtr.Zero;
            _disposed = true;
            Infrastructure.Logging.Logger.Log($"[PdfDocumentState] T{tid} Dispose complete");
        }
    }
}
