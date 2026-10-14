using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Composition.SystemBackdrops;
using DocGlance.Services.Registration;
using DocGlance.Services.Updates;
using DocGlance.Services.Storage;
using DocGlance.Infrastructure.Configuration;
using DocGlance.Infrastructure.Logging;
using DocGlance.Core.Enums;
using DocGlance.UI.Shell;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace DocGlance.Features.Settings;

public sealed partial class SettingsPage : Page
{
    private const string GitHubOwner = "777abhishek"; // TODO: Update with your GitHub username
    private const string GitHubRepo = "DocGlance";
    private bool _isLoadingSettings;

    public SettingsPage()
    {
        InitializeComponent();
        QuickLookSpaceToggle.Toggled += QuickLookSpaceToggle_Toggled;
        Loaded += SettingsPage_Loaded;
    }

    // ── Page load ─────────────────────────────────────────────────────────────

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        VersionText.Text = $"Version {UpdateService.GetDisplayVersion()}";

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
        _isLoadingSettings = true;

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
        QuickLookSpaceToggle.IsOn = AppSettings.QuickLookSpaceShortcutEnabled;

        // Home & Browser
        DefaultHomeViewComboBox.SelectedIndex = AppSettings.DefaultHomeView == "Recent" ? 0 : 1;
        RecentFilesSlider.Value = AppSettings.RecentFilesLimit;
        RecentFilesValueText.Text = AppSettings.RecentFilesLimit.ToString();

        // Session & Startup
        RestoreSessionToggle.IsOn = AppSettings.RestoreLastSession;
        RestoreWindowToggle.IsOn = AppSettings.RestoreWindowSize;

        QuickLookHotkeyText.Text = FormatQuickLookHotkey(
            AppSettings.QuickLookHotkeyModifiers,
            AppSettings.QuickLookHotkeyKey);
        UpdateQuickLookHotkeyStatus();

        // Performance
        CacheSizeSlider.Value = AppSettings.ThumbnailCacheSizeMB;
        CacheSizeValueText.Text = $"{AppSettings.ThumbnailCacheSizeMB} MB";
        _isLoadingSettings = false;
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

        ((DocGlance.App)Application.Current).SetTheme(theme);
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

    private void QuickLookSpaceToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
            return;

        bool enabled = QuickLookSpaceToggle.IsOn;
        var window = TitleBarWindow.Current;
        string error = window == null ? "The DocGlance window is unavailable." : string.Empty;
        if (window == null || !window.TrySetQuickLookShortcutEnabled(enabled, out error))
        {
            _isLoadingSettings = true;
            QuickLookSpaceToggle.IsOn = !enabled;
            _isLoadingSettings = false;
            QuickLookHotkeyStatusText.Text = string.IsNullOrWhiteSpace(error)
                ? "Could not change the global shortcut setting."
                : error;
            return;
        }

        UpdateQuickLookHotkeyStatus();
    }

    private void ChangeQuickLookHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        TitleBarWindow.Current?.SuspendQuickLookHotkey();
        ChangeQuickLookHotkeyButton.Visibility = Visibility.Collapsed;
        QuickLookHotkeyCaptureBox.Text = "Press Ctrl, Alt, or Shift with another key…";
        QuickLookHotkeyCaptureBox.Visibility = Visibility.Visible;
        QuickLookHotkeyCaptureBox.Focus(FocusState.Programmatic);
    }

    private void QuickLookHotkeyCaptureBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        e.Handled = true;
        int virtualKey = (int)e.Key;

        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            FinishQuickLookHotkeyCapture(resumePrevious: true);
            return;
        }

        if (virtualKey is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5)
            return;

        uint modifiers = 0;
        if (IsKeyDown(0x11)) modifiers |= 0x0002; // Ctrl
        if (IsKeyDown(0x12)) modifiers |= 0x0001; // Alt
        if (IsKeyDown(0x10)) modifiers |= 0x0004; // Shift

        if (modifiers == 0)
        {
            QuickLookHotkeyCaptureBox.Text = "Hold Ctrl, Alt, or Shift, then press another key…";
            return;
        }

        var window = TitleBarWindow.Current;
        string error = window == null ? "The DocGlance window is unavailable." : string.Empty;
        if (window == null || !window.TrySetQuickLookHotkey(virtualKey, modifiers, out error))
        {
            QuickLookHotkeyCaptureBox.Text = string.IsNullOrWhiteSpace(error)
                ? "Could not register that shortcut. Try another combination…"
                : error + " Try another combination…";
            return;
        }

        QuickLookHotkeyText.Text = FormatQuickLookHotkey(modifiers, virtualKey);
        FinishQuickLookHotkeyCapture(resumePrevious: false);
        UpdateQuickLookHotkeyStatus();
    }

    private void FinishQuickLookHotkeyCapture(bool resumePrevious)
    {
        QuickLookHotkeyCaptureBox.Visibility = Visibility.Collapsed;
        ChangeQuickLookHotkeyButton.Visibility = Visibility.Visible;

        var window = TitleBarWindow.Current;
        if (resumePrevious && window != null && !window.ResumeQuickLookHotkey(out string error))
            QuickLookHotkeyStatusText.Text = error;
        else
            UpdateQuickLookHotkeyStatus();

        ChangeQuickLookHotkeyButton.Focus(FocusState.Programmatic);
    }

    private void UpdateQuickLookHotkeyStatus()
    {
        if (!AppSettings.QuickLookSpaceShortcutEnabled)
        {
            QuickLookHotkeyStatusText.Text = "Global shortcut is disabled.";
            return;
        }

        QuickLookHotkeyStatusText.Text = TitleBarWindow.Current?.IsQuickLookHotkeyRegistered == true
            ? "Registered with Windows. Works while DocGlance is running; select a file in Explorer first."
            : "Could not register with Windows. The shortcut may already be in use.";
    }

    private static string FormatQuickLookHotkey(uint modifiers, int virtualKey)
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((modifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((modifiers & 0x0001) != 0) parts.Add("Alt");
        if ((modifiers & 0x0004) != 0) parts.Add("Shift");

        string keyName = ((Windows.System.VirtualKey)virtualKey).ToString();
        if (keyName.StartsWith("Number", StringComparison.Ordinal))
            keyName = keyName.Substring("Number".Length);
        parts.Add(keyName);
        return string.Join(" + ", parts);
    }

    private static bool IsKeyDown(int virtualKey) => GetKeyState(virtualKey) < 0;

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

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
        AssocStatusText.Text       = "DocGlance is registered for its supported file types. Choose DocGlance as the default per type in Windows Settings.";
        
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
            AssocStatusText.Text       = "DocGlance is registered — it appears in 'Open with' for all supported file types.";
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
            ok ? "DocGlance is now registered for all supported file types in 'Open with'." : "Registration failed — check the log.",
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
            ok ? "DocGlance has been removed from Windows file associations."
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

Copyright (c) 2026 DocGlance Contributors

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

All third-party libraries used in DocGlance are licensed under the MIT License:

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
