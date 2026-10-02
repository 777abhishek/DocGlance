using QuickView.Core.Extensions;

namespace QuickView.Tests.Shared.Formatters;

public class DateFormatterTests
{
    // ── FormatRelative ────────────────────────────────────────────────────────

    [Fact]
    public void FormatRelative_JustNow()
        => DateFormatter.FormatRelative(DateTime.Now.AddSeconds(-10))
            .Should().Be("Just now");

    [Fact]
    public void FormatRelative_MinutesAgo()
        => DateFormatter.FormatRelative(DateTime.Now.AddMinutes(-30))
            .Should().Be("30 min ago");

    [Fact]
    public void FormatRelative_Yesterday()
        => DateFormatter.FormatRelative(DateTime.Now.Date.AddDays(-1))
            .Should().Be("Yesterday");

    [Fact]
    public void FormatRelative_DaysAgo()
    {
        var result = DateFormatter.FormatRelative(DateTime.Now.AddDays(-3));
        result.Should().Be("3 days ago");
    }

    [Fact]
    public void FormatRelative_FutureDate_ReturnsJustNow()
        => DateFormatter.FormatRelative(DateTime.Now.AddMinutes(5))
            .Should().Be("Just now");

    [Fact]
    public void FormatRelative_OlderThanWeek_SameYear_ShowsMonthDay()
    {
        var date = new DateTime(DateTime.Now.Year, 1, 15);
        // Only run this test if January 15 is at least 8 days in the past
        if ((DateTime.Now - date).TotalDays > 8)
            DateFormatter.FormatRelative(date).Should().Be("Jan 15");
    }

    [Fact]
    public void FormatRelative_DifferentYear_ShowsFullDate()
    {
        var date = new DateTime(2020, 6, 1);
        DateFormatter.FormatRelative(date).Should().Be("Jun 01, 2020");
    }

    [Fact]
    public void FormatRelative_UtcDate_ConvertsToLocal()
    {
        var utcDate = DateTime.UtcNow.AddSeconds(-5);
        DateFormatter.FormatRelative(utcDate).Should().Be("Just now");
    }

    // ── FormatDetailed ────────────────────────────────────────────────────────

    [Fact]
    public void FormatDetailed_ReturnsExpectedFormat()
    {
        var date = new DateTime(2026, 3, 15, 14, 30, 0);
        DateFormatter.FormatDetailed(date).Should().Be("Mar 15, 2026 at 2:30 PM");
    }

    // ── FormatShort ───────────────────────────────────────────────────────────

    [Fact]
    public void FormatShort_ReturnsIso8601()
    {
        var date = new DateTime(2026, 3, 5);
        DateFormatter.FormatShort(date).Should().Be("2026-03-05");
    }

    // ── FormatRange ───────────────────────────────────────────────────────────

    [Fact]
    public void FormatRange_SameMonthSameYear()
    {
        var start = new DateTime(2026, 1, 15);
        var end   = new DateTime(2026, 1, 20);
        DateFormatter.FormatRange(start, end).Should().Be("Jan 15-20");
    }

    [Fact]
    public void FormatRange_DifferentMonthSameYear()
    {
        var start = new DateTime(2026, 1, 15);
        var end   = new DateTime(2026, 2, 3);
        DateFormatter.FormatRange(start, end).Should().Be("Jan 15 - Feb 03");
    }

    [Fact]
    public void FormatRange_DifferentYears()
    {
        var start = new DateTime(2025, 12, 28);
        var end   = new DateTime(2026, 1, 5);
        DateFormatter.FormatRange(start, end).Should().Be("Dec 28, 2025 - Jan 05, 2026");
    }
}
