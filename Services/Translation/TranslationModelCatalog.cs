using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace DocGlance.Services.Translation;

public sealed record TranslationModelFile(
    string Name,
    string Type,
    string SourceLanguage,
    string TargetLanguage,
    string Version,
    long DownloadBytes,
    string DecompressedSha256,
    Uri DownloadUri);

public sealed record TranslationModelPair(
    string SourceLanguage,
    string TargetLanguage,
    string Version,
    IReadOnlyList<TranslationModelFile> Files)
{
    public string Key => LanguageCatalog.PairKey(SourceLanguage, TargetLanguage);
    public long DownloadBytes => Files.Sum(file => file.DownloadBytes);
}

public sealed class TranslationModelCatalog
{
    private const long MaxCompressedFileBytes = 64L * 1024 * 1024;
    public const string ProviderName = "Firefox Translations";
    public const string RecordsEndpoint =
        "https://firefox.settings.services.mozilla.com/v1/buckets/main/collections/translations-models-v2/records";
    public const string AttachmentRoot =
        "https://firefox-settings-attachments.cdn.mozilla.net/";
    private const int SupportedModelMajor = 3;

    private readonly Dictionary<string, TranslationModelPair> _pairs;

    private TranslationModelCatalog(
        DateTimeOffset fetchedAt,
        Dictionary<string, TranslationModelPair> pairs)
    {
        FetchedAt = fetchedAt;
        _pairs = pairs;
    }

    public DateTimeOffset FetchedAt { get; }
    public IReadOnlyCollection<TranslationModelPair> Pairs => _pairs.Values;

    public bool TryGetPair(string source, string target, out TranslationModelPair pair) =>
        _pairs.TryGetValue(LanguageCatalog.PairKey(source, target), out pair!);

    public static TranslationModelCatalog Parse(string json, DateTimeOffset? fetchedAt = null)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out JsonElement data) ||
            data.ValueKind != JsonValueKind.Array)
            throw new FormatException("The translation model catalog has no records array.");

        var candidates = new List<TranslationModelFile>();
        foreach (JsonElement record in data.EnumerateArray())
        {
            if (!TryParseRecord(record, out TranslationModelFile? file))
                continue;

            if (file == null)
                continue;

            candidates.Add(file);
        }

        var pairs = new Dictionary<string, TranslationModelPair>(StringComparer.Ordinal);
        foreach (IGrouping<(string Source, string Target), TranslationModelFile> group in
                 candidates.GroupBy(file => (file.SourceLanguage, file.TargetLanguage)))
        {
            IGrouping<string, TranslationModelFile>? latestCompleteVersion = group
                .GroupBy(file => file.Version, StringComparer.Ordinal)
                .Where(version =>
                {
                    TranslationModelFile[] files = version.ToArray();
                    return files.GroupBy(file => file.Type, StringComparer.Ordinal)
                               .All(filesOfType => filesOfType.Count() == 1) &&
                        HasRequiredFiles(files);
                })
                .OrderByDescending(version => version.Key, VersionComparer.Instance)
                .FirstOrDefault();
            if (latestCompleteVersion == null)
                continue;

            TranslationModelFile[] files = latestCompleteVersion
                .OrderBy(file => file.Type, StringComparer.Ordinal)
                .ToArray();
            string version = latestCompleteVersion.Key;
            var pair = new TranslationModelPair(group.Key.Source, group.Key.Target, version, files);
            pairs.Add(pair.Key, pair);
        }

        if (pairs.Count == 0)
            throw new FormatException("The catalog contains no complete supported model pairs.");

        LanguageCatalog.RegisterModelLanguages(pairs.Values);
        return new TranslationModelCatalog(fetchedAt ?? DateTimeOffset.UtcNow, pairs);
    }

    private static bool TryParseRecord(JsonElement record, out TranslationModelFile? file)
    {
        file = null;
        if (!TryGetString(record, "fileType", out string type) ||
            !TryGetString(record, "sourceLanguage", out string source) ||
            !TryGetString(record, "targetLanguage", out string target) ||
            !TryGetString(record, "version", out string version) ||
            !TryGetString(record, "architecture", out string architecture) ||
            !TryGetString(record, "name", out string name) ||
            !record.TryGetProperty("attachment", out JsonElement attachment) ||
            !TryGetString(attachment, "location", out string location) ||
            !TryGetString(record, "decompressedHash", out string hash) ||
            !attachment.TryGetProperty("size", out JsonElement sizeElement) ||
            !sizeElement.TryGetInt64(out long size) ||
            size <= 0 ||
            size > MaxCompressedFileBytes)
            return false;

        source = source.ToLowerInvariant();
        target = target.ToLowerInvariant();
        if (!LanguageCatalog.IsSupportedModelCode(source) ||
            !LanguageCatalog.IsSupportedModelCode(target) ||
            string.Equals(source, target, StringComparison.Ordinal) ||
            !string.Equals(architecture, "base-memory", StringComparison.OrdinalIgnoreCase) ||
            !IsSupportedVersion(version) ||
            type is not ("model" or "vocab" or "srcvocab" or "trgvocab" or "lex") ||
            name.Length == 0 ||
            name.IndexOfAny(['/', '\\', ':']) >= 0 ||
            hash.Length != 64 ||
            !hash.All(Uri.IsHexDigit))
            return false;

        if (Uri.TryCreate(location, UriKind.Absolute, out _))
            return false;

        string relative = location.TrimStart('/');
        if (relative.Split('/').Any(segment => segment is "." or ".."))
            return false;
        if (!Uri.TryCreate(new Uri(AttachmentRoot), relative, out Uri? downloadUri) ||
            downloadUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(downloadUri.Host, new Uri(AttachmentRoot).Host, StringComparison.OrdinalIgnoreCase))
            return false;

        file = new TranslationModelFile(
            name,
            type,
            source,
            target,
            version,
            size,
            hash.ToLowerInvariant(),
            downloadUri);
        return true;
    }

    private static bool HasRequiredFiles(IReadOnlyCollection<TranslationModelFile> files)
    {
        bool hasModel = files.Any(file => file.Type == "model");
        bool hasSharedVocabulary = files.Any(file => file.Type == "vocab");
        bool hasSplitVocabularies =
            files.Any(file => file.Type == "srcvocab") &&
            files.Any(file => file.Type == "trgvocab");
        return hasModel && (hasSharedVocabulary || hasSplitVocabularies);
    }

    private static bool IsSupportedVersion(string version)
    {
        string major = version.Split('.')[0];
        return int.TryParse(major, NumberStyles.None, CultureInfo.InvariantCulture, out int value) &&
            value == SupportedModelMajor;
    }

    private static int CompareVersions(string left, string right)
    {
        string[] leftParts = left.Split('.');
        string[] rightParts = right.Split('.');
        for (int index = 0; index < Math.Max(leftParts.Length, rightParts.Length); index++)
        {
            int leftPart = index < leftParts.Length &&
                int.TryParse(leftParts[index], NumberStyles.None, CultureInfo.InvariantCulture, out int parsedLeft)
                    ? parsedLeft
                    : 0;
            int rightPart = index < rightParts.Length &&
                int.TryParse(rightParts[index], NumberStyles.None, CultureInfo.InvariantCulture, out int parsedRight)
                    ? parsedRight
                    : 0;
            int comparison = leftPart.CompareTo(rightPart);
            if (comparison != 0)
                return comparison;
        }
        return 0;
    }

    private static bool TryGetString(JsonElement element, string property, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(property, out JsonElement candidate) ||
            candidate.ValueKind != JsonValueKind.String)
            return false;
        value = candidate.GetString() ?? string.Empty;
        return value.Length > 0;
    }

    private sealed class VersionComparer : IComparer<string>
    {
        public static readonly VersionComparer Instance = new();
        public int Compare(string? x, string? y) =>
            CompareVersions(x ?? "0", y ?? "0");
    }
}
