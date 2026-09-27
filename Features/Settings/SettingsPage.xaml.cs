using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuickView.Infrastructure.Registration;
using System;
using System.Linq;

namespace QuickView.Features.Settings;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    // ── Page load ─────────────────────────────────────────────────────────────

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Populate supported types list
        SupportedTypesText.Text = string.Join("  ",
            AppRegistration.SupportedExtensions
                .OrderBy(x => x)
                .Select(x => x));

        RefreshAssocStatus();
    }

    // ── Status display ────────────────────────────────────────────────────────

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

        RefreshAssocStatus();
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

        RefreshAssocStatus();
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

        ((Document.App)Application.Current).SetTheme(theme);
    }

    // ── InfoBar helper ────────────────────────────────────────────────────────

    private void ShowInfoBar(string message, InfoBarSeverity severity)
    {
        AssocInfoBar.Severity = severity;
        AssocInfoBar.Message  = message;
        AssocInfoBar.IsOpen   = true;
    }
}
