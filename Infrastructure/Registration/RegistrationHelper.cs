// RegistrationHelper.cs
// Thin layer on top of AppRegistration that handles:
//   • First-run detection (registers automatically on first launch)
//   • Exe-path change detection (re-registers if app was moved)
//   • Async wrappers so UI never blocks on registry I/O

using QuickView.Infrastructure.Logging;
using QuickView.Infrastructure.Storage;
using System;
using System.Threading.Tasks;

namespace QuickView.Infrastructure.Registration;

public static class RegistrationHelper
{
    // Persisted setting key
    private const string SettingKey      = "registration";
    private const string SubKeyRegistered = "is_registered";
    private const string SubKeyExePath    = "registered_exe";

    // ── Public state ─────────────────────────────────────────────────────────

    /// <summary>True when QuickView is currently registered as an "Open With" handler.</summary>
    public static bool IsRegistered => LoadBool(SubKeyRegistered);

    // ── Called once from App.OnLaunched ──────────────────────────────────────

    /// <summary>
    /// On first launch: registers automatically so QuickView immediately
    /// appears in "Open With" without any user action.
    /// On subsequent launches: re-registers if the exe was moved/updated.
    /// Silent — never shows UI, never throws.
    /// </summary>
    public static Task EnsureRegisteredAsync() => Task.Run(() =>
    {
        try
        {
            string exePath = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(exePath)) return;

            string? lastExe   = LoadString(SubKeyExePath);
            bool    wasReg    = LoadBool(SubKeyRegistered);

            // Register if: never registered, or exe path changed (app updated/moved)
            bool pathChanged = !string.Equals(lastExe, exePath,
                                   StringComparison.OrdinalIgnoreCase);

            if (!wasReg || pathChanged)
            {
                Logger.Log($"[Registration] Registering (firstRun={!wasReg}, pathChanged={pathChanged})");
                AppRegistration.Register(exePath);
                SaveBool(SubKeyRegistered, true);
                SaveString(SubKeyExePath, exePath);
                Logger.Log("[Registration] Done");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[Registration] EnsureRegistered failed: {ex.Message}");
        }
    });

    // ── Called from Settings page ─────────────────────────────────────────────

    /// <summary>Register now and persist the state. Returns true on success.</summary>
    public static Task<bool> RegisterAsync() => Task.Run(() =>
    {
        try
        {
            string exePath = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(exePath)) return false;

            AppRegistration.Register(exePath);
            SaveBool(SubKeyRegistered, true);
            SaveString(SubKeyExePath, exePath);
            Logger.Log("[Registration] Registered by user");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Registration] Register failed: {ex.Message}");
            return false;
        }
    });

    /// <summary>Unregister and persist the state. Returns true on success.</summary>
    public static Task<bool> UnregisterAsync() => Task.Run(() =>
    {
        try
        {
            AppRegistration.Unregister();
            SaveBool(SubKeyRegistered, false);
            Logger.Log("[Registration] Unregistered by user");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Registration] Unregister failed: {ex.Message}");
            return false;
        }
    });

    // ── Simple persistence via SettingsStore ──────────────────────────────────

    private static bool   LoadBool(string key)   => SettingsStore.Load<bool>(  $"{SettingKey}_{key}");
    private static string? LoadString(string key) => SettingsStore.Load<string>($"{SettingKey}_{key}");
    private static void   SaveBool(string key, bool v)    => SettingsStore.Save($"{SettingKey}_{key}", v);
    private static void   SaveString(string key, string v) => SettingsStore.Save($"{SettingKey}_{key}", v);
}
