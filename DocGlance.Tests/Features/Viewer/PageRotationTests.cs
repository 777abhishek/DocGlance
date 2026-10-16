using DocGlance.Features.Viewer;
using Windows.Foundation;

namespace DocGlance.Tests.Features.Viewer;

public sealed class PageRotationTests
{
    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void UnrotatePoint_RestoresOriginalPoint(double angle)
    {
        var original = new Point(21, 37);
        const double width = 100;
        const double height = 160;

        Point rotated = PageRotation.RotatePoint(original, width, height, angle);
        Point restored = PageRotation.UnrotatePoint(
            rotated, width, height, angle);

        restored.X.Should().BeApproximately(original.X, 0.001);
        restored.Y.Should().BeApproximately(original.Y, 0.001);
    }

    [Theory]
    [InlineData(90, 160, 100)]
    [InlineData(180, 100, 160)]
    [InlineData(270, 160, 100)]
    public void GetSize_SwapsDimensionsOnlyForQuarterTurns(
        double angle,
        double expectedWidth,
        double expectedHeight)
    {
        PageRotation.GetSize(100, 160, angle)
            .Should().Be((expectedWidth, expectedHeight));
    }

    [Fact]
    public void RotateRect_ProducesCorrectClockwiseBounds()
    {
        Rect rotated = PageRotation.RotateRect(
            new Rect(10, 20, 30, 40),
            100,
            160,
            90);

        rotated.Should().Be(new Rect(100, 10, 40, 30));
    }
}
