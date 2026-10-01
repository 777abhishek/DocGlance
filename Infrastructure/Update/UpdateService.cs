using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
    private const string GitHubOwner = "777abhishek";
    private const string GitHubRepo  = "QuickView";
    private const string ApiUrl      = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

    // ── Events ────────────────────────────────────────────────────────────────
    /// <summary>Fired on the calling thread when a newer release is found.</summary>
    public static event EventHandler<UpdateAvailableEventArgs>? UpdateAvailable;

    // ── State ─────────────────────────────────────────────────────────────────
    private static bool _checked;
    
    // Reusable HttpClient to avoid resource leaks
    private static readonly HttpClient _httpClient = BuildHttpClient();

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
    /// Download the installer for <paramref name="release"/> to a temp file,
    /// verify its SHA-256 checksum and Authenticode signature, and launch it with the /SILENT flag.
    /// </summary>
    public static async Task DownloadAndInstallAsync(ReleaseInfo release)
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"QuickView-Setup-{release.Version}.exe");

            if (!File.Exists(tempPath))
            {
                Logger.Log($"[Update] Downloading {release.InstallerUrl}");
                using var resp  = await _httpClient.GetAsync(release.InstallerUrl,
                                      HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();

                await using var fs  = File.Create(tempPath);
                await using var src = await resp.Content.ReadAsStreamAsync();
                await src.CopyToAsync(fs);
                
                Logger.Log($"[Update] Download complete: {tempPath}");
            }

            // SEC-03: Verify SHA-256 checksum before signature verification
            if (!string.IsNullOrWhiteSpace(release.Sha256))
            {
                Logger.Log($"[Update] Verifying SHA-256 checksum...");
                if (!VerifySha256Checksum(tempPath, release.Sha256, out var checksumError))
                {
                    Logger.Error($"[Update] SHA-256 verification failed: {checksumError}");
                    
                    // Delete the tampered installer
                    try { File.Delete(tempPath); } catch { }
                    
                    throw new InvalidOperationException($"Installer checksum verification failed: {checksumError}");
                }
                Logger.Log($"[Update] SHA-256 checksum verified successfully");
            }
            else
            {
                Logger.Log($"[Update] WARNING: No SHA-256 checksum provided - skipping integrity check");
            }

            // CRITICAL: Verify Authenticode signature before executing
            if (!VerifyAuthenticodeSignature(tempPath, out var signatureError))
            {
                Logger.Error($"[Update] Signature verification failed: {signatureError}");
                
                // Delete the unsigned/invalid installer
                try { File.Delete(tempPath); } catch { }
                
                throw new InvalidOperationException($"Installer signature verification failed: {signatureError}");
            }

            Logger.Log($"[Update] Signature verified. Launching installer: {tempPath}");
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
            throw; // Re-throw so the UI can show an error message
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

            var release    = await _httpClient.GetFromJsonAsync<GitHubRelease>(ApiUrl);

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
            
            // Marshal event invocation to the UI thread so subscribers can safely
            // touch XAML elements without throwing cross-thread exceptions.
            // Application.Current is the base WinUI type; cast to our App class.
            var app = Microsoft.UI.Xaml.Application.Current as QuickView.App;
            var window = app?.MainWindow;
            if (window?.DispatcherQueue != null)
            {
                window.DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateAvailable?.Invoke(null, new UpdateAvailableEventArgs(info));
                });
            }
            else
            {
                // Fallback if we can't marshal: fire on current thread (background).
                // Subscribers must handle cross-thread access themselves.
                UpdateAvailable?.Invoke(null, new UpdateAvailableEventArgs(info));
            }
        }
        catch (Exception ex)
        {
            // Never crash the app over a failed update check.
            Logger.Error("[Update] Check failed (non-fatal)", ex);
        }
    }

    private static Version GetCurrentVersion()
    {
        // AssemblyVersion uses semantic versioning (e.g. 1.0.0.0) so it can be
        // compared against GitHub release tags like "v1.2.0".
        // InformationalVersion holds the build date (e.g. "2026.09.28") and is
        // used only for display in the About dialog — not for numeric comparison.
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v ?? new Version(1, 0, 0);
    }

    /// <summary>Returns the human-readable build date from InformationalVersion.</summary>
    public static string GetDisplayVersion()
    {
        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        return info ?? GetCurrentVersion().ToString(3);
    }

    private static HttpClient BuildHttpClient()
    {
        var client = new HttpClient();
        // GitHub API requires a User-Agent header.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"QuickView/{GetCurrentVersion()}");
        client.Timeout = TimeSpan.FromSeconds(20);
        return client;
    }

    /// <summary>
    /// SEC-03: Verifies the SHA-256 checksum of a downloaded file.
    /// </summary>
    /// <param name="filePath">Path to the file to verify</param>
    /// <param name="expectedSha256">Expected SHA-256 checksum (hex string)</param>
    /// <param name="error">Description of the verification failure</param>
    /// <returns>True if checksum matches, false otherwise</returns>
    private static bool VerifySha256Checksum(string filePath, string expectedSha256, out string error)
    {
        error = string.Empty;

        try
        {
            if (!File.Exists(filePath))
            {
                error = "File does not exist";
                return false;
            }

            if (string.IsNullOrWhiteSpace(expectedSha256))
            {
                error = "Expected checksum is empty";
                return false;
            }

            // Compute SHA-256 hash of the file
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            var computed = Convert.ToHexString(hash);

            // Compare (case-insensitive)
            if (computed.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            else
            {
                error = $"Checksum mismatch. Expected: {expectedSha256}, Computed: {computed}";
                return false;
            }
        }
        catch (Exception ex)
        {
            error = $"Exception during checksum verification: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Verifies the Authenticode signature of an executable file using WinVerifyTrust.
    /// </summary>
    /// <param name="filePath">Path to the executable to verify</param>
    /// <param name="error">Description of the verification failure</param>
    /// <returns>True if signature is valid and trusted, false otherwise</returns>
    private static bool VerifyAuthenticodeSignature(string filePath, out string error)
    {
        error = string.Empty;

        try
        {
            if (!File.Exists(filePath))
            {
                error = "File does not exist";
                return false;
            }

            // Use WinVerifyTrust API to check Authenticode signature
            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                pcwszFilePath = filePath,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };

            var data = new WINTRUST_DATA
            {
                cbStruct = Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE, // Don't check revocation (may fail offline)
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>()),
                dwStateAction = WTD_STATEACTION_VERIFY,
                hWVTStateData = IntPtr.Zero,
                pwszURLReference = IntPtr.Zero,
                dwProvFlags = WTD_SAFER_FLAG,
                dwUIContext = 0
            };

            Marshal.StructureToPtr(fileInfo, data.pFile, false);

            try
            {
                var guidAction = new Guid(WINTRUST_ACTION_GENERIC_VERIFY_V2);
                uint result = WinVerifyTrust(IntPtr.Zero, ref guidAction, ref data);

                if (result == 0)
                {
                    Logger.Log($"[Update] Signature verification succeeded for: {filePath}");
                    return true;
                }
                else
                {
                    error = $"WinVerifyTrust failed with code: 0x{result:X8} ({GetWinVerifyTrustErrorMessage(result)})";
                    return false;
                }
            }
            finally
            {
                // Clean up
                data.dwStateAction = WTD_STATEACTION_CLOSE;
                var guidAction = new Guid(WINTRUST_ACTION_GENERIC_VERIFY_V2);
                WinVerifyTrust(IntPtr.Zero, ref guidAction, ref data);
                Marshal.FreeHGlobal(data.pFile);
            }
        }
        catch (Exception ex)
        {
            error = $"Exception during signature verification: {ex.Message}";
            return false;
        }
    }

    private static string GetWinVerifyTrustErrorMessage(uint errorCode)
    {
        return errorCode switch
        {
            0x800B0100 => "No signature present",
            0x800B0109 => "Root certificate not trusted",
            0x800B010A => "Certificate chain to trusted root",
            0x800B0111 => "Certificate revoked",
            0x80096010 => "The signature is not valid",
            _ => "Unknown error"
        };
    }

    // ── WinVerifyTrust P/Invoke ──────────────────────────────────────────────

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

    private const string WINTRUST_ACTION_GENERIC_VERIFY_V2 = "{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}";

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_SAFER_FLAG = 0x100;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public int cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public int cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
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
    public string Sha256       { get; init; } = ""; // SEC-03: SHA-256 checksum for installer verification
}

/// <summary>Event args for <see cref="UpdateService.UpdateAvailable"/>.</summary>
public sealed class UpdateAvailableEventArgs(ReleaseInfo release) : EventArgs
{
    public ReleaseInfo Release { get; } = release;
}
