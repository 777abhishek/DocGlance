using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuickView.Infrastructure.Logging;
using QuickView.Services.Updates;
using System;

namespace QuickView.Services.Updates;

/// <summary>
/// A subtle InfoBar that appears at the top of the window when an update is available.
/// Wire it up by placing &lt;update:UpdateBanner x:Name="UpdateBanner" /&gt; in your XAML
/// and calling <see cref="Attach"/> from OnLaunched.
/// </summary>
public sealed partial class UpdateBanner : UserControl
{
    private ReleaseInfo? _pending;

    public UpdateBanner()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Subscribe to <see cref="UpdateService.UpdateAvailable"/> and show the
    /// banner on the UI thread when a new version is detected.
    /// </summary>
    public void Attach()
    {
        UpdateService.UpdateAvailable += OnUpdateAvailable;
        UpdateService.CheckInBackground();
    }

    private void OnUpdateAvailable(object? sender, UpdateAvailableEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _pending = e.Release;
            InfoBar.Title   = $"QuickView {e.Release.Version} is available";
            InfoBar.Message = "A new version is ready to install.";
            InfoBar.IsOpen  = true;
        });
    }

    private async void OnInstallClick(object sender, RoutedEventArgs e)
    {
        if (_pending is null) return;

        InstallButton.IsEnabled = false;
#if PACKAGED
        InstallButton.Content   = "Opening Store…";
#else
        InstallButton.Content   = "Downloading…";
#endif

        await UpdateService.DownloadAndInstallAsync(_pending);

        // Packaged: user went to Store page — re-enable the button.
        // Unpackaged: the installer will close the app; if something went wrong, re-enable.
        InstallButton.IsEnabled = true;
#if PACKAGED
        InstallButton.Content   = "Get from Store";
#else
        InstallButton.Content   = "Install Update";
#endif
    }
}
