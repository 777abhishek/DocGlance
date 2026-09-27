using System;

namespace QuickView.Core.Helpers;

public static class DateHelper
{
    public static string FormatRelative(DateTime date)
    {
        var diff = DateTime.Now - date;
        if (diff.TotalSeconds < 0)            return "Just now";
        if (date.Date == DateTime.Today)      return "Today";
        if (date.Date == DateTime.Today.AddDays(-1)) return "Yesterday";
        if (diff.TotalDays < 7)
        {
            int d = Math.Max(1, (int)diff.TotalDays);
            return $"{d} day{(d == 1 ? "" : "s")} ago";
        }
        return date.Year == DateTime.Now.Year
            ? date.ToString("MMM dd")
            : date.ToString("MMM dd, yyyy");
    }
}
