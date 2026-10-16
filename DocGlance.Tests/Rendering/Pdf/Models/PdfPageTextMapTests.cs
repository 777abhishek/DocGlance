using DocGlance.Rendering.Pdf.Models;

namespace DocGlance.Tests.Rendering.Pdf.Models;

public sealed class PdfPageTextMapTests
{
    [Fact]
    public void HitTest_ReturnsContainingGlyphBeforeNearbyGlyphs()
    {
        var map = new PdfPageTextMap(0, new[]
        {
            new PdfSearchRect(10, 10, 5, 10),
            new PdfSearchRect(16, 10, 5, 10),
        });

        map.HitTest(18, 14).Should().Be(1);
    }

    [Fact]
    public void HitTest_UsesToleranceForPointsJustOutsideText()
    {
        var map = new PdfPageTextMap(0, new[]
        {
            new PdfSearchRect(10, 10, 5, 10),
        });

        map.HitTest(8, 14, tolerance: 3).Should().Be(0);
        map.HitTest(6, 14, tolerance: 3).Should().Be(-1);
    }

    [Fact]
    public void HitTest_AllowsSelectionEndpointToReachShorterNextLine()
    {
        var map = new PdfPageTextMap(0, new[]
        {
            new PdfSearchRect(10, 10, 10, 10),
            new PdfSearchRect(22, 10, 10, 10),
            new PdfSearchRect(34, 10, 10, 10),
            new PdfSearchRect(10, 24, 10, 10),
            new PdfSearchRect(22, 24, 10, 10),
        });

        map.HitTest(40, 25).Should().Be(-1);
        map.HitTest(40, 25, allowDistantHorizontalHit: true).Should().Be(4);
    }

    [Fact]
    public void GetSelectionRects_MergesGlyphsByLineAndSeparatesWrappedLines()
    {
        var map = new PdfPageTextMap(0, new[]
        {
            new PdfSearchRect(10, 10, 5, 10),
            new PdfSearchRect(16, 10, 6, 10),
            new PdfSearchRect(10, 24, 5, 10),
        });

        map.GetSelectionRects(0, 3).Should().Equal(
            new PdfSearchRect(10, 10, 12, 10),
            new PdfSearchRect(10, 24, 5, 10));
    }

    [Fact]
    public void GetSelectionRects_ClampsRangeToAvailableGlyphs()
    {
        var map = new PdfPageTextMap(0, new[]
        {
            new PdfSearchRect(10, 10, 5, 10),
        });

        map.GetSelectionRects(-5, 500).Should().Equal(
            new PdfSearchRect(10, 10, 5, 10));
    }
}
