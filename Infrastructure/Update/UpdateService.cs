using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using QuickView.Infrastructure.Logging;

namespace QuickView.Infrastructure.Update;

/// <summary>
/// Lightweight background update checker.
/// Checks the GitHub Releases API once per session on startup (non-blocking).
/// Fires <see cref="UpdateAvailable"/> when a newer version is found.
/// </summary>
public static class UpdateService
{
    // ── Configuration ────────────────────────────────────────────────────────
    // Update this to your actual GitHub repo before publishing.
    private const string GitHubOwner = "your-org";
    private const string GitHubRepo  = "QuickView";
    private const string ApiUrl      = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

    // ── Events ────────────────────────────────────────────────────────────────
    /// <summary>Fired on the calling thread when a newer release is found.</summary>
    public static event EventHandler<UpdateAvailableEventArgs>? UpdateAvailable;

    // ── State ─────────────────────────────────────────────────────────────────
    private static bool _checked;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Start an async update check.  Call this once from OnLaunched.
    /// Silently swallows all errors — a failed update check must never crash the app.
    /// </summary>
    public static void CheckInBackground()
    {
        if (_checked) return;
        _checked = true;
        _ = CheckAsync();
    }

    /// <summary>
    /// Download the installer for <paramref name="release"/> to a temp file
    /// and launch it with the /SILENT flag so the UI stays out of the way.
    /// </summary>
    public static async Task DownloadAndInstallAsync(ReleaseInfo release)
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"QuickView-Setup-{release.Version}.exe");

            if (!File.Exists(tempPath))
            {
                Logger.Log($"[Update] Downloading {release.InstallerUrl}");
                using var http  = BuildHttpClient();
                using var resp  = await http.GetAsync(release.InstallerUrl,
                                      HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();

                await using var fs  = File.Create(tempPath);
                await using var src = await resp.Content.ReadAsStreamAsync();
                await src.CopyToAsync(fs);
            }

            Logger.Log($"[Update] Launching installer: {tempPath}");
            Process.Start(new ProcessStartInfo
            {
                FileName        = tempPath,
                Arguments       = "/SILENT /CLOSEAPPLICATIONS",
                UseShellExecute = true          // needed for UAC elevation
            });
        }
        catch (Exception ex)
        {
            Logger.Error("[Update] DownloadAndInstall failed", ex);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static async Task CheckAsync()
    {
        // Wait 10 s after startup so we don't slow the initial load.
        await Task.Delay(TimeSpan.FromSeconds(10));

        try
        {
            var current = GetCurrentVersion();
            Logger.Log($"[Update] Current version: {current}");

            using var http = BuildHttpClient();
            var release    = await http.GetFromJsonAsync<GitHubRelease>(ApiUrl);

            if (release is null || release.TagName is null)
                return;

            // Strip leading 'v' from tag names like "v1.2.0"
            var tag     = release.TagName.TrimStart('v');
            var latest  = Version.TryParse(tag, out var v) ? v : null;
            if (latest is null || latest <= current)
            {
                Logger.Log($"[Update] Already up-to-date ({current} >= {latest})");
                return;
            }

            // Find the first .exe asset
            var asset = Array.Find(release.Assets ?? [],
                            a => a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true);

            var info = new ReleaseInfo
            {
                Version      = tag,
                ReleaseNotes = release.Body ?? string.Empty,
                InstallerUrl = asset?.BrowserDownloadUrl ?? string.Empty,
                ReleaseUrl   = release.HtmlUrl ?? string.Empty,
            };

            Logger.Log($"[Update] New version available: {tag}");
            UpdateAvailable?.Invoke(null, new UpdateAvailableEventArgs(info));
        }
        catch (Exception ex)
        {
            // Never crash the app over a failed update check.
            Logger.Error("[Update] Check failed (non-fatal)", ex);
        }
    }

    private static Version GetCurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v ?? new Version(1, 0, 0);
    }

    private static HttpClient BuildHttpClient()
    {
        var client = new HttpClient();
        // GitHub API requires a User-Agent header.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"QuickView/{GetCurrentVersion()}");
        client.Timeout = TimeSpan.FromSeconds(20);
        return client;
    }

    // ── DTOs ─────────────────────────────────────────────────────────────────

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]  public string?        TagName { get; init; }
        [JsonPropertyName("html_url")]  public string?        HtmlUrl { get; init; }
        [JsonPropertyName("body")]      public string?        Body    { get; init; }
        [JsonPropertyName("assets")]    public GitHubAsset[]? Assets  { get; init; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]                  public string? Name                { get; init; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl  { get; init; }
    }
}

/// <summary>Information about a pending update.</summary>
public sealed class ReleaseInfo
{
    public string Version      { get; init; } = "";
    public string ReleaseNotes { get; init; } = "";
    public string InstallerUrl { get; init; } = "";
    public string ReleaseUrl   { get; init; } = "";
}

/// <summary>Event args for <see cref="UpdateService.UpdateAvailable"/>.</summary>
public sealed class UpdateAvailableEventArgs(ReleaseInfo release) : EventArgs
{
    public ReleaseInfo Release { get; } = release;
}
