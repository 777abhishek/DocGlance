using System;
using System.Diagnostics;
using System.Threading;

namespace DocGlance.Infrastructure.Logging;

/// <summary>
/// Correlated operation scope returned by <see cref="Logger.BeginOperation"/>.
/// Logs `Event=OperationStarted` on creation and `Event=OperationCompleted /
/// OperationCancelled / OperationFailed` with elapsed duration on Dispose.
/// Disposing twice is harmless. Never throws.
/// </summary>
public sealed class LogScope : IDisposable
{
    private static long _nextId;

    private readonly string _operation;
    private readonly string _fields;
    private readonly long _startTimestamp;
    private int _outcome; // 0 = completed, 1 = cancelled, 2 = failed

    /// <summary>Stable correlation id shared by every entry of this operation.</summary>
    public string OperationId { get; }

    internal LogScope(string operation, (string Key, object? Value)[] fields)
    {
        _operation = operation ?? "Operation";
        OperationId = Interlocked.Increment(ref _nextId).ToString("x8");
        _startTimestamp = Stopwatch.GetTimestamp();
        _fields = RenderFields(fields);

        Logger.Write(LogLevel.Debug, $"Event=OperationStarted Operation={_operation} OperationId={OperationId}{_fields}");
    }

    /// <summary>Marks the operation as cancelled (completion entry says Cancelled=true).</summary>
    public void MarkCancelled() => Interlocked.Exchange(ref _outcome, 1);

    /// <summary>Marks the operation as failed (completion entry says Failed=true).</summary>
    public void Fail() => Interlocked.Exchange(ref _outcome, 2);

    /// <summary>Elapsed wall time since the scope started.</summary>
    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(_startTimestamp);

    public void Dispose()
    {
        var outcome = Interlocked.Exchange(ref _outcome, -1);
        if (outcome == -1) return; // already disposed

        var level = outcome == 2 ? LogLevel.Error : LogLevel.Debug;
        var name = outcome switch
        {
            1 => "OperationCancelled",
            2 => "OperationFailed",
            _ => "OperationCompleted",
        };
        Logger.Write(level,
            $"Event={name} Operation={_operation} OperationId={OperationId} " +
            $"DurationMs={Elapsed.TotalMilliseconds:F1}{_fields}");
    }

    private static string RenderFields((string Key, object? Value)[] fields)
    {
        if (fields is not { Length: > 0 }) return string.Empty;
        var sb = new System.Text.StringBuilder();
        foreach (var (key, value) in fields)
        {
            sb.Append(' ').Append(key).Append('=');
            try { sb.Append(value?.ToString() ?? "null"); }
            catch { sb.Append("<render-failed>"); }
        }
        return sb.ToString();
    }
}
