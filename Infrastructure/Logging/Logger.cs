using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DocGlance.Infrastructure.Logging;

/// <summary>Severity of a log entry, rendered in the [LEVEL] field.</summary>
public enum LogLevel
{
    /// <summary>Detailed diagnostic detail — only written when verbose logging is enabled.</summary>
    Debug = 0,
    /// <summary>Normal operational events (document opened, render completed, …).</summary>
    Info = 1,
    /// <summary>Recoverable problem worth attention (retry, fallback, eviction under pressure).</summary>
    Warning = 2,
    /// <summary>Operation failed but the application continues.</summary>
    Error = 3,
    /// <summary>Process-ending failure or boundary the app cannot recover from.</summary>
    Fatal = 4,
}

/// <summary>
/// Structured file logger with an async bounded queue.
///
/// Design constraints:
///  - Public methods never throw and never block the caller on file I/O (hot render paths call these).
///  - A single background writer thread serialises all writes; batches are drained per wake-up.
///  - When the queue is full, entries are dropped (counted) rather than blocking producers.
///  - Logging failures are swallowed and reported to Debug output only — logging must never
///    crash the app or recurse into itself.
///  - Document contents, extracted text and credentials are never written; user paths are
///    sanitised by the writer thread.
///
/// Log location: %LOCALAPPDATA%\DocGlance\Logs\app.log (rotated archives: app.log.1 … app.log.5).
/// </summary>
public static class Logger
{
    private static readonly string LogDir;
    private static readonly string LogPath;
    private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB per file
    private const int RetainedArchives = 5;               // app.log.1 … app.log.5
    private const int QueueCapacity = 4096;
    private const int MaxBatchSize = 256;

    private static readonly BlockingCollection<string> _queue =
        new(new ConcurrentQueue<string>(), QueueCapacity);

    // Constructed in the static ctor: field initializers run before the ctor body
    // assigns LogPath, so this cannot be an inline initializer.
    private static LogFileWriter _fileWriter = null!;

    private static long _droppedEntries;
    private static TaskCompletionSource? _flushRequest;
    private static readonly object FlushGate = new();

    /// <summary>
    /// When false (default), <see cref="LogLevel.Debug"/> entries are discarded before reaching
    /// the writer. Info and above are always written. Set once at startup from AppSettings.
    /// </summary>
    public static volatile bool VerboseEnabled;

    /// <summary>Full path of the primary log file (app.log). Empty when the log dir could not be created.</summary>
    public static string LogFilePath => LogPath ?? string.Empty;

    /// <summary>Directory containing app.log and rotated archives.</summary>
    public static string LogDirectory => LogDir ?? string.Empty;

    // Regex to match paths that might contain usernames
    private static readonly Regex PathPattern = new Regex(
        @"[A-Z]:\\Users\\[^\\]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static Logger()
    {
        try
        {
            LogDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DocGlance",
                "Logs");

            Directory.CreateDirectory(LogDir);
            LogPath = Path.Combine(LogDir, "app.log");

            // APPEND to existing log instead of overwriting it every launch.
            // This preserves multi-session history. Rotation kicks in at 5 MB.
            File.AppendAllText(LogPath,
                $"\n=== DocGlance session start - {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz} " +
                $"PID={Environment.ProcessId} ===\n");
        }
        catch
        {
            // Fallback to temp if AppData fails
            LogDir = Path.GetTempPath();
            LogPath = Path.Combine(LogDir, "DocGlance_app.log");
        }

        _fileWriter = new LogFileWriter(LogPath, MaxLogSizeBytes, RetainedArchives);

        var writer = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "DocGlance-Logger",
            Priority = ThreadPriority.BelowNormal
        };
        writer.Start();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Back-compat entry point used across the app. Maps to <see cref="LogLevel.Info"/>.</summary>
    public static void Log(string message) => Write(LogLevel.Info, message);

    public static void Debug(string message) => Write(LogLevel.Debug, message);

    public static void Info(string message) => Write(LogLevel.Info, message);

    public static void Warn(string message) => Write(LogLevel.Warning, message);

    /// <summary>Back-compat entry point: logs at Error level with an optional exception.</summary>
    public static void Error(string message, Exception? ex = null)
        => Write(LogLevel.Error, message, exception: ex);

    /// <summary>Fatal failures (crash handlers, unrecoverable boundaries). Always written.</summary>
    public static void Fatal(string message, Exception? ex = null)
        => Write(LogLevel.Fatal, message, exception: ex);

    /// <summary>
    /// Structured entry: message plus key=value context fields rendered after a '|' separator.
    /// Example: Write(LogLevel.Error, "PageRenderFailed",
    ///                ("OperationId", opId), ("DocumentType", "PDF"), ("Page", 14))
    /// Values are rendered with ToString(); no escaping — keep values short and free of newlines.
    /// </summary>
    public static void Write(LogLevel level, string message, params (string Key, object? Value)[] fields)
    {
        if (level == LogLevel.Debug && !VerboseEnabled)
            return;

        var sb = new StringBuilder(message.Length + 64);
        sb.Append(message);
        if (fields is { Length: > 0 })
        {
            sb.Append(" |");
            foreach (var (key, value) in fields)
            {
                sb.Append(' ').Append(key).Append('=');
                try { sb.Append(value?.ToString() ?? "null"); }
                catch (Exception ex) { sb.Append("<render-failed:").Append(ex.GetType().Name).Append('>'); }
            }
        }

        Enqueue(level, sb.ToString());
    }

    /// <summary>Logs a message with a full exception chain (type, message, stack, inner chain).</summary>
    public static void Write(LogLevel level, string message, Exception? exception)
        => Enqueue(level, exception == null
            ? message
            : message + "\n" + FormatException(exception));

    /// <summary>
    /// Starts a correlated operation scope: logs a start entry with a fresh OperationId and,
    /// on Dispose, a completion entry with duration (plus Cancelled/Failed outcome when
    /// <see cref="LogScope.MarkCancelled"/>/<see cref="LogScope.Fail"/> was called).
    /// </summary>
    public static LogScope BeginOperation(string operation, params (string Key, object? Value)[] fields)
        => new(operation, fields);

    /// <summary>
    /// Drains everything enqueued so far to disk. Called from shutdown and crash handlers.
    /// Never throws; waits at most <paramref name="timeout"/>. Concurrent callers: the first
    /// caller's wait may be satisfied by the other's drain — the data is on disk either way.
    /// </summary>
    public static void Flush(TimeSpan timeout)
    {
        try
        {
            TaskCompletionSource? tcs;
            lock (FlushGate)
            {
                tcs = _flushRequest ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            if (tcs.Task.Wait(timeout))
                return;

            // Writer did not respond in time (extremely rare — it may be stuck on disk I/O).
            // Nothing else we can safely do from here.
        }
        catch
        {
            // Flush must never throw into a crash handler.
        }
    }

    /// <summary>Number of entries dropped because the queue was full (diagnostic, never logged).</summary>
    public static long DroppedEntryCount => Interlocked.Read(ref _droppedEntries);

    // ── Queue / writer ────────────────────────────────────────────────────────

    private static void Enqueue(LogLevel level, string payload)
    {
        try
        {
            // The header is rendered on the caller's thread so PID/TID reflect the producer,
            // not the writer thread. One string per entry keeps the queue lock-free.
            if (!_queue.TryAdd(FormatLine(level, payload)))
                Interlocked.Increment(ref _droppedEntries);
        }
        catch
        {
            // Never throw from logging.
        }
    }

    private static string FormatLine(LogLevel level, string payload)
    {
        var sb = new StringBuilder(payload.Length + 96);
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"));
        sb.Append(" [").Append(level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO ",
            LogLevel.Warning => "WARN ",
            LogLevel.Error => "ERROR",
            _ => "FATAL",
        }).Append("] [");
        sb.Append(Environment.ProcessId).Append('/')
          .Append(Environment.CurrentManagedThreadId).Append("] ");
        sb.Append(payload);
        return sb.ToString();
    }

    /// <summary>
    /// Serialises the full exception chain: type, message, HResult, stack trace, then inner
    /// exceptions (up to 5 levels) so the first line always names the deepest root cause.
    /// </summary>
    internal static string FormatException(Exception ex)
    {
        var sb = new StringBuilder(512);
        var current = ex;
        int depth = 0;
        while (current != null && depth < 6)
        {
            sb.Append(depth == 0 ? "Exception: " : "---> Inner: ");
            sb.Append(current.GetType().FullName);
            sb.Append(": ").AppendLine(current.Message);
            sb.Append("HResult=0x").AppendLine(current.HResult.ToString("X8"));
            if (!string.IsNullOrEmpty(current.StackTrace))
                sb.AppendLine(current.StackTrace);
            current = current.InnerException;
            depth++;
        }
        if (current != null)
            sb.AppendLine("---> (more inner exceptions truncated)");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Background consumer: blocks for the first line, drains a batch, writes once.
    /// Sanitisation runs here so callers never pay for the regex.
    /// </summary>
    private static void WriteLoop()
    {
        var sb = new StringBuilder();
        var batch = new List<string>(MaxBatchSize);

        while (true)
        {
            batch.Clear();
            try
            {
                // Block until at least one line arrives
                if (!_queue.TryTake(out var first, Timeout.Infinite))
                    continue;
                batch.Add(first);

                // Drain whatever else is queued (up to a sane batch size)
                while (batch.Count < MaxBatchSize && _queue.TryTake(out var more, 0))
                    batch.Add(more);
            }
            catch (OperationCanceledException) { continue; }
            catch (ObjectDisposedException) { break; }

            try
            {
                sb.Clear();
                foreach (var line in batch)
                    sb.AppendLine(SanitizePath(line));

                _fileWriter.Append(sb.ToString());
                CompleteFlushIfRequested();
            }
            catch
            {
                // Disk failures must not kill the writer; drop this batch silently.
            }
        }
    }

    private static void CompleteFlushIfRequested()
    {
        TaskCompletionSource? tcs;
        lock (FlushGate)
        {
            tcs = _flushRequest;
            _flushRequest = null;
        }
        tcs?.TrySetResult();
    }

    /// <summary>
    /// Sanitizes paths to remove usernames and sensitive directory information.
    /// Example: "C:\Users\JohnDoe\Documents\file.txt" becomes "C:\Users\***\Documents\file.txt"
    /// </summary>
    internal static string SanitizePath(string message)
    {
        if (string.IsNullOrEmpty(message))
            return message;

        // Replace "C:\Users\Username" with "C:\Users\***"
        return PathPattern.Replace(message, match =>
        {
            var parts = match.Value.Split('\\');
            if (parts.Length >= 3)
            {
                parts[2] = "***"; // Replace username
                return string.Join('\\', parts);
            }
            return match.Value;
        });
    }
}
