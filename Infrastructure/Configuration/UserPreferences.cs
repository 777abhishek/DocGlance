using QuickView.Infrastructure.Storage;

namespace QuickView.Infrastructure.Configuration;

/// <summary>User-facing preferences (sidebar width, last zoom, etc.).</summary>
public static class UserPreferences
{
    private const string Key = "user_prefs";
    private static UserPreferencesData _data =
        SettingsStore.Load<UserPreferencesData>(Key) ?? new();

    public static int SidebarWidth
    {
        get => _data.SidebarWidth;
        set { _data.SidebarWidth = value; Save(); }
    }

    public static double LastZoom
    {
        get => _data.LastZoom;
        set { _data.LastZoom = value; Save(); }
    }

    public static bool SidebarOpen
    {
        get => _data.SidebarOpen;
        set { _data.SidebarOpen = value; Save(); }
    }

    private static void Save() => SettingsStore.Save(Key, _data);

    private sealed class UserPreferencesData
    {
        public int    SidebarWidth { get; set; } = 200;
        public double LastZoom     { get; set; } = 1.0;
        public bool   SidebarOpen  { get; set; } = true;
    }
}
