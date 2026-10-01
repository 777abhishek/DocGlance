using System;
using System.IO;
using System.Text.Json;

namespace QuickView.Infrastructure.Storage;

/// <summary>
/// Persists arbitrary settings as JSON in LocalApplicationData.
/// Writes are atomic (write-to-temp → rename) so a crash mid-write
/// never leaves a corrupt file.
/// </summary>
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
            var json     = JsonSerializer.Serialize(value);
            var filePath = BuildPath(key);
            var tmpPath  = filePath + ".tmp";

            // Write to a temp file first, then rename — atomic on NTFS.
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, filePath, overwrite: true);
        }
        catch { }
    }

    public static T? Load<T>(string key)
    {
        try
        {
            var path = BuildPath(key);
            if (!File.Exists(path)) return default;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        }
        catch { return default; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a safe file path from <paramref name="key"/>.
    /// Strips any path-traversal characters so a caller cannot write outside
    /// the settings directory (e.g. key = "../../evil" is neutralised).
    /// </summary>
    private static string BuildPath(string key)
    {
        // Keep only alphanumeric chars, hyphens, and underscores.
        // This prevents path traversal and makes keys predictable.
        var safe = System.Text.RegularExpressions.Regex.Replace(
            key, @"[^a-zA-Z0-9_\-]", "_");

        return Path.Combine(Dir, safe + ".json");
    }
}
