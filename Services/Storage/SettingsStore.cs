using QuickView.Infrastructure.Logging;
using System;
using System.IO;
using System.Text.Json;

namespace QuickView.Services.Storage;

/// <summary>
/// Persists arbitrary settings as JSON in LocalApplicationData.
/// Writes are atomic (write-to-temp → rename) so a crash mid-write
/// never leaves a corrupt file, and serialised so concurrent callers
/// cannot trample each other's temp file.
/// </summary>
public static class SettingsStore
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuickView", "settings");

    private static readonly object Gate = new();

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

            lock (Gate)
            {
                // Unique temp name per write; rename over the target is atomic on NTFS.
                var tmpPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
                try
                {
                    File.WriteAllText(tmpPath, json);
                    File.Move(tmpPath, filePath, overwrite: true);
                }
                catch
                {
                    try { File.Delete(tmpPath); } catch { }
                    throw;
                }
            }
        }
        catch (Exception ex)
        {
            // Settings are best-effort, but a failed save must be diagnosable.
            Logger.Warn($"[Settings] Save '{key}' failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static T? Load<T>(string key)
    {
        string? path = null;
        try
        {
            path = BuildPath(key);
            lock (Gate)
            {
                if (!File.Exists(path)) return default;
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
            }
        }
        catch (JsonException ex)
        {
            // Keep the unreadable file for recovery instead of letting the next Save overwrite it.
            Logger.Warn($"[Settings] '{key}' is corrupt, using defaults: {ex.Message}");
            QuarantineCorruptFile(path);
            return default;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Settings] Load '{key}' failed: {ex.GetType().Name}: {ex.Message}");
            return default;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void QuarantineCorruptFile(string? path)
    {
        if (path == null) return;
        try
        {
            lock (Gate)
                File.Move(path, path + ".corrupt", overwrite: true);
        }
        catch { }
    }

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
