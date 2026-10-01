// RegistrationHelper.cs
// Thin layer on top of AppRegistration that handles first-run detection and
// async wrappers so the UI never blocks on registry I/O.
//
// PACKAGED (Store / MSIX):
//   File associations are installed/removed with the MSIX package automatically.
//   All public members return success / already-registered so the Settings page
//   compiles and behaves sensibly without any registry I/O.
//
// UNPACKAGED (direct EXE / GitHub release):
//   Original behaviour — registers on first launch, re-registers if the exe moves.

using QuickView.Infrastructure.Logging;
using System.Threading.Tasks;

#if !PACKAGED
using QuickView.Infrastructure.Storage;
using System;
#endif

namespace QuickView.Infrastructure.Registration;

public static class RegistrationHelper
{
#if PACKAGED

    // ── Packaged: associations are always "active" via the manifest ───────────

    /// <summary>
    /// In a packaged app, file associations are part of the MSIX package and are
    /// always present after installation.  Returns true.
    /// </summary>
    public static bool IsRegistered => true;

    /// <summary>No-op in packaged builds — the manifest handles registration.</summary>
    public static Task EnsureRegisteredAsync() => Task.CompletedTask;

    /// <summary>No-op in packaged builds.  Returns true (already registered).</summary>
    public static Task<bool> RegisterAsync()
    {
        Logger.Log("[Registration] Packaged build — registration managed by manifest.");
        return Task.FromResult(true);
    }

    /// <summary>
    /// No-op in packaged builds — associations are removed when the package is uninstalled.
    /// Returns true to indicate the operation "succeeded" (nothing to do).
    /// </summary>
    public static Task<bool> UnregisterAsync()
    {
        Logger.Log("[Registration] Packaged build — unregistration managed by package uninstall.");
        return Task.FromResult(true);
    }

#else

    // ── Unpackaged: original HKCU registration logic ─────────────────────────

    private const string SettingKey       = "registration";
    private const string SubKeyRegistered = "is_registered";
    private const string SubKeyExePath    = "registered_exe";

    /// <summary>True when QuickView is currently registered as an "Open With" handler.</summary>
    public static bool IsRegistered => LoadBool(SubKeyRegistered);

    /// <summary>
    /// On first launch: registers automatically.
    /// On subsequent launches: re-registers if the exe was moved/updated.
    /// Silent — never shows UI, never throws.
    /// </summary>
    public static Task EnsureRegisteredAsync() => Task.Run(() =>
    {
        try
        {
            string exePath = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(exePath)) return;

            string? lastExe  = LoadString(SubKeyExePath);
            bool    wasReg   = LoadBool(SubKeyRegistered);
            bool    pathChanged = !string.Equals(lastExe, exePath,
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

    // ── Persistence helpers ───────────────────────────────────────────────────

    private static bool    LoadBool(string key)   => SettingsStore.Load<bool>(  $"{SettingKey}_{key}");
    private static string? LoadString(string key) => SettingsStore.Load<string>($"{SettingKey}_{key}");
    private static void    SaveBool(string key, bool v)    => SettingsStore.Save($"{SettingKey}_{key}", v);
    private static void    SaveString(string key, string v) => SettingsStore.Save($"{SettingKey}_{key}", v);

#endif
}
