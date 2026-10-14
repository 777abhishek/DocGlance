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
    }

    [Theory]
    [InlineData(".pdf", true, false, false)]
    [InlineData(".PDF", true, false, false)]
    [InlineData(".docx", false, false, true)]
    [InlineData(".pptm", false, false, true)]
    [InlineData(".yaml", false, true, false)]
    [InlineData(".rtf", false, true, false)]
    [InlineData(".exe", false, false, false)]
    public void Classification_IsCaseInsensitiveAndExclusive(string ext, bool pdf, bool text, bool office)
    {
        SupportedExtensions.IsPdf(ext).Should().Be(pdf);
        SupportedExtensions.IsText(ext).Should().Be(text);
        SupportedExtensions.IsOffice(ext).Should().Be(office);
    }

    [Fact]
    public void Registration_UsesTheCanonicalList()
    {
        AppRegistration.SupportedExtensions.Should().BeEquivalentTo(SupportedExtensions.All);
    }
}
