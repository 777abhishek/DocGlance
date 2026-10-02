using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuickView.Services.Registration;
using System;
using System.Linq;
using System.Reflection;

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

    // ── Status display ────────────────────────────────────────────────────────

#if PACKAGED
    private void RefreshAssocStatusForPackaged()
    {
        // Show that associations are managed by the package/Store
        AssocStatusIcon.Glyph      = "\uE73E"; // checkmark
        AssocStatusIcon.Foreground = (Windows.UI.Color?)Application.Current.Resources["SystemFillColorSuccessBrush"] is null
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green)
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
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
            AssocStatusIcon.Foreground = (Windows.UI.Color?)Application.Current.Resources["SystemFillColorSuccessBrush"] is null
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green)
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            AssocStatusText.Text       = "QuickView is registered — it appears in 'Open with' for all supported file types.";
            RegisterButton.IsEnabled   = false;
            UnregisterButton.IsEnabled = true;
        }
        else
        {
            AssocStatusIcon.Glyph      = "\uE783"; // info
            AssocStatusIcon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
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

        bool ok = await RegistrationHelper.RegisterAsync();

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

        bool ok = await RegistrationHelper.UnregisterAsync();

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
        await Windows.System.Launcher.LaunchUriAsync(
            new Uri("ms-settings:defaultapps"));
    }

    // ── Theme ─────────────────────────────────────────────────────────────────

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedIndex < 0) return;

        ElementTheme theme = ThemeComboBox.SelectedIndex switch
        {
            0 => ElementTheme.Light,
            1 => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        ((QuickView.App)Application.Current).SetTheme(theme);
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
        await Windows.System.Launcher.LaunchUriAsync(
            new Uri($"https://github.com/{GitHubOwner}/{GitHubRepo}"));
    }

    private async void IssuesLink_Click(object sender, RoutedEventArgs e)
    {
        await Windows.System.Launcher.LaunchUriAsync(
            new Uri($"https://github.com/{GitHubOwner}/{GitHubRepo}/issues"));
    }

    private async void ReleasesLink_Click(object sender, RoutedEventArgs e)
    {
        await Windows.System.Launcher.LaunchUriAsync(
            new Uri($"https://github.com/{GitHubOwner}/{GitHubRepo}/releases"));
    }
}
