using DocGlance.Core.Constants;
using DocGlance.Services.Registration;

namespace DocGlance.Tests.Core.Constants;

public class SupportedExtensionsTests
{
    [Fact]
    public void All_HasNoDuplicates()
    {
        SupportedExtensions.All.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Categories_DoNotOverlap()
    {
        SupportedExtensions.Pdf.Intersect(SupportedExtensions.Text).Should().BeEmpty();
        SupportedExtensions.Pdf.Intersect(SupportedExtensions.Office).Should().BeEmpty();
        SupportedExtensions.Text.Intersect(SupportedExtensions.Office).Should().BeEmpty();
        SupportedExtensions.Image.Intersect(SupportedExtensions.Office).Should().BeEmpty();
        SupportedExtensions.Image.Intersect(SupportedExtensions.Text).Should().BeEmpty();
    }

    [Theory]
    [InlineData(".pdf", true, false, false, false)]
    [InlineData(".PDF", true, false, false, false)]
    [InlineData(".docx", false, false, true, false)]
    [InlineData(".pptm", false, false, true, false)]
    [InlineData(".yaml", false, true, false, false)]
    [InlineData(".rtf", false, true, false, false)]
    [InlineData(".PNG", false, false, false, true)]
    [InlineData(".exe", false, false, false, false)]
    public void Classification_IsCaseInsensitiveAndExclusive(
        string ext, bool pdf, bool text, bool office, bool image)
    {
        SupportedExtensions.IsPdf(ext).Should().Be(pdf);
        SupportedExtensions.IsText(ext).Should().Be(text);
        SupportedExtensions.IsOffice(ext).Should().Be(office);
        SupportedExtensions.IsImage(ext).Should().Be(image);
    }

    [Fact]
    public void Registration_UsesTheCanonicalList()
    {
        AppRegistration.SupportedExtensions.Should().BeEquivalentTo(SupportedExtensions.All);
    }
}
