using System.Collections.Generic;

namespace QuickView.Infrastructure.Storage;

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
        list.Remove(filePath);
        list.Insert(0, filePath);
        if (list.Count > MaxSize) list.RemoveRange(MaxSize, list.Count - MaxSize);
        SettingsStore.Save(Key, list);
    }

    public static void Remove(string filePath)
    {
        var list = Load();
        list.Remove(filePath);
        SettingsStore.Save(Key, list);
    }
}
