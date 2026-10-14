using System;
using System.Collections.Generic;

namespace DocGlance.Services.Storage;

/// <summary>Stores the list of recently opened file paths.</summary>
public static class RecentFilesStore
{
    private const string Key     = "recent_files";
    private const int    MaxSize = 50;

    public static List<string> Load() =>
        SettingsStore.Load<List<string>>(Key) ?? new();

    public static void Push(string filePath)
    {
        var list = Load();
        // Remove any existing entry for the same path regardless of case
        // so that C:\Foo\bar.txt and c:\foo\bar.txt don't create two entries.
        list.RemoveAll(p => p.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, filePath);
        if (list.Count > MaxSize) list.RemoveRange(MaxSize, list.Count - MaxSize);
        SettingsStore.Save(Key, list);
    }

    public static void Remove(string filePath)
    {
        var list = Load();
        list.RemoveAll(p => p.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        SettingsStore.Save(Key, list);
    }
}
