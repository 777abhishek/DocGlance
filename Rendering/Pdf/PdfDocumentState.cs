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

    public int PageCount { get; }
    public IReadOnlyList<(double Width, double Height)> PageSizes { get; }

    /// <summary>Native handle. Read only while holding <see cref="Pdfium.Gate"/>.</summary>
    internal IntPtr Handle => _document;

    internal PdfDocumentState(IntPtr doc, int pageCount, IReadOnlyList<(double Width, double Height)> pageSizes)
    {
        _document = doc;
        PageCount = pageCount;
        PageSizes = pageSizes;
    }

    public (double Width, double Height) GetPageSize(int pageIndex)
    {
        if (pageIndex >= 0 && pageIndex < PageSizes.Count)
            return PageSizes[pageIndex];
        return (612, 792);
    }

    public void Dispose()
    {
        lock (Pdfium.Gate)
        {
            if (_document == IntPtr.Zero) return;
            Pdfium.FPDF_CloseDocument(_document);
            _document = IntPtr.Zero;
        }
    }
}
