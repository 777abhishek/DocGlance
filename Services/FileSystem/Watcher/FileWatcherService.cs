using System;
using System.IO;

namespace DocGlance.Services.FileSystem.Watcher;

/// <summary>
/// Watches indexed folders for file-system changes and raises events
/// so the UI can refresh without a full rescan.
/// </summary>
public sealed class FileWatcherService : IDisposable
{
    private FileSystemWatcher? _watcher;

    public event EventHandler<string>? FileCreated;
    public event EventHandler<string>? FileDeleted;
    public event EventHandler<string>? FileChanged;

    public void Watch(string folderPath)
    {
        _watcher?.Dispose();
        if (!Directory.Exists(folderPath)) return;

        _watcher = new FileSystemWatcher(folderPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter          = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents   = true,
        };

        _watcher.Created += (_, e) => FileCreated?.Invoke(this, e.FullPath);
        _watcher.Deleted += (_, e) => FileDeleted?.Invoke(this, e.FullPath);
        _watcher.Changed += (_, e) => FileChanged?.Invoke(this, e.FullPath);
    }

    public void Stop()
    {
        if (_watcher != null) _watcher.EnableRaisingEvents = false;
    }

    public void Dispose() => _watcher?.Dispose();
}
