using DocGlance.Services.Translation;

namespace DocGlance.Tests.Services.Translation;

public sealed class TranslationModelCatalogTests
{
    [Fact]
    public void Parse_UsesSupportedModelRecordsAndRequiresModelAndVocabulary()
    {
        const string hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        string json = $$"""
            {
              "data": [
                {
                  "fileType": "model",
                  "sourceLanguage": "en",
                  "targetLanguage": "es",
                  "version": "3.0.0",
                  "architecture": "base-memory",
                  "name": "model.enes.bin",
                  "attachment": { "location": "models/model.enes.zst", "size": 300 },
                  "decompressedHash": "{{hash}}"
                },
                {
                  "fileType": "vocab",
                  "sourceLanguage": "en",
                  "targetLanguage": "es",
                  "version": "3.0.0",
                  "architecture": "base-memory",
                  "name": "vocab.enes.spm",
                  "attachment": { "location": "models/vocab.enes.zst", "size": 100 },
                  "decompressedHash": "{{hash}}"
                },
                {
                  "fileType": "model",
                  "sourceLanguage": "en",
                  "targetLanguage": "es",
                  "version": "3.1.0",
                  "architecture": "base-memory",
                  "name": "model.enes.v3.1.bin",
                  "attachment": { "location": "models/model.enes.v3.1.zst", "size": 350 },
                  "decompressedHash": "{{hash}}"
                },
                {
                  "fileType": "vocab",
                  "sourceLanguage": "en",
                  "targetLanguage": "es",
                  "version": "3.1.0",
                  "architecture": "base-memory",
                  "name": "vocab.enes.v3.1.spm",
                  "attachment": { "location": "models/vocab.enes.v3.1.zst", "size": 110 },
                  "decompressedHash": "{{hash}}"
                },
                {
                  "fileType": "model",
                  "sourceLanguage": "en",
                  "targetLanguage": "be",
                  "version": "3.0.0",
                  "architecture": "base-memory",
                  "name": "model.enbe.bin",
                  "attachment": { "location": "models/model.enbe.zst", "size": 250 },
                  "decompressedHash": "{{hash}}"
                },
                {
                  "fileType": "vocab",
                  "sourceLanguage": "en",
                  "targetLanguage": "be",
                  "version": "3.0.0",
                  "architecture": "base-memory",
                  "name": "vocab.enbe.spm",
                  "attachment": { "location": "models/vocab.enbe.zst", "size": 90 },
                  "decompressedHash": "{{hash}}"
                },
                {
                  "fileType": "model",
                  "sourceLanguage": "en",
                  "targetLanguage": "fr",
                  "version": "3.0.0",
                  "architecture": "unsupported",
                  "name": "model.enfr.bin",
                  "attachment": { "location": "models/model.enfr.zst", "size": 300 },
                  "decompressedHash": "{{hash}}"
                }
              ]
            }
            """;

        TranslationModelCatalog catalog = TranslationModelCatalog.Parse(json);

        catalog.Pairs.Should().HaveCount(2);
        catalog.TryGetPair("en", "es", out TranslationModelPair pair).Should().BeTrue();
        pair.Version.Should().Be("3.1.0");
        pair.Files.Select(file => file.Name).Should().Contain(["model.enes.v3.1.bin", "vocab.enes.v3.1.spm"]);
        pair.DownloadBytes.Should().Be(460);
        LanguageCatalog.Find("be")?.Name.Should().Be("Belarusian");
        catalog.TryGetPair("en", "be", out _).Should().BeTrue();
        catalog.TryGetPair("en", "fr", out _).Should().BeFalse();
    }

    [Fact]
    public void Parse_RejectsCatalogsWithoutCompleteSupportedPairs()
    {
        const string json = """{"data":[{"fileType":"model","sourceLanguage":"en","targetLanguage":"es","version":"3.0.0","architecture":"base-memory","name":"model.enes.bin","attachment":{"location":"https://example.com/model.zst","size":100},"decompressedHash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}]}""";

        Action action = () => TranslationModelCatalog.Parse(json);

        action.Should().Throw<FormatException>();
    }
}
