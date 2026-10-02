using QuickView.Core.Enums;
using QuickView.Services.Storage;

namespace QuickView.Infrastructure.Configuration;

/// <summary>Application-wide settings with typed properties and automatic persistence.</summary>
public static class AppSettings
{
    private const string Key = "app_settings";

    private static AppSettingsData _data = SettingsStore.Load<AppSettingsData>(Key) ?? new();

    // File size limits (in bytes) to prevent DoS attacks
    // These can be adjusted by power users via settings files
    public const long MaxPdfFileSizeBytes = 500 * 1024 * 1024;      // 500 MB
    public const long MaxTextFileSizeBytes = 100 * 1024 * 1024;     // 100 MB
    public const long MaxImageFileSizeBytes = 200 * 1024 * 1024;    // 200 MB
    public const long MaxOfficeFileSizeBytes = 300 * 1024 * 1024;   // 300 MB
    public const long MaxGeneralFileSizeBytes = 1024 * 1024 * 1024; // 1 GB

    public static ViewMode DefaultViewMode
    {
        get => _data.DefaultViewMode;
        set { _data.DefaultViewMode = value; Save(); }
    }

    public static bool ShowThumbnails
    {
        get => _data.ShowThumbnails;
        set { _data.ShowThumbnails = value; Save(); }
    }

    public static string Theme
    {
        get => _data.Theme;
        set { _data.Theme = value; Save(); }
    }

    private static void Save() => SettingsStore.Save(Key, _data);

    private sealed class AppSettingsData
    {
        public ViewMode DefaultViewMode { get; set; } = ViewMode.Grid;
        public bool     ShowThumbnails  { get; set; } = true;
        public string   Theme           { get; set; } = "System";
    }
}
