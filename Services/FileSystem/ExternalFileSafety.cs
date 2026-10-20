using System;
using System.Collections.Generic;
using System.IO;

namespace DocGlance.Services.FileSystem;

internal static class ExternalFileSafety
{
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".appref-ms", ".bat", ".cmd", ".com", ".cpl", ".dll", ".hta", ".inf",
        ".jar", ".js", ".jse", ".lnk", ".msi", ".msp", ".ps1", ".ps1xml",
        ".ps2", ".ps2xml", ".psc1", ".psd1", ".psm1", ".reg", ".scr",
        ".scf", ".url", ".vbe", ".vbs", ".wsf", ".wsh", ".exe",
    };

    internal static bool IsAllowed(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return !BlockedExtensions.Contains(Path.GetExtension(filePath));
    }
}
