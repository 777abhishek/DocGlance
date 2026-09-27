using System;
using System.Text.Json;
using System.IO;

namespace QuickView.Infrastructure.Storage;

/// <summary>Persists arbitrary settings as JSON in LocalApplicationData.</summary>
public static class SettingsStore
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuickView", "settings");

    static SettingsStore()
    {
        try { Directory.CreateDirectory(Dir); } catch { }
    }

    public static void Save<T>(string key, T value)
    {
        try
        {
            var json = JsonSerializer.Serialize(value);
            File.WriteAllText(Path.Combine(Dir, key + ".json"), json);
        }
        catch { }
    }

    public static T? Load<T>(string key)
    {
        try
        {
            var path = Path.Combine(Dir, key + ".json");
            if (!File.Exists(path)) return default;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        }
        catch { return default; }
    }
}
