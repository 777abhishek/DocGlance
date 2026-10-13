using System;
using System.Collections.Generic;

namespace QuickView.Rendering.Pdf;

/// <summary>Owns an open PdfCraft document and its cached page dimensions.</summary>
public sealed class PdfDocumentState : IDisposable
{
    private IntPtr _document;
    private int _generation;
    private bool _disposed;

    public int PageCount { get; }
    public IReadOnlyList<(double Width, double Height)> PageSizes { get; }
    public int Generation => _generation;

    internal IntPtr Handle => _disposed ? IntPtr.Zero : _document;

    internal PdfDocumentState(
        IntPtr document,
        int pageCount,
        IReadOnlyList<(double Width, double Height)> pageSizes)
    {
        _document = document;
        PageCount = pageCount;
        PageSizes = pageSizes;
        _generation = (int)(Environment.TickCount64 & 0x7FFFFFFF);
    }

    public (double Width, double Height) GetPageSize(int pageIndex)
        => !_disposed && (uint)pageIndex < (uint)PageSizes.Count
            ? PageSizes[pageIndex]
            : (612, 792);

    public void IncrementGeneration() => System.Threading.Interlocked.Increment(ref _generation);

    public void Dispose()
    {
        if (_disposed) return;
        IncrementGeneration();
        IntPtr document = _document;
        _document = IntPtr.Zero;
        _disposed = true;
        if (document != IntPtr.Zero)
            PdfCraftNative.pc_free_document(document);
    }
}
