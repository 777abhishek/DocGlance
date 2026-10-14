namespace DocGlance.Common.Formatters;

/// <summary>
/// Formats file sizes in human-readable units (B, KB, MB, GB).
/// Replaces the legacy FileSizeHelper for clearer naming and purpose.
/// </summary>
public static class FileSizeFormatter
{
    /// <summary>
    /// Formats bytes as human-readable string with appropriate unit.
    /// Examples: "1.5 MB", "450 KB", "12.3 GB"
    /// </summary>
    public static string Format(long bytes)
    {
        if (bytes < 0)
            return "0 B";

        if (bytes < 1024L)
            return $"{bytes} B";
        
        if (bytes < 1024L * 1024)
            return $"{bytes / 1024.0:F1} KB";
        
        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        
        if (bytes < 1024L * 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        
        return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F1} TB";
    }

    /// <summary>
    /// Formats bytes with exactly specified precision.
    /// </summary>
    public static string FormatWithPrecision(long bytes, int decimalPlaces = 2)
    {
        if (bytes < 0)
            return "0 B";

        string format = $"F{decimalPlaces}";

        if (bytes < 1024L)
            return $"{bytes} B";
        
        if (bytes < 1024L * 1024)
            return $"{(bytes / 1024.0).ToString(format)} KB";
        
        if (bytes < 1024L * 1024 * 1024)
            return $"{(bytes / (1024.0 * 1024)).ToString(format)} MB";
        
        if (bytes < 1024L * 1024 * 1024 * 1024)
            return $"{(bytes / (1024.0 * 1024 * 1024)).ToString(format)} GB";
        
        return $"{(bytes / (1024.0 * 1024 * 1024 * 1024)).ToString(format)} TB";
    }

    /// <summary>
    /// Parses a formatted size string back to bytes.
    /// Examples: "1.5 MB" → 1572864, "450 KB" → 460800
    /// </summary>
    public static long? Parse(string formattedSize)
    {
        if (string.IsNullOrWhiteSpace(formattedSize))
            return null;

        var parts = formattedSize.Trim().Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return null;

        if (!double.TryParse(parts[0], out double value))
            return null;

        long multiplier = parts[1].ToUpperInvariant() switch
        {
            "B" => 1L,
            "KB" => 1024L,
            "MB" => 1024L * 1024,
            "GB" => 1024L * 1024 * 1024,
            "TB" => 1024L * 1024 * 1024 * 1024,
            _ => 0
        };

        return multiplier > 0 ? (long)(value * multiplier) : null;
    }
}
