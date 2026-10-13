using QuickView.Core.Enums;
using QuickView.Services.Storage;
using System;

namespace QuickView.Infrastructure.Configuration;

/// <summary>Application-wide settings with typed properties and automatic persistence.</summary>
public static class AppSettings
{
    private const string Key = "app_settings";

    private static AppSettingsData _data = SettingsStore.Load<AppSettingsData>(Key) ?? new();

    // ── Settings changed event ────────────────────────────────────────────────
    public static event EventHandler? SettingsChanged;

    private static void NotifyChanged() => SettingsChanged?.Invoke(null, EventArgs.Empty);

    // ── Appearance ────────────────────────────────────────────────────────────
    
    public static string Theme
    {
        get => _data.Theme;
        set { _data.Theme = value; Save(); }
    }

    // ── Viewer ────────────────────────────────────────────────────────────────
    
    public static string DefaultZoomMode
    {
        get => _data.DefaultZoomMode;
        set { _data.DefaultZoomMode = value; Save(); }
    }

    public static int DefaultZoomPercent
    {
        get => _data.DefaultZoomPercent;
        set { _data.DefaultZoomPercent = value; Save(); }
    }

    public static bool ShowThumbnails
    {
        get => _data.ShowThumbnails;
        set { _data.ShowThumbnails = value; Save(); }
    }

    public static bool ShowSidebar
    {
        get => _data.ShowSidebar;
        set { _data.ShowSidebar = value; Save(); }
    }

    public static bool RememberViewState
    {
        get => _data.RememberViewState;
        set { _data.RememberViewState = value; Save(); }
    }

    // ── Home & Browser ────────────────────────────────────────────────────────

    public static int RecentFilesLimit
    {
        get => _data.RecentFilesLimit;
        set { _data.RecentFilesLimit = value; Save(); }
    }

    public static string DefaultHomeView
    {
        get => _data.DefaultHomeView;
        set { _data.DefaultHomeView = value; Save(); }
    }

    // ── Session & Startup ─────────────────────────────────────────────────────

    public static bool RestoreLastSession
    {
        get => _data.RestoreLastSession;
        set { _data.RestoreLastSession = value; Save(); }
    }

    public static bool RestoreWindowSize
    {
        get => _data.RestoreWindowSize;
        set { _data.RestoreWindowSize = value; Save(); }
    }

    // ── Performance ───────────────────────────────────────────────────────────

    public static long ThumbnailCacheSize
    {
        get => _data.ThumbnailCacheSizeMB * 1024 * 1024;
        set { _data.ThumbnailCacheSizeMB = (int)(value / 1024 / 1024); Save(); }
    }

    public static int ThumbnailCacheSizeMB
    {
        get => _data.ThumbnailCacheSizeMB;
        set { _data.ThumbnailCacheSizeMB = value; Save(); }
    }

    // ── File Size Limits ──────────────────────────────────────────────────────
    // Note: These are const for security. To change, edit code and recompile.
    
    public const long MaxPdfFileSizeBytes = 500 * 1024 * 1024;      // 500 MB
    public const long MaxTextFileSizeBytes = 100 * 1024 * 1024;     // 100 MB
    public const long MaxImageFileSizeBytes = 200 * 1024 * 1024;    // 200 MB
    public const long MaxOfficeFileSizeBytes = 300 * 1024 * 1024;   // 300 MB
    public const long MaxGeneralFileSizeBytes = 1024 * 1024 * 1024; // 1 GB

    private static void Save()
    {
        SettingsStore.Save(Key, _data);
        NotifyChanged();
    }

    private sealed class AppSettingsData
    {
        // Appearance
        public string Theme { get; set; } = "System";

        // Viewer
        public string DefaultZoomMode { get; set; } = "FitWidth"; // FitWidth, FitPage, ActualSize
        public int DefaultZoomPercent { get; set; } = 100;
        public bool ShowThumbnails { get; set; } = true;
        public bool ShowSidebar { get; set; } = true;
        public bool RememberViewState { get; set; } = true;

        // Home & Browser
        public int RecentFilesLimit { get; set; } = 50;
        public string DefaultHomeView { get; set; } = "Recent"; // Recent or All

        // Session
        public bool RestoreLastSession { get; set; } = true;
        public bool RestoreWindowSize { get; set; } = true;

        // Performance
        public int ThumbnailCacheSizeMB { get; set; } = 500;
    }
}
