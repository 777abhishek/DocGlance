using System.Threading;

namespace QuickView.Infrastructure.Concurrency;

/// <summary>
/// Reference-counted gate that makes "dispose a native document" safe while native
/// calls are in flight. Every entry into native code takes a lease; Dispose blocks
/// until the in-flight count drains to zero (or a timeout elapses).
///
/// Without this, `pc_free_document` can free a Rust document while a thread-pool
/// thread is executing `pc_render_page` inside it — undefined behavior that kills
/// the process (access violation, uncatchable from managed code).
///
/// The other document wrappers (PptxDocument, DocxDocument, SpreadsheetDocument)
/// achieve the same guarantee with a plain lock; this class adds a bounded wait and
/// is unit-testable without any native dependency.
/// </summary>
public sealed class NativeCallGate
{
    private int _disposed;
    private int _active;
    private readonly ManualResetEventSlim _drained = new(initialState: true);

    /// <summary>True once <see cref="Dispose"/> has been called.</summary>
    public bool IsDisposed => _disposed == 1;

    /// <summary>Number of leases currently held (diagnostics).</summary>
    public int ActiveCallCount => _active;

    /// <summary>
    /// Takes a lease for one native call, or returns null when the document is
    /// disposed. Lease must be disposed exactly once.
    /// </summary>
    public Lease? TryEnter()
    {
        if (_disposed == 1) return null;

        // First in-flight call closes the drain gate so a concurrent Dispose waits.
        if (Interlocked.Increment(ref _active) == 1)
            _drained.Reset();

        // Re-check after winning the count: Dispose may have started while we incremented.
        if (_disposed == 1)
        {
            Release();
            return null;
        }

        return new Lease(this);
    }

    /// <summary>
    /// Marks the gate disposed and waits up to <paramref name="drainTimeout"/> for
    /// in-flight calls to finish. Returns true when drained (safe to free the native
    /// document); false means calls were still running — the caller MUST NOT free and
    /// should log the leak.
    /// </summary>
    public bool Dispose(System.TimeSpan drainTimeout)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return true; // already disposed/drained by an earlier call

        return _drained.Wait(drainTimeout);
    }

    /// <summary>
    /// Waits for in-flight calls to finish without changing the disposed state. Intended for a
    /// background thread after <see cref="Dispose"/> returned false, so the original caller
    /// (typically the UI thread) never blocks.
    /// </summary>
    public bool WaitForDrain(System.TimeSpan timeout) => _drained.Wait(timeout);

    private void Release()
    {
        if (Interlocked.Decrement(ref _active) == 0)
            _drained.Set();
    }

    /// <summary>Scoped lease for one native call.</summary>
    public sealed class Lease : System.IDisposable
    {
        private NativeCallGate? _gate;

        internal Lease(NativeCallGate gate) => _gate = gate;

        public void Dispose()
        {
            var gate = _gate;
            _gate = null;
            gate?.Release();
        }
    }
}
