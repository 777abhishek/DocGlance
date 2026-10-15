using DocGlance.Services.Translation;

namespace DocGlance.Tests.Services.Translation;

public sealed class LanguageCatalogTests
{
    [Fact]
    public void RequiredPairs_UsesEnglishPivotForNonEnglishLanguages()
    {
        LanguageCatalog.RequiredPairs("de", "es")
            .Should().Equal("de-en", "en-es");
    }

    [Theory]
    [InlineData("en", "fr", "en-fr")]
    [InlineData("fr", "en", "fr-en")]
    public void RequiredPairs_UsesDirectModelWhenOneLanguageIsEnglish(
        string source,
        string target,
        string expected)
    {
        LanguageCatalog.RequiredPairs(source, target).Should().Equal(expected);
    }

    [Fact]
    public void RequiredPairs_RejectsUnsupportedAndSameLanguageRoutes()
    {
        LanguageCatalog.RequiredPairs("ja", "en").Should().BeEmpty();
        LanguageCatalog.RequiredPairs("en", "en").Should().BeEmpty();
    }

    [Fact]
    public void SearchTerms_IncludesHindiNameAndIsoCode()
    {
        LanguageCatalog.SearchTerms("hi")
            .Should().Contain("Hindi")
            .And.Contain("hin");
    }
}
