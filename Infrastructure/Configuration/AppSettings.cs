using QuickView.Core.Enums;
using QuickView.Infrastructure.Storage;

namespace QuickView.Infrastructure.Configuration;

/// <summary>Application-wide settings with typed properties and automatic persistence.</summary>
public static class AppSettings
{
    private const string Key = "app_settings";

    private static AppSettingsData _data = SettingsStore.Load<AppSettingsData>(Key) ?? new();

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
