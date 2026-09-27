using System;
using System.IO;

namespace QuickView.Infrastructure.Logging;

public static class Logger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        "QuickViewApp.log");

    private static readonly object _lock = new();

    static Logger()
    {
        try { File.WriteAllText(LogPath, $"=== QuickView App Log - {DateTime.Now} ===\n\n"); }
        catch { }
    }

    public static void Log(string message)
    {
        try
        {
            lock (_lock)
            {
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n";
                File.AppendAllText(LogPath, line);
                Console.WriteLine(line.TrimEnd());
            }
        }
        catch { }
    }

    public static void Error(string message, Exception? ex = null)
    {
        Log(ex != null
            ? $"ERROR: {message}\n{ex.Message}\n{ex.StackTrace}"
            : $"ERROR: {message}");
    }
}
