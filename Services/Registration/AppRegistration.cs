// AppRegistration.cs
// File association management — behaviour differs between packaged and unpackaged builds.
//
// PACKAGED (Store / MSIX):
//   File associations are declared in Package.appxmanifest and installed/removed
//   by Windows automatically with the package.  Manual registry writes for file
//   associations are virtualised and ignored in packaged apps, so the old HKCU
//   approach is simply not used.  This class becomes a thin no-op shim so the
//   rest of the codebase compiles unchanged.
//
// UNPACKAGED (direct EXE / GitHub release):
//   The original HKCU registry approach is retained in full so the direct-download
//   build continues to work without modification.

using System.Collections.Generic;

#if !PACKAGED
using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;
#endif

namespace QuickView.Services.Registration;

public static class AppRegistration
{
    // ── Supported extensions (used by both builds for the Settings UI list) ──

    public static readonly IReadOnlyList<string> SupportedExtensions =
        QuickView.Core.Constants.SupportedExtensions.All;

#if PACKAGED

    // ── Packaged build: associations are handled by Package.appxmanifest ─────
    // These stubs allow RegistrationHelper and SettingsPage to compile as-is.

    /// <summary>
    /// In a packaged app, file associations are declared in Package.appxmanifest
    /// and managed entirely by Windows.  This method is a no-op.
    /// </summary>
    public static void Register(string exePath) { /* handled by manifest */ }

    /// <summary>
    /// In a packaged app, uninstalling the MSIX package removes all associations.
    /// This method is a no-op.
    /// </summary>
    public static void Unregister() { /* handled by package uninstall */ }

    /// <summary>
    /// In a packaged app the associations are always "registered" (they come with
    /// the package install).  Returns true so the Settings UI shows the correct state.
    /// </summary>
    public static bool IsRegistered(string exePath) => true;

#else

    // ── Unpackaged build: write HKCU registry keys (original implementation) ─

    private const string ProgId   = "QuickView.AssocFile.1";
    private const string AppExeKey = "QuickView.exe";
    private const string AppName  = "QuickView";
    private const string AppDesc  = "Fast universal file viewer for Windows";
    private const string CapabKey = @"Software\Clients\QuickView\Capabilities";

    /// <summary>
    /// Writes all HKCU registry keys needed to appear in "Open With" and be
    /// selectable as the default app.  No elevation required.
    /// </summary>
    public static void Register(string exePath)
    {
        string iconPath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(exePath)!, "Assets", "AppIcon.ico");
        string openCmd  = $"\"{exePath}\" \"%1\"";

        // 1 ── ProgID
        using (var progId = EnsureKey($@"Software\Classes\{ProgId}"))
        {
            progId.SetValue("", "QuickView Document");
            progId.SetValue("FriendlyTypeName", "QuickView Document");
            progId.SetValue("EditFlags", 0x00010000, RegistryValueKind.DWord);
        }
        using (var cmd = EnsureKey($@"Software\Classes\{ProgId}\shell\open\command"))
            cmd.SetValue("", openCmd);

        // 2 ── OpenWithProgids per extension (preserves native file icons)
        foreach (var ext in SupportedExtensions)
        {
            using var openWith = EnsureKey($@"Software\Classes\{ext}\OpenWithProgids");
            openWith.SetValue(ProgId, new byte[0], RegistryValueKind.None);
        }

        // 3 ── Applications\QuickView.exe entry
        using (var appKey = EnsureKey($@"Software\Classes\Applications\{AppExeKey}"))
            appKey.SetValue("FriendlyAppName", AppName);
        using (var defIcon = EnsureKey($@"Software\Classes\Applications\{AppExeKey}\DefaultIcon"))
            defIcon.SetValue("", $"\"{iconPath}\"", RegistryValueKind.ExpandString);
        using (var appCmd = EnsureKey($@"Software\Classes\Applications\{AppExeKey}\shell\open\command"))
            appCmd.SetValue("", openCmd);
        using (var suppTypes = EnsureKey($@"Software\Classes\Applications\{AppExeKey}\SupportedTypes"))
            foreach (var ext in SupportedExtensions)
                suppTypes.SetValue(ext, "");

        // 4 ── App Paths
        using (var appPaths = EnsureKey(
            $@"Software\Microsoft\Windows\CurrentVersion\App Paths\{AppExeKey}"))
        {
            appPaths.SetValue("", exePath);
            appPaths.SetValue("Path", System.IO.Path.GetDirectoryName(exePath)!);
        }

        // 5 ── Capabilities block
        using (var cap = EnsureKey(CapabKey))
        {
            cap.SetValue("ApplicationName", AppName);
            cap.SetValue("ApplicationDescription", AppDesc);
            cap.SetValue("ApplicationIcon", $"\"{iconPath}\",0");
        }
        using (var fa = EnsureKey($@"{CapabKey}\FileAssociations"))
            foreach (var ext in SupportedExtensions)
                fa.SetValue(ext, ProgId);

        // 6 ── RegisteredApplications
        using (var regApps = Registry.CurrentUser.OpenSubKey(
                   @"Software\RegisteredApplications", writable: true)
               ?? Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            regApps.SetValue(AppName, CapabKey);

        NotifyShell();
    }

    public static void Unregister()
    {
        DeleteKeyTree($@"Software\Classes\{ProgId}");
        DeleteKeyTree($@"Software\Classes\Applications\{AppExeKey}");
        DeleteKeyTree($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{AppExeKey}");
        DeleteKeyTree(@"Software\Clients\QuickView");
        using var regApps = Registry.CurrentUser.OpenSubKey(
            @"Software\RegisteredApplications", writable: true);
        regApps?.DeleteValue(AppName, throwOnMissingValue: false);
        NotifyShell();
    }

    public static bool IsRegistered(string exePath)
    {
        try
        {
            using var cmd = Registry.CurrentUser.OpenSubKey(
                $@"Software\Classes\{ProgId}\shell\open\command");
            if (cmd == null) return false;
            var val = cmd.GetValue("")?.ToString() ?? "";
            return val.Contains(exePath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static RegistryKey EnsureKey(string path) =>
        Registry.CurrentUser.CreateSubKey(path, writable: true);

    private static void DeleteKeyTree(string path)
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
        catch { }
    }

    private static void NotifyShell()
    {
        try { SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero); }
        catch { }
    }

    [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags,
        IntPtr dwItem1, IntPtr dwItem2);

#endif
}
