using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using QuickView.Infrastructure.Concurrency;
using QuickView.Infrastructure.Logging;

namespace QuickView.Rendering.Pdf;

/// <summary>
/// Owns an open PdfCraft document and its cached page dimensions.
///
/// Lifetime contract: every native call must run under a lease from <see cref="_gate"/>
/// (see <see cref="BeginNativeCall"/>). Disposal waits (bounded) for in-flight native
/// calls (on a background thread, never the caller's) before freeing the Rust document — otherwise `pc_free_document` can race an
/// in-flight `pc_render_page` and the process dies with an access violation.
/// </summary>
public sealed class PdfDocumentState : IDisposable
{
    /// <summary>How long Dispose waits for in-flight native calls before refusing to free.</summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(30);

    private IntPtr _document;
    private PdfDocument? _managedPdfDocument;
    private readonly NativeCallGate _gate = new();
    private int _generation;
    private bool _disposed;

    public int PageCount { get; }
    public IReadOnlyList<(double Width, double Height)> PageSizes { get; }
    public int Generation => _generation;

    /// <summary>
    /// Native handle for callers that already hold a gate lease. Zero when disposed.
    /// Do not call native functions with this directly — use <see cref="BeginNativeCall"/>.
    /// </summary>
    internal IntPtr Handle => _disposed ? IntPtr.Zero : _document;

    /// <summary>True once <see cref="Dispose"/> has started.</summary>
    public bool IsDisposed => _gate.IsDisposed;

    internal PdfDocumentState(
        PdfDocument document,
        int pageCount,
        IReadOnlyList<(double Width, double Height)> pageSizes)
    {
        _managedPdfDocument = document;
        PageCount = pageCount;
        PageSizes = pageSizes;
        _generation = (int)(Environment.TickCount64 & 0x7FFFFFFF);
    }

    internal PdfDocument? ManagedPdfDocument => _disposed ? null : _managedPdfDocument;

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

    /// <summary>
    /// Takes a gate lease and returns the native handle for exactly one native call.
    /// Returns (IntPtr.Zero, null) when the document is disposed — the caller must
    /// treat that as "render failed" and skip. The lease must be disposed in a finally.
    /// </summary>
    internal (IntPtr Handle, NativeCallGate.Lease? Lease) BeginNativeCall()
    {
        if (_managedPdfDocument != null)
            return (IntPtr.Zero, null);

        var lease = _gate.TryEnter();
        if (lease == null)
            return (IntPtr.Zero, null);

        // Safe read: _document is only zeroed after the gate drains, and we hold a lease.
        return (_document, lease);
    }

    public void Dispose()
    {
        if (_disposed) return;
        IncrementGeneration();
        _disposed = true;

        // Common case: nothing in flight, free right away on the caller's thread.
        if (_gate.Dispose(TimeSpan.Zero))
        {
            FreeNativeDocument();
            return;
        }

        // A native call is still running. Never block the caller (usually the UI thread):
        // drain on a background thread, then free.
        _ = Task.Run(() =>
        {
            if (_gate.WaitForDrain(DrainTimeout))
            {
                FreeNativeDocument();
                return;
            }

            // In-flight native calls did not finish within the timeout (pathological page,
            // wedged renderer). Freeing now would crash the process, and waiting forever
            // would leak a thread — leak the native document instead and say so loudly.
            Logger.Write(LogLevel.Error,
                "Event=PdfDocumentDisposeLeaked | " +
                $"ActiveCalls={_gate.ActiveCallCount} PageCount={PageCount}");
        });
    }

    private void FreeNativeDocument()
    {
        _managedPdfDocument = null;
        IntPtr document = System.Threading.Interlocked.Exchange(ref _document, IntPtr.Zero);
        if (document != IntPtr.Zero)
            PdfCraftNative.pc_free_document(document);
    }
}
