using System;
using System.IO;
using System.Text;
using System.Threading;

namespace DocGlance.Infrastructure.Logging;

/// <summary>
/// File sink for the logger: appending batches and size-based rotation with a
/// bounded set of archives (app.log → app.log.1 … app.log.N). Thread-safe via a
/// single lock — the Logger's writer thread is the only producer in production,
/// but tests exercise it concurrently. Never throws.
/// </summary>
internal sealed class LogFileWriter
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _retainedArchives;
    private readonly object _io = new();

    public LogFileWriter(string path, long maxBytes, int retainedArchives)
    {
        _path = path;
        _maxBytes = maxBytes;
        _retainedArchives = retainedArchives;
    }

    /// <summary>Appends one batch of pre-formatted lines, rotating first if needed.</summary>
    public void Append(string content)
    {
        try
        {
            lock (_io)
            {
                RotateIfNeeded();
                File.AppendAllText(_path, content);
            }
        }
        catch
        {
            // Disk failures must not propagate into the logger writer thread.
        }
    }

    /// <summary>Rotates when the file exceeds the size budget: shifts archives and restarts app.log.</summary>
    private void RotateIfNeeded()
    {
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length < _maxBytes) return;

        var oldest = _path + "." + _retainedArchives;
        if (File.Exists(oldest))
            File.Delete(oldest);

        for (int i = _retainedArchives - 1; i >= 1; i--)
        {
            var source = _path + "." + i;
            if (File.Exists(source))
                File.Move(source, _path + "." + (i + 1));
        }

        File.Move(_path, _path + ".1");
        File.WriteAllText(_path,
            $"=== DocGlance log (rotated) - {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz} ===\n");
    }
}
