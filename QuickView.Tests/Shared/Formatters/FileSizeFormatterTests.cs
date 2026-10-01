using QuickView.Shared.Formatters;

namespace QuickView.Tests.Shared.Formatters;

public class FileSizeFormatterTests
{
    // ── Format ────────────────────────────────────────────────────────────────

    [Fact]
    public void Format_Zero_ReturnsZeroBytes()
        => FileSizeFormatter.Format(0).Should().Be("0 B");

    [Fact]
    public void Format_Negative_ReturnsZeroBytes()
        => FileSizeFormatter.Format(-1).Should().Be("0 B");

    [Fact]
    public void Format_Bytes_NoUnit()
        => FileSizeFormatter.Format(512).Should().Be("512 B");

    [Fact]
    public void Format_Kilobytes()
        => FileSizeFormatter.Format(1024).Should().Be("1.0 KB");

    [Fact]
    public void Format_Megabytes()
        => FileSizeFormatter.Format(1024 * 1024).Should().Be("1.0 MB");

    [Fact]
    public void Format_Gigabytes()
        => FileSizeFormatter.Format(1024L * 1024 * 1024).Should().Be("1.0 GB");

    [Fact]
    public void Format_Terabytes()
        => FileSizeFormatter.Format(1024L * 1024 * 1024 * 1024).Should().Be("1.0 TB");

    [Theory]
    [InlineData(1_500_000, "1.4 MB")]
    [InlineData(2_500_000_000L, "2.3 GB")]
    public void Format_FractionalValues(long bytes, string expected)
        => FileSizeFormatter.Format(bytes).Should().Be(expected);

    // ── FormatWithPrecision ───────────────────────────────────────────────────

    [Fact]
    public void FormatWithPrecision_TwoDecimalPlaces()
        => FileSizeFormatter.FormatWithPrecision(1536, 2).Should().Be("1.50 KB");

    [Fact]
    public void FormatWithPrecision_ZeroDecimalPlaces()
        => FileSizeFormatter.FormatWithPrecision(1024 * 1024, 0).Should().Be("1 MB");

    // ── Parse ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Null_ReturnsNull()
        => FileSizeFormatter.Parse(null!).Should().BeNull();

    [Fact]
    public void Parse_Empty_ReturnsNull()
        => FileSizeFormatter.Parse("").Should().BeNull();

    [Fact]
    public void Parse_KB()
        => FileSizeFormatter.Parse("1.0 KB").Should().Be(1024);

    [Fact]
    public void Parse_MB()
        => FileSizeFormatter.Parse("1.0 MB").Should().Be(1024 * 1024);

    [Fact]
    public void Parse_InvalidFormat_ReturnsNull()
        => FileSizeFormatter.Parse("notasize").Should().BeNull();

    [Fact]
    public void Parse_UnknownUnit_ReturnsNull()
        => FileSizeFormatter.Parse("1.0 ZB").Should().BeNull();
}
