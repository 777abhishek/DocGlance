using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Composition.SystemBackdrops;
using QuickView.Services.Registration;
using QuickView.Services.Storage;
using QuickView.Infrastructure.Configuration;
using QuickView.Infrastructure.Logging;
using QuickView.Core.Enums;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace QuickView.Features.Settings;

public sealed partial class SettingsPage : Page
{
    private const string GitHubOwner = "777abhishek"; // TODO: Update with your GitHub username
    private const string GitHubRepo = "QuickView";

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    // ── Page load ─────────────────────────────────────────────────────────────

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Set version from assembly
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null)
        {
            // Format as YYYY.MM.DD
            VersionText.Text = $"Version {version.Major}.{version.Minor:D2}.{version.Build:D2}";
        }

        // Populate supported types list
        SupportedTypesText.Text = string.Join("  ",
            AppRegistration.SupportedExtensions
                .OrderBy(x => x)
                .Select(x => x));
        
        // UI-08: Load saved settings
        LoadSettings();

#if PACKAGED
        // In packaged builds, hide the manual registration UI entirely.
        // File associations are installed with the MSIX package and the user
        // sets defaults via Windows Settings → Default Apps.
        // The Settings page can show a message + shortcut button instead.
        RefreshAssocStatusForPackaged();
#else
        RefreshAssocStatus();
#endif
    }
    
    // ── UI-08: Settings persistence ───────────────────────────────────────────
    
    private void LoadSettings()
    {
        // Appearance
        ThemeComboBox.SelectedIndex = AppSettings.Theme switch
        {
            "Light" => 0,
            "Dark" => 1,
            _ => 2 // System
        };

        // Viewer
        ViewModeComboBox.SelectedIndex = AppSettings.DefaultZoomMode switch
        {
            "FitWidth" => 0,
            "FitPage" => 1,
            "ActualSize" => 2,
            _ => 0
        };
        ZoomSlider.Value = AppSettings.DefaultZoomPercent;
        ZoomValueText.Text = $"{AppSettings.DefaultZoomPercent}%";
        ThumbnailsToggle.IsOn = AppSettings.ShowThumbnails;
        SidebarToggle.IsOn = AppSettings.ShowSidebar;
        RememberViewToggle.IsOn = AppSettings.RememberViewState;

        // Home & Browser
        DefaultHomeViewComboBox.SelectedIndex = AppSettings.DefaultHomeView == "Recent" ? 0 : 1;
        RecentFilesSlider.Value = AppSettings.RecentFilesLimit;
        RecentFilesValueText.Text = AppSettings.RecentFilesLimit.ToString();

        // Session & Startup
        RestoreSessionToggle.IsOn = AppSettings.RestoreLastSession;
        RestoreWindowToggle.IsOn = AppSettings.RestoreWindowSize;

        // Performance
        CacheSizeSlider.Value = AppSettings.ThumbnailCacheSizeMB;
        CacheSizeValueText.Text = $"{AppSettings.ThumbnailCacheSizeMB} MB";
    }

    // ── Theme ─────────────────────────────────────────────────────────────────

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedIndex < 0) return;

        string themeValue = ThemeComboBox.SelectedIndex switch
        {
            0 => "Light",
            1 => "Dark",
            _ => "System"
        };
        AppSettings.Theme = themeValue;

        ElementTheme theme = ThemeComboBox.SelectedIndex switch
        {
            0 => ElementTheme.Light,
            1 => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        ((QuickView.App)Application.Current).SetTheme(theme);
    }
    
    // ── Viewer ────────────────────────────────────────────────────────────────

    private void ViewModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModeComboBox.SelectedIndex < 0) return;

        AppSettings.DefaultZoomMode = ViewModeComboBox.SelectedIndex switch
        {
            0 => "FitWidth",
            1 => "FitPage",
            2 => "ActualSize",
            _ => "FitWidth"
        };
    }

    private void ZoomSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        int zoomPercent = (int)ZoomSlider.Value;
        AppSettings.DefaultZoomPercent = zoomPercent;
        if (ZoomValueText != null)
        {
            ZoomValueText.Text = $"{zoomPercent}%";
        }
    }
    
    private void ThumbnailsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        AppSettings.ShowThumbnails = ThumbnailsToggle.IsOn;
    }

    private void SidebarToggle_Toggled(object sender, RoutedEventArgs e)
    {
        AppSettings.ShowSidebar = SidebarToggle.IsOn;
    }

    private void RememberViewToggle_Toggled(object sender, RoutedEventArgs e)
    {
        AppSettings.RememberViewState = RememberViewToggle.IsOn;
    }

    // ── Home & Browser ────────────────────────────────────────────────────────

    private void DefaultHomeViewComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DefaultHomeViewComboBox.SelectedIndex < 0) return;

        AppSettings.DefaultHomeView = DefaultHomeViewComboBox.SelectedIndex == 0 ? "Recent" : "All";
    }

    private void RecentFilesSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        int limit = (int)RecentFilesSlider.Value;
        AppSettings.RecentFilesLimit = limit;
        if (RecentFilesValueText != null)
        {
            RecentFilesValueText.Text = limit.ToString();
        }
    }

    // ── Session & Startup ─────────────────────────────────────────────────────

    private void RestoreSessionToggle_Toggled(object sender, RoutedEventArgs e)
    {
        AppSettings.RestoreLastSession = RestoreSessionToggle.IsOn;
    }

    private void RestoreWindowToggle_Toggled(object sender, RoutedEventArgs e)
    {
        AppSettings.RestoreWindowSize = RestoreWindowToggle.IsOn;
    }

    // ── Performance ───────────────────────────────────────────────────────────

    private void CacheSizeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        int sizeMB = (int)CacheSizeSlider.Value;
        AppSettings.ThumbnailCacheSizeMB = sizeMB;
        if (CacheSizeValueText != null)
        {
            CacheSizeValueText.Text = $"{sizeMB} MB";
        }
    }

    // ── Status display ────────────────────────────────────────────────────────

    /// <summary>Looks up a theme brush; falls back to a solid colour when the key is missing or not a brush.</summary>
    private static Brush ResourceBrush(string key, Windows.UI.Color fallback)
        => Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(fallback);

#if PACKAGED
    private void RefreshAssocStatusForPackaged()
    {
        // Show that associations are managed by the package/Store
        AssocStatusIcon.Glyph      = "\uE73E"; // checkmark
        AssocStatusIcon.Foreground = ResourceBrush("SystemFillColorSuccessBrush", Microsoft.UI.Colors.Green);
        AssocStatusText.Text       = "QuickView is a packaged app — file associations are managed automatically.";
        
        // Disable manual registration buttons (they're no-ops in packaged builds)
        RegisterButton.IsEnabled   = false;
        UnregisterButton.IsEnabled = false;
    }
#else
    private void RefreshAssocStatus()
    {
        bool registered = RegistrationHelper.IsRegistered;

        if (registered)
        {
            AssocStatusIcon.Glyph      = "\uE73E"; // checkmark
            AssocStatusIcon.Foreground = ResourceBrush("SystemFillColorSuccessBrush", Microsoft.UI.Colors.Green);
            AssocStatusText.Text       = "QuickView is registered — it appears in 'Open with' for all supported file types.";
            RegisterButton.IsEnabled   = false;
            UnregisterButton.IsEnabled = true;
        }
        else
        {
            AssocStatusIcon.Glyph      = "\uE783"; // info
            AssocStatusIcon.Foreground = ResourceBrush("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray);
            AssocStatusText.Text       = "Not registered. Click 'Register with Windows' to appear in 'Open with' menus.";
            RegisterButton.IsEnabled   = true;
            UnregisterButton.IsEnabled = false;
        }
    }
#endif

    // ── Register ──────────────────────────────────────────────────────────────

    private async void RegisterButton_Click(object sender, RoutedEventArgs e)
    {
        RegisterButton.IsEnabled   = false;
        UnregisterButton.IsEnabled = false;
        AssocStatusText.Text       = "Registering…";

        bool ok;
        try { ok = await RegistrationHelper.RegisterAsync(); }
        catch (Exception ex) { Logger.Error("RegisterButton_Click", ex); ok = false; }

        ShowInfoBar(
            ok ? "QuickView is now registered with Windows." : "Registration failed — check the log.",
            ok ? InfoBarSeverity.Success : InfoBarSeverity.Error);

#if PACKAGED
        RefreshAssocStatusForPackaged();
#else
        RefreshAssocStatus();
#endif
    }

    // ── Unregister ────────────────────────────────────────────────────────────

    private async void UnregisterButton_Click(object sender, RoutedEventArgs e)
    {
        RegisterButton.IsEnabled   = false;
        UnregisterButton.IsEnabled = false;
        AssocStatusText.Text       = "Removing registration…";

        bool ok;
        try { ok = await RegistrationHelper.UnregisterAsync(); }
        catch (Exception ex) { Logger.Error("UnregisterButton_Click", ex); ok = false; }

        ShowInfoBar(
            ok ? "QuickView has been removed from Windows file associations."
               : "Unregister failed — check the log.",
            ok ? InfoBarSeverity.Success : InfoBarSeverity.Error);

#if PACKAGED
        RefreshAssocStatusForPackaged();
#else
        RefreshAssocStatus();
#endif
    }

    // ── Open Default Apps ─────────────────────────────────────────────────────

    private async void OpenDefaultApps_Click(object sender, RoutedEventArgs e)
    {
        // Opens Windows Settings → Default apps directly
        await LaunchUriSafeAsync("ms-settings:defaultapps");
    }

    /// <summary>Launches a URI; failures are logged and shown, never thrown into an async-void handler.</summary>
    private async Task LaunchUriSafeAsync(string uri)
    {
        try
        {
            if (!await Windows.System.Launcher.LaunchUriAsync(new Uri(uri)))
                ShowInfoBar("Windows could not open the link.", InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            Logger.Error($"LaunchUri({uri})", ex);
            ShowInfoBar("Windows could not open the link.", InfoBarSeverity.Warning);
        }
    }

    // ── InfoBar helper ────────────────────────────────────────────────────────

    private void ShowInfoBar(string message, InfoBarSeverity severity)
    {
        AssocInfoBar.Severity = severity;
        AssocInfoBar.Message  = message;
        AssocInfoBar.IsOpen   = true;
    }

    // ── About Links ───────────────────────────────────────────────────────────

    private async void GitHubLink_Click(object sender, RoutedEventArgs e)
    {
        await LaunchUriSafeAsync($"https://github.com/{GitHubOwner}/{GitHubRepo}");
    }

    private async void IssuesLink_Click(object sender, RoutedEventArgs e)
    {
        await LaunchUriSafeAsync($"https://github.com/{GitHubOwner}/{GitHubRepo}/issues");
    }

    private async void ReleasesLink_Click(object sender, RoutedEventArgs e)
    {
        await LaunchUriSafeAsync($"https://github.com/{GitHubOwner}/{GitHubRepo}/releases");
    }

    private async void ViewLicensesLink_Click(object sender, RoutedEventArgs e)
    {
        // Create and show a dialog with full license information
        var dialog = new ContentDialog
        {
            Title = "Open Source Licenses",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot,
            Content = new ScrollViewer
            {
                MaxHeight = 500,
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Consolas, Courier New"),
                    FontSize = 11,
                    Padding = new Thickness(16),
                    Text = @"MIT License

Copyright (c) 2026 QuickView Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the ""Software""), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

---

THIRD-PARTY LIBRARIES

All third-party libraries used in QuickView are licensed under the MIT License:

• Windows App SDK (Microsoft Corporation)
• Win2D (Microsoft Corporation)
• CommunityToolkit.Mvvm (.NET Foundation)
• Entity Framework Core (.NET Foundation)
• Fluent System Icons (Microsoft Corporation)

Full license texts for these libraries can be found in their respective
NuGet packages and repositories."
                }
            }
        };

        await dialog.ShowAsync();
    }
}
