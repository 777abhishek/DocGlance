using System;
using System.Globalization;

namespace DocGlance.Common.Formatters;

/// <summary>
/// Formats dates for display in user-friendly formats.
/// Replaces the legacy DateHelper for clearer naming and purpose.
/// </summary>
public static class DateFormatter
{
    /// <summary>
    /// Formats a date as a human-readable relative string.
    /// IMPORTANT: <paramref name="date"/> MUST be in LOCAL time (not UTC).
    /// Pass DateTime.Now or FileInfo.LastWriteTime (local), NOT FileInfo.LastWriteTimeUtc.
    /// </summary>
    /// <param name="date">The date in LOCAL time to format</param>
    /// <returns>Relative time string like "Today", "2 days ago", "Jan 15"</returns>
    public static string FormatRelative(DateTime date)
    {
        // Defensive: convert UTC to local if needed
        if (date.Kind == DateTimeKind.Utc)
            date = date.ToLocalTime();

        var now = DateTime.Now;
        var diff = now - date;
        
        // Future dates (clock skew or files modified "in the future")
        if (diff.TotalSeconds < 0)
            return "Just now";
        
        // Today
        if (date.Date == now.Date)
        {
            if (diff.TotalMinutes < 1)
                return "Just now";
            if (diff.TotalHours < 1)
                return $"{(int)diff.TotalMinutes} min ago";
            return date.ToString("h:mm tt", CultureInfo.InvariantCulture);
        }
        
        // Yesterday
        if (date.Date == now.Date.AddDays(-1))
            return "Yesterday";
        
        // Within last week
        if (diff.TotalDays < 7)
        {
            int days = Math.Max(1, (int)diff.TotalDays);
            return $"{days} day{(days == 1 ? "" : "s")} ago";
        }
        
        // Current year: show month and day
        if (date.Year == now.Year)
            return date.ToString("MMM dd", CultureInfo.InvariantCulture);
        
        // Different year: show full date
        return date.ToString("MMM dd, yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats date with time for detailed views.
    /// Example: "Jan 15, 2026 at 3:45 PM"
    /// </summary>
    public static string FormatDetailed(DateTime date)
    {
        if (date.Kind == DateTimeKind.Utc)
            date = date.ToLocalTime();

        return date.ToString("MMM dd, yyyy 'at' h:mm tt", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats date in short international format.
    /// Example: "2026-01-15"
    /// </summary>
    public static string FormatShort(DateTime date)
    {
        if (date.Kind == DateTimeKind.Utc)
            date = date.ToLocalTime();

        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats date range as human-readable string.
    /// Examples: "Jan 15-20", "Jan 15 - Feb 3", "Dec 28, 2025 - Jan 5, 2026"
    /// </summary>
    public static string FormatRange(DateTime start, DateTime end)
    {
        if (start.Kind == DateTimeKind.Utc)
            start = start.ToLocalTime();
        if (end.Kind == DateTimeKind.Utc)
            end = end.ToLocalTime();

        // Same year and month
        if (start.Year == end.Year && start.Month == end.Month)
            return $"{start.ToString("MMM dd", CultureInfo.InvariantCulture)}-{end.ToString("dd", CultureInfo.InvariantCulture)}";

        // Same year, different months
        if (start.Year == end.Year)
            return $"{start.ToString("MMM dd", CultureInfo.InvariantCulture)} - {end.ToString("MMM dd", CultureInfo.InvariantCulture)}";

        // Different years
        return $"{start.ToString("MMM dd, yyyy", CultureInfo.InvariantCulture)} - {end.ToString("MMM dd, yyyy", CultureInfo.InvariantCulture)}";
    }
}
