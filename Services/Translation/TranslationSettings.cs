using DocGlance.Services.Storage;

namespace DocGlance.Services.Translation;

public static class TranslationSettings
{
    private const string StoreKey = "translation_settings";
    private static Data _data = SettingsStore.Load<Data>(StoreKey) ?? new();

    public static string SourceLanguage
    {
        get => LanguageCatalog.Find(_data.SourceLanguage)?.Code ?? "auto";
        set
        {
            string normalized = value == "auto"
                ? "auto"
                : LanguageCatalog.Find(value)?.Code ?? "auto";
            if (_data.SourceLanguage == normalized)
                return;
            _data.SourceLanguage = normalized;
            Save();
        }
    }

    public static string TargetLanguage
    {
        get => LanguageCatalog.Find(_data.TargetLanguage)?.Code ?? "en";
        set
        {
            string normalized = LanguageCatalog.Find(value)?.Code ?? "en";
            if (_data.TargetLanguage == normalized)
                return;
            _data.TargetLanguage = normalized;
            Save();
        }
    }

    private static void Save() => SettingsStore.Save(StoreKey, _data);

    private sealed class Data
    {
        public string SourceLanguage { get; set; } = "auto";
        public string TargetLanguage { get; set; } = "en";
    }
}
