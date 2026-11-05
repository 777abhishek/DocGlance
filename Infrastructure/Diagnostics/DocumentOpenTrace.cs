using DocGlance.Core.Models;
using DocGlance.Infrastructure.Logging;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace DocGlance.Infrastructure.Diagnostics;

public sealed class DocumentOpenTrace
{
    private static readonly ConditionalWeakTable<DocumentItem, DocumentOpenTrace> Traces = new();
    private static readonly AsyncLocal<DocumentOpenTrace?> CurrentTrace = new();
    private static readonly object RegistryLock = new();

    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly string _id = Guid.NewGuid().ToString("N")[..8];
    private readonly string _format;
    private readonly long? _fileSize;
    private readonly ProcessMetrics _initialMetrics = CaptureProcessMetrics();
    private int _firstContentQueued;

    private DocumentOpenTrace(DocumentItem document)
    {
        _format = Path.GetExtension(document.FilePath).ToLowerInvariant();
        try { _fileSize = new FileInfo(document.FilePath).Length; }
        catch (Exception ex) { Logger.Warn($"[OpenTrace] Could not read file size: {ex.Message}"); }
    }

    public static DocumentOpenTrace Start(DocumentItem document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var trace = new DocumentOpenTrace(document);
        lock (RegistryLock)
        {
            Traces.Remove(document);
            Traces.Add(document, trace);
        }
        trace.Mark("OpenRequested");
        return trace;
    }

    public static DocumentOpenTrace? For(DocumentItem document) =>
        Traces.TryGetValue(document, out DocumentOpenTrace? trace) ? trace : null;

    public static DocumentOpenTrace? Current => CurrentTrace.Value;

    public IDisposable Activate()
    {
        DocumentOpenTrace? previous = CurrentTrace.Value;
        CurrentTrace.Value = this;
        return new Scope(() => CurrentTrace.Value = previous);
    }

    public void Mark(string milestone, string? detail = null)
    {
        ProcessMetrics metrics = CaptureProcessMetrics();

        Logger.Log(
            $"Event=DocumentOpenMilestone Id={_id} Stage={milestone} Format={_format} " +
            $"ElapsedMs={_stopwatch.Elapsed.TotalMilliseconds:F1} " +
            $"FileSizeBytes={_fileSize?.ToString() ?? "unknown"} " +
            $"PrivateBytes={metrics.PrivateBytes} PrivateBytesDelta={metrics.PrivateBytes - _initialMetrics.PrivateBytes} " +
            $"WorkingSetBytes={metrics.WorkingSetBytes} WorkingSetDelta={metrics.WorkingSetBytes - _initialMetrics.WorkingSetBytes} " +
            $"ProcessCpuMs={metrics.CpuMilliseconds:F1} ProcessCpuDeltaMs={metrics.CpuMilliseconds - _initialMetrics.CpuMilliseconds:F1} " +
            $"ProcessAllocatedBytes={metrics.AllocatedBytes} AllocatedBytesDelta={metrics.AllocatedBytes - _initialMetrics.AllocatedBytes} " +
            $"Detail={detail ?? "none"}");
    }

    public void MarkFirstContentRendered()
    {
        if (Interlocked.Exchange(ref _firstContentQueued, 1) != 0)
            return;

        EventHandler<object>? rendering = null;
        rendering = (_, _) =>
        {
            CompositionTarget.Rendering -= rendering;
            Mark("FirstContentRendered");
        };
        CompositionTarget.Rendering += rendering;
    }

    private static ProcessMetrics CaptureProcessMetrics()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            process.Refresh();
            return new ProcessMetrics(
                process.PrivateMemorySize64,
                process.WorkingSet64,
                process.TotalProcessorTime.TotalMilliseconds,
                GC.GetTotalAllocatedBytes(precise: false));
        }
        catch (Exception ex)
        {
            Logger.Warn($"[OpenTrace] Could not sample process metrics: {ex.Message}");
            return new ProcessMetrics(0, 0, 0, GC.GetTotalAllocatedBytes(precise: false));
        }
    }

    private readonly record struct ProcessMetrics(
        long PrivateBytes,
        long WorkingSetBytes,
        double CpuMilliseconds,
        long AllocatedBytes);

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
