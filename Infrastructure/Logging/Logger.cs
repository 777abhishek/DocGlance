using System;
using System.IO;
using System.Text.RegularExpressions;

namespace QuickView.Infrastructure.Logging;

public static class Logger
{
    private static readonly string LogDir;
    private static readonly string LogPath;
    private static readonly object _lock = new();
    private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB
    
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
            lock (_lock)
            {
                File.AppendAllText(LogPath, $"\n=== QuickView Session Start - {DateTime.Now} ===\n");
            }
        }
        catch
        {
            // Fallback to temp if AppData fails
            LogDir = Path.GetTempPath();
            LogPath = Path.Combine(LogDir, "QuickView_app.log");
        }
    }

    public static void Log(string message)
    {
        try
        {
            lock (_lock)
            {
                RotateIfNeeded();
                
                // Sanitize the message to remove potentially sensitive paths
                var sanitized = SanitizePath(message);
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] {sanitized}\n";
                File.AppendAllText(LogPath, line);
                
                // Console.WriteLine removed — WinExe apps have no console, this is dead code.
            }
        }
        catch { }
    }

    public static void Error(string message, Exception? ex = null)
    {
        // Sanitize BOTH ex.Message and ex.StackTrace for PII (file paths in exceptions).
        var errorMsg = ex != null
            ? $"ERROR: {SanitizePath(message)}\n{SanitizePath(ex.Message)}\n{SanitizePath(ex.StackTrace ?? "")}"
            : $"ERROR: {SanitizePath(message)}";
        Log(errorMsg);
    }

    /// <summary>
    /// Rotates the log file when it exceeds 5 MB.
    /// Renames app.log → app.log.1, discarding the old .1 if it exists.
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
