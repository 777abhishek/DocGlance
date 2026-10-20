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
using System.Linq;
using System.Runtime.InteropServices;
#endif

namespace DocGlance.Services.Registration;

public static class AppRegistration
{
    // ── Supported extensions (used by both builds for the Settings UI list) ──

    public static readonly IReadOnlyList<string> SupportedExtensions =
        DocGlance.Core.Constants.SupportedExtensions.All;

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

    private const string LegacyProgId = "DocGlance.AssocFile.1";
    private const string AppExeKey = "DocGlance.exe";
    private const string AppName  = "DocGlance";
    private const string AppDesc  = "Fast universal file viewer for Windows";
    private const string CapabKey = @"Software\Clients\DocGlance\Capabilities";

    private static string GetIconName(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => "PDF",
        ".bmp" or ".dds" or ".gif" or ".hdp" or ".ico" or ".jpeg" or ".jpg" or ".jxr" or
        ".png" or ".svg" or ".svgz" or ".tif" or ".tiff" or ".wdp" => "Image",
        ".pptx" or ".ppt" or ".pptm" or ".ppsx" => "PowerPoint",
        ".docx" or ".doc" => "Word",
        ".xlsx" or ".xls" => "Excel",
        ".csv" or ".tsv" => "CSV",
        ".log" => "LOG",
        ".md" or ".markdown" or ".mdx" or ".mmd" => "Markdown",
        ".rtf" => "RichText",
        ".json" => "JSON",
        ".xml" or ".xaml" => "XML",
        ".cs" or ".vb" or ".fs" or ".js" or ".mjs" or ".ts" or ".tsx" or ".jsx" or
        ".py" or ".java" or ".kt" or ".cpp" or ".c" or ".h" or ".go" or ".rs" or
        ".swift" or ".rb" or ".php" or ".sh" or ".ps1" or ".bat" or ".html" or
        ".htm" or ".css" or ".scss" or ".sql" => "Code",
        _ => "Text",
    };

    private static string GetTypeDescription(string iconName) => iconName switch
    {
        "PDF" => "PDF Document",
        "Image" => "Image File",
        "PowerPoint" => "PowerPoint Presentation",
        "Word" => "Word Document",
        "Excel" => "Excel Spreadsheet",
        "CSV" => "CSV File",
        "LOG" => "Log File",
        "Markdown" => "Markdown Document",
        "RichText" => "Rich Text Document",
        "JSON" => "JSON File",
        "XML" => "XML File",
        "Code" => "Code File",
        _ => "Text Document",
    };

    private static string ProgIdFor(string extension) => $"DocGlance.{GetIconName(extension)}";

    /// <summary>
    /// Writes all HKCU registry keys needed to appear in "Open With" and be
    /// selectable as the default app.  No elevation required.
    /// </summary>
    public static void Register(string exePath)
    {
        string iconPath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(exePath)!, "Assets", "AppIcon.ico");
        string openCmd  = $"\"{exePath}\" \"%1\"";

        // 1 ── Per-icon ProgIDs: Windows gets a distinct DefaultIcon for each file type.
        foreach (var iconName in SupportedExtensions.Select(GetIconName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string progId = $"DocGlance.{iconName}";
            string description = GetTypeDescription(iconName);
            string typeIconPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(exePath)!, "Assets",
                iconName == "Image" ? "AppIcon.ico" : System.IO.Path.Combine("FileTypeIcons", $"{iconName}.ico"));

            using (var type = EnsureKey($@"Software\Classes\{progId}"))
            {
                type.SetValue("", description);
                type.SetValue("FriendlyTypeName", description);
                type.SetValue("EditFlags", 0x00010000, RegistryValueKind.DWord);
            }
            using (var icon = EnsureKey($@"Software\Classes\{progId}\DefaultIcon"))
                icon.SetValue("", $"\"{typeIconPath}\",0", RegistryValueKind.ExpandString);
            using (var cmd = EnsureKey($@"Software\Classes\{progId}\shell\open\command"))
                cmd.SetValue("", openCmd);
        }

        // 2 ── Map every supported extension to its own icon-bearing ProgID.
        var currentProgIds = SupportedExtensions
            .Select(ProgIdFor)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var ext in SupportedExtensions)
        {
            using var openWith = EnsureKey($@"Software\Classes\{ext}\OpenWithProgids");
            string intendedProgId = ProgIdFor(ext);
            foreach (var obsoleteProgId in currentProgIds.Append(LegacyProgId))
            {
                if (!string.Equals(obsoleteProgId, intendedProgId, StringComparison.OrdinalIgnoreCase))
                    openWith.DeleteValue(obsoleteProgId, throwOnMissingValue: false);
            }
            openWith.SetValue(intendedProgId, new byte[0], RegistryValueKind.None);
        }

        // 3 ── Applications\DocGlance.exe entry
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
                fa.SetValue(ext, ProgIdFor(ext));

        // 6 ── RegisteredApplications
        using (var regApps = Registry.CurrentUser.OpenSubKey(
                   @"Software\RegisteredApplications", writable: true)
               ?? Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            regApps.SetValue(AppName, CapabKey);

        NotifyShell();
    }

    public static void Unregister()
    {
        foreach (var ext in SupportedExtensions)
        {
            try
            {
                using var openWith = Registry.CurrentUser.OpenSubKey(
                    $@"Software\Classes\{ext}\OpenWithProgids", writable: true);
                openWith?.DeleteValue(ProgIdFor(ext), throwOnMissingValue: false);
                openWith?.DeleteValue(LegacyProgId, throwOnMissingValue: false);
            }
            catch { }
        }

        foreach (var iconName in SupportedExtensions.Select(GetIconName).Distinct(StringComparer.OrdinalIgnoreCase))
            DeleteKeyTree($@"Software\Classes\DocGlance.{iconName}");
        DeleteKeyTree($@"Software\Classes\{LegacyProgId}");
        DeleteKeyTree($@"Software\Classes\Applications\{AppExeKey}");
        DeleteKeyTree($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{AppExeKey}");
        DeleteKeyTree(@"Software\Clients\DocGlance");
        using var regApps = Registry.CurrentUser.OpenSubKey(
            @"Software\RegisteredApplications", writable: true);
        regApps?.DeleteValue(AppName, throwOnMissingValue: false);
        NotifyShell();
    }

    public static bool IsRegistered(string exePath)
    {
        try
        {
            foreach (var ext in SupportedExtensions)
            {
                string progId = ProgIdFor(ext);
                string iconName = GetIconName(ext);
                string expectedIcon = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(exePath)!, "Assets", "FileTypeIcons", $"{iconName}.ico");

                using var command = Registry.CurrentUser.OpenSubKey(
                    $@"Software\Classes\{progId}\shell\open\command");
                using var icon = Registry.CurrentUser.OpenSubKey(
                    $@"Software\Classes\{progId}\DefaultIcon");
                using var openWith = Registry.CurrentUser.OpenSubKey(
                    $@"Software\Classes\{ext}\OpenWithProgids");

                string commandValue = command?.GetValue("")?.ToString() ?? "";
                string iconValue = icon?.GetValue("")?.ToString() ?? "";
                bool mapped = openWith?.GetValueNames().Any(name =>
                    string.Equals(name, progId, StringComparison.OrdinalIgnoreCase)) == true;
                if (!commandValue.Contains(exePath, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(iconValue, $"\"{expectedIcon}\",0", StringComparison.OrdinalIgnoreCase) ||
                    !mapped)
                    return false;
            }

            return true;
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
