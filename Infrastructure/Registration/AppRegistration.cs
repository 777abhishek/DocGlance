// AppRegistration.cs
// Professional Windows file association registration for an unpackaged desktop app.
//
// Registry layout written (all HKCU — no admin required):
//
//  HKCU\Software\Classes\QuickView.AssocFile.1
//      (Default)          = "QuickView Document"
//      FriendlyTypeName   = "QuickView Document"
//      EditFlags          = 0x00010000   (FTA_OpenIsSafe)
//      DefaultIcon\(Default) = "C:\...\QuickView.exe,0"
//      shell\open\command\(Default) = "\"C:\...\QuickView.exe\" \"%1\""
//
//  HKCU\Software\Classes\.pdf  → QuickView.AssocFile.1   (and every other ext)
//  HKCU\Software\Classes\Applications\QuickView.exe
//      FriendlyAppName    = "QuickView"
//      DefaultIcon\(Default) = "C:\...\Assets\AppIcon.ico"
//      SupportedTypes\.<ext>  (empty REG_SZ per ext — makes us show in Open With)
//      shell\open\command\(Default) = "\"C:\...\QuickView.exe\" \"%1\""
//
//  HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\QuickView.exe
//      (Default) = full exe path
//      Path      = folder containing the exe
//
//  HKCU\Software\RegisteredApplications
//      QuickView = "Software\Clients\QuickView\Capabilities"
//
//  HKCU\Software\Clients\QuickView\Capabilities
//      ApplicationName        = "QuickView"
//      ApplicationDescription = "Fast universal file viewer"
//      FileAssociations\.<ext> = "QuickView.AssocFile.1"

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace QuickView.Infrastructure.Registration;

public static class AppRegistration
{
    // ── ProgID ────────────────────────────────────────────────────────────────
    private const string ProgId      = "QuickView.AssocFile.1";
    private const string AppExeKey   = "QuickView.exe";
    private const string AppName     = "QuickView";
    private const string AppDesc     = "Fast universal file viewer for Windows";
    private const string CapabKey    = @"Software\Clients\QuickView\Capabilities";

    // All extensions QuickView handles — file icons stay as the system default;
    // we only register the *open* verb so we appear in "Open With".
    public static readonly IReadOnlyList<string> SupportedExtensions = new[]
    {
        // Documents
        ".pdf",
        // Plain text / code
        ".txt", ".log", ".csv", ".tsv", ".ini", ".cfg", ".conf",
        ".env", ".toml", ".yaml", ".yml", ".properties",
        ".gitignore", ".editorconfig",
        // Code
        ".cs", ".vb", ".fs", ".js", ".mjs", ".ts", ".tsx", ".jsx",
        ".py", ".java", ".kt", ".cpp", ".c", ".h", ".go", ".rs",
        ".swift", ".rb", ".php", ".sh", ".ps1", ".bat",
        ".html", ".htm", ".css", ".scss", ".json", ".xml", ".xaml",
        ".sql", ".md", ".markdown",
    };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes all registry keys needed to appear in "Open With" and be
    /// selectable as the default app. Uses HKCU — no elevation required.
    /// Call SHChangeNotify after this to refresh Explorer.
    /// </summary>
    public static void Register(string exePath)
    {
        string iconPath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(exePath)!, "Assets", "AppIcon.ico");
        string openCmd  = $"\"{exePath}\" \"%1\"";

        // 1 ── ProgID under HKCU\Software\Classes
        using (var progId = EnsureKey($@"Software\Classes\{ProgId}"))
        {
            progId.SetValue("", "QuickView Document");
            progId.SetValue("FriendlyTypeName", "QuickView Document");
            progId.SetValue("EditFlags", 0x00010000, RegistryValueKind.DWord); // FTA_OpenIsSafe
        }
        using (var icon = EnsureKey($@"Software\Classes\{ProgId}\DefaultIcon"))
            icon.SetValue("", $"\"{iconPath}\"", RegistryValueKind.ExpandString);

        using (var cmd = EnsureKey($@"Software\Classes\{ProgId}\shell\open\command"))
            cmd.SetValue("", openCmd);

        // 2 ── Map each extension → ProgID
        foreach (var ext in SupportedExtensions)
        {
            using var extKey = EnsureKey($@"Software\Classes\{ext}");
            extKey.SetValue("", ProgId);
        }

        // 3 ── HKCU\Software\Classes\Applications\QuickView.exe
        //      (controls "Open With" list entry, icon, friendly name)
        using (var appKey = EnsureKey($@"Software\Classes\Applications\{AppExeKey}"))
            appKey.SetValue("FriendlyAppName", AppName);

        using (var defIcon = EnsureKey($@"Software\Classes\Applications\{AppExeKey}\DefaultIcon"))
            defIcon.SetValue("", $"\"{iconPath}\"", RegistryValueKind.ExpandString);

        using (var appCmd = EnsureKey($@"Software\Classes\Applications\{AppExeKey}\shell\open\command"))
            appCmd.SetValue("", openCmd);

        // SupportedTypes — empty REG_SZ per ext tells Windows we support them
        using (var suppTypes = EnsureKey($@"Software\Classes\Applications\{AppExeKey}\SupportedTypes"))
        {
            foreach (var ext in SupportedExtensions)
                suppTypes.SetValue(ext, "");
        }

        // 4 ── App Paths — lets ShellExecute find us by name
        using (var appPaths = EnsureKey(
            $@"Software\Microsoft\Windows\CurrentVersion\App Paths\{AppExeKey}"))
        {
            appPaths.SetValue("", exePath);
            appPaths.SetValue("Path", System.IO.Path.GetDirectoryName(exePath)!);
        }

        // 5 ── Capabilities block (used by Default Programs in Windows Settings)
        using (var cap = EnsureKey(CapabKey))
        {
            cap.SetValue("ApplicationName", AppName);
            cap.SetValue("ApplicationDescription", AppDesc);
            cap.SetValue("ApplicationIcon", $"\"{iconPath}\",0");
        }
        using (var fa = EnsureKey($@"{CapabKey}\FileAssociations"))
        {
            foreach (var ext in SupportedExtensions)
                fa.SetValue(ext, ProgId);
        }

        // 6 ── RegisteredApplications — makes us show up in "Default Apps" list
        using (var regApps = Registry.CurrentUser.OpenSubKey(
            @"Software\RegisteredApplications", writable: true)
            ?? Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
        {
            regApps.SetValue(AppName, CapabKey);
        }

        // 7 ── Notify the shell so icons/associations refresh immediately
        NotifyShell();
    }

    /// <summary>
    /// Removes all keys written by Register(). Leaves the extension → ProgID
    /// mappings in place per Microsoft best-practice (another app may own them).
    /// </summary>
    public static void Unregister()
    {
        // Remove ProgID
        DeleteKeyTree($@"Software\Classes\{ProgId}");

        // Remove Applications entry
        DeleteKeyTree($@"Software\Classes\Applications\{AppExeKey}");

        // Remove App Paths
        DeleteKeyTree(
            $@"Software\Microsoft\Windows\CurrentVersion\App Paths\{AppExeKey}");

        // Remove Capabilities
        DeleteKeyTree(@"Software\Clients\QuickView");

        // Remove RegisteredApplications entry
        using var regApps = Registry.CurrentUser.OpenSubKey(
            @"Software\RegisteredApplications", writable: true);
        regApps?.DeleteValue(AppName, throwOnMissingValue: false);

        NotifyShell();
    }

    /// <summary>Returns true if the registration is current (ProgID and exe path match).</summary>
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
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Tells Explorer to re-read file associations and refresh all icons.
    /// Equivalent to what every professional installer does after writing associations.
    /// </summary>
    private static void NotifyShell()
    {
        try
        {
            // SHCNE_ASSOCCHANGED = 0x08000000, SHCNF_IDLIST = 0x0000
            SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
        }
        catch { /* not critical */ }
    }

    [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags,
        IntPtr dwItem1, IntPtr dwItem2);
}
