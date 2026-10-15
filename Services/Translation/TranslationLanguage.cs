using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DocGlance.Services.Translation;

public sealed record TranslationLanguage(string Code, string Name)
{
    public override string ToString() => Name;
}

public static class LanguageCatalog
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, TranslationLanguage> LanguagesByCode =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["de"] = new("de", "German"),
            ["en"] = new("en", "English"),
            ["es"] = new("es", "Spanish"),
            ["fr"] = new("fr", "French"),
        };

    public static IReadOnlyList<TranslationLanguage> Languages
    {
        get
        {
            lock (Gate)
                return LanguagesByCode.Values.OrderBy(language => language.Name).ToArray();
        }
    }

    public static TranslationLanguage? Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;
        lock (Gate)
            return LanguagesByCode.TryGetValue(code, out TranslationLanguage? language)
                ? language
                : null;
    }

    public static IReadOnlyList<string> SearchTerms(string code)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { code };
        TranslationLanguage? language = Find(code);
        if (language is not null)
            terms.Add(language.Name);

        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(code);
            terms.Add(culture.EnglishName);
            terms.Add(culture.NativeName);
            terms.Add(culture.ThreeLetterISOLanguageName);
        }
        catch (CultureNotFoundException)
        {
        }

        return terms.Where(term => !string.IsNullOrWhiteSpace(term)).ToArray();
    }

    public static bool IsSupportedPair(string source, string target) =>
        Find(source) != null && Find(target) != null &&
        !string.Equals(source, target, StringComparison.OrdinalIgnoreCase);

    public static bool IsSupportedModelCode(string code) =>
        code.Length == 2 && code.All(character => character is >= 'a' and <= 'z');

    public static void RegisterModelLanguages(IEnumerable<TranslationModelPair> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        lock (Gate)
        {
            foreach (TranslationModelPair pair in pairs)
            {
                RegisterModelLanguage(pair.SourceLanguage);
                RegisterModelLanguage(pair.TargetLanguage);
            }
        }
    }

    public static string PairKey(string source, string target) => $"{source}-{target}";

    public static IReadOnlyList<string> RequiredPairs(string source, string target)
    {
        if (!IsSupportedPair(source, target))
            return Array.Empty<string>();

        string pair = PairKey(source, target);
        if (source == "en" || target == "en")
            return [pair];

        return [PairKey(source, "en"), PairKey("en", target)];
    }

    private static void RegisterModelLanguage(string code)
    {
        if (LanguagesByCode.ContainsKey(code))
            return;

        string name;
        try
        {
            name = CultureInfo.GetCultureInfo(code).EnglishName;
        }
        catch (CultureNotFoundException)
        {
            name = code.ToUpperInvariant();
        }
        LanguagesByCode.Add(code, new TranslationLanguage(code, name));
    }
}
