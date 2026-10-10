using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace QuickView.Infrastructure.Logging;

/// <summary>
/// File logger with an async bounded queue.
/// Log/Error just enqueue (cheap enough for hot paths like the render loop);
/// a background daemon thread batches, sanitises and writes. If the queue is
/// full, messages are dropped rather than blocking the caller.
/// </summary>
public static class Logger
{
    private static readonly string LogDir;
    private static readonly string LogPath;
    private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB
    private const int QueueCapacity = 2048;

    private static readonly BlockingCollection<string> _queue =
        new(new ConcurrentQueue<string>(), QueueCapacity);

    // Regex to match paths that might contain usernames
    private static readonly Regex PathPattern = new Regex(
        @"[A-Z]:\\Users\\[^\\]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static Logger()
    {
        try
        {
            // Use LocalApplicationData for log directory
            LogDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuickView",
                "Logs");

            Directory.CreateDirectory(LogDir);
            LogPath = Path.Combine(LogDir, "app.log");

            // APPEND to existing log instead of overwriting it every launch.
            // This preserves multi-session history. Rotation kicks in at 5 MB.
            File.AppendAllText(LogPath, $"\n=== QuickView Session Start - {DateTime.Now} ===\n");
        }
        catch
        {
            // Fallback to temp if AppData fails
            LogDir = Path.GetTempPath();
            LogPath = Path.Combine(LogDir, "QuickView_app.log");
        }

        var writer = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "QuickView-Logger",
            Priority = ThreadPriority.BelowNormal
        };
        writer.Start();
    }

    public static void Log(string message)
    {
        try
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            _queue.TryAdd(line);
        }
        catch { }
    }

    public static void Error(string message, Exception? ex = null)
    {
        var errorMsg = ex != null
            ? $"ERROR: {message}\n{ex.Message}\n{ex.StackTrace ?? ""}"
            : $"ERROR: {message}";
        Log(errorMsg);
    }

    /// <summary>
    /// Background consumer: blocks for the first line, drains a batch, writes once.
    /// Sanitisation runs here so callers never pay for the regex.
    /// </summary>
    private static void WriteLoop()
    {
        var sb = new StringBuilder();
        var batch = new List<string>(128);

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
                while (batch.Count < 128 && _queue.TryTake(out var more, 0))
                    batch.Add(more);
            }
            catch (OperationCanceledException) { continue; }
            catch (ObjectDisposedException) { break; }

            try
            {
                sb.Clear();
                foreach (var line in batch)
                    sb.AppendLine(SanitizePath(line));

                RotateIfNeeded();
                File.AppendAllText(LogPath, sb.ToString());
            }
            catch { }
        }
    }

    /// <summary>
    /// Rotates the log file when it exceeds 5 MB.
    /// Renames app.log → app.log.1, discarding the old .1 if it exists.
    /// Runs only on the logger thread.
    /// </summary>
    private static void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(LogPath)) return;

            var info = new FileInfo(LogPath);
            if (info.Length < MaxLogSizeBytes) return;

            var archivePath = LogPath + ".1";

            // Delete old archive if it exists
            if (File.Exists(archivePath))
                File.Delete(archivePath);

            // Rename current log to .1
            File.Move(LogPath, archivePath);

            // Start fresh log
            File.WriteAllText(LogPath, $"=== QuickView Log (rotated) - {DateTime.Now} ===\n");
        }
        catch { }
    }

    /// <summary>
    /// Sanitizes paths to remove usernames and sensitive directory information.
    /// Example: "C:\Users\JohnDoe\Documents\file.txt" becomes "C:\Users\***\Documents\file.txt"
    /// </summary>
    private static string SanitizePath(string message)
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
