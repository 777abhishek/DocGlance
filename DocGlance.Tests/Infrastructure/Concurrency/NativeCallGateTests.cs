using DocGlance.Infrastructure.Concurrency;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace DocGlance.Tests.Infrastructure.Concurrency;

public sealed class NativeCallGateTests
{
    [Fact]
    public void TryEnter_BeforeDispose_ReturnsLease()
    {
        var gate = new NativeCallGate();

        using var lease = gate.TryEnter();

        Assert.NotNull(lease);
        Assert.Equal(1, gate.ActiveCallCount);
        Assert.False(gate.IsDisposed);
    }

    [Fact]
    public void Lease_Dispose_Releases()
    {
        var gate = new NativeCallGate();
        var lease = gate.TryEnter();

        lease!.Dispose();

        Assert.Equal(0, gate.ActiveCallCount);
    }

    [Fact]
    public void Lease_DisposeTwice_DoesNotUnderflow()
    {
        var gate = new NativeCallGate();
        var lease = gate.TryEnter();

        lease!.Dispose();
        lease.Dispose();

        Assert.Equal(0, gate.ActiveCallCount);
    }

    [Fact]
    public void TryEnter_AfterDispose_ReturnsNull()
    {
        var gate = new NativeCallGate();
        gate.Dispose(TimeSpan.Zero);

        var lease = gate.TryEnter();

        Assert.Null(lease);
        Assert.True(gate.IsDisposed);
    }

    [Fact]
    public void Dispose_WithNoActiveCalls_ReturnsTrueImmediately()
    {
        var gate = new NativeCallGate();

        bool drained = gate.Dispose(TimeSpan.FromSeconds(5));

        Assert.True(drained);
    }

    [Fact]
    public void Dispose_Twice_SecondCallReturnsTrueWithoutHanging()
    {
        var gate = new NativeCallGate();
        gate.Dispose(TimeSpan.Zero);

        bool drained = gate.Dispose(TimeSpan.FromSeconds(5));

        Assert.True(drained);
    }

    [Fact]
    public async Task Dispose_WaitsForInFlightCall()
    {
        var gate = new NativeCallGate();
        var lease = gate.TryEnter();
        Assert.NotNull(lease);

        bool? drained = null;
        var disposeTask = Task.Run(() => drained = gate.Dispose(TimeSpan.FromSeconds(5)));

        // Give Dispose time to observe the active call and block.
        await Task.Delay(100);
        Assert.Null(drained);

        lease?.Dispose();
        var finished = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(disposeTask, finished);
        Assert.True(drained);
    }

    [Fact]
    public async Task Dispose_TimeoutWithStuckCall_ReturnsFalse()
    {
        var gate = new NativeCallGate();
        using var lease = gate.TryEnter();

        bool drained = await Task.Run(() => gate.Dispose(TimeSpan.FromMilliseconds(100)));

        Assert.False(drained);
        Assert.True(gate.IsDisposed);
    }

    [Fact]
    public void Dispose_ZeroTimeoutWithActiveCall_ReturnsFalseWithoutBlocking_ThenWaitForDrainSucceeds()
    {
        var gate = new NativeCallGate();
        var lease = gate.TryEnter();
        Assert.NotNull(lease);

        // This is how PdfDocumentState disposes on the UI thread: never block.
        Assert.False(gate.Dispose(TimeSpan.Zero));
        Assert.False(gate.WaitForDrain(TimeSpan.FromMilliseconds(50)));

        lease!.Dispose();

        Assert.True(gate.WaitForDrain(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task TryEnter_DuringDisposalWindow_ReturnsNull()
    {
        var gate = new NativeCallGate();
        using var lease = gate.TryEnter();
        Assert.NotNull(lease);

        // Start a Dispose that will block on the active lease.
        var disposeTask = Task.Run(() => gate.Dispose(TimeSpan.FromSeconds(5)));
        await Task.Delay(100);

        // Concurrent entry attempts must be refused once disposal started.
        var rejected = gate.TryEnter();
        Assert.Null(rejected);

        lease.Dispose();
        await disposeTask;
    }

    [Fact]
    public async Task Stress_ManyConcurrentLeases_AllReleaseCleanly()
    {
        var gate = new NativeCallGate();
        var rnd = new Random(42);

        var tasks = new Task[32];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                for (int j = 0; j < 50; j++)
                {
                    var lease = gate.TryEnter();
                    if (lease != null)
                    {
                        await Task.Delay(rnd.Next(0, 2));
                        lease.Dispose();
                    }
                }
            });
        }

        await Task.WhenAll(tasks);

        Assert.Equal(0, gate.ActiveCallCount);
        Assert.True(gate.Dispose(TimeSpan.FromSeconds(5)));
    }
}
