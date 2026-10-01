# QuickView — Security Audit

> Audit date: 2026-10-01  
> Read-only analysis. No source files were modified.  
> Severity scale: Critical / High / Medium / Low / Informational

---

## Executive Summary

QuickView has **meaningful security foundations** that are above average for an open-source desktop viewer: path traversal is explicitly blocked, symlinks are rejected, file size limits are enforced per type, log paths are sanitised, and installer downloads are verified with `WinVerifyTrust`. The most significant risks are concentrated in the HTML/Markdown rendering pipeline (WebView2 with local file access and an XSS-capable custom parser) and the native PDFium interop layer.

**Finding count by severity:**

| Severity | Count |
|---|---|\
| High | 3 |
| Medium | 5 |
| Low | 4 |
| Informational | 4 |

---

## Findings

---

### SEC-01 — HtmlRenderer loads arbitrary local file:// URIs in WebView2

**Severity:** High  
**Confidence:** Confirmed

**File:** `Rendering/Web/HtmlRenderer.cs`  
**Class:** `HtmlRenderer`  
**Method:** `RenderAsync`

**Observation:**
```csharp
var wv = new WebView2();
wv.Source = new System.Uri(filePath);
```
The renderer constructs a `file://` URI directly from `filePath` and navigates to it. WebView2 with a `file://` source can:
- Access other local files via relative paths
- Execute arbitrary JavaScript embedded in the HTML file
- Potentially use `fetch()` or `XMLHttpRequest` to read sibling files

**Attack scenario:** A user opens a malicious `.html` file. The file contains `<script>` that reads local files and exfiltrates data.

**Recommended mitigation:**
- Set `CoreWebView2.Settings.IsScriptEnabled = false`
- Set `CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false`
- Add `NavigationStarting` handler to block navigation to non-file:// origins
- Consider `NavigateToString` with sanitised HTML instead

---

### SEC-02 — MarkdownRenderer custom parser susceptible to XSS

**Severity:** High  
**Confidence:** Confirmed

**File:** `Rendering/Web/MarkdownRenderer.cs`  
**Class:** `MarkdownRenderer`  
**Method:** `InlineFormat`, `BuildHtml`

**Observation:**
The Markdown-to-HTML converter applies regex substitutions that generate raw HTML after text has been escaped:
```csharp
text = Regex.Replace(text, @"\[([^\]]+)\]\(([^\s\)]+)...",
    m => $"<a href='{m.Groups[2].Value}' ...");
```
A Markdown link like `[click](javascript:alert(1))` produces `<a href='javascript:alert(1)'>` in the output.

**Attack scenario:** User opens crafted `.md` file with `javascript:` links. WebView2 executes the script.

**Recommended mitigation:**
- Validate all URLs: only allow `http://`, `https://`, relative paths
- Reject `javascript:`, `data:`, `vbscript:` schemes
- Use established library like `Markdig` instead of custom parser

---

### SEC-03 — Update installer downloaded without SHA-256 verification

**Severity:** High  
**Confidence:** Logical analysis

**File:** `Infrastructure/Update/UpdateService.cs`  
**Method:** `DownloadAndInstallAsync`

**Observation:**
The installer URL comes from GitHub API response. If compromised, a malicious binary could be downloaded. While `WinVerifyTrust` checks Authenticode signatures, no SHA-256 digest is verified.

**Recommended mitigation:**
- Publish SHA-256 checksum in release
- Verify digest before launching installer
- Validate `InstallerUrl` hostname is `github.com` or `githubusercontent.com`

---

### SEC-04 — pdfium.dll loaded by short name without explicit path

**Severity:** Medium  
**Confidence:** Confirmed

**File:** `Rendering/Pdf/Pdfium.cs`

**Observation:**
```csharp
private const string Dll = "pdfium";
```
DLL loaded via standard search order. Could allow DLL hijacking if attacker writes malicious `pdfium.dll` to PATH directory.

**Recommended mitigation:**
- Use `SetDllDirectory("")` or `AddDllDirectory`
- Explicitly load via `NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "pdfium.dll"))`

---

### SEC-05 — FolderScanner does not validate paths before adding to index

**Severity:** Medium  
**Confidence:** Confirmed

**File:** `FileSystem/Scanner/FolderScanner.cs`  
**Method:** `ScanDir`

**Observation:**
`FolderScanner` calls `DocumentItem.FromFileInfo` without running `PathValidator`. Symlinks inside scanned folders bypass validation and appear in UI.

**Recommended mitigation:**
- Apply `PathValidator.IsValidFilePath` before adding items
- Skip invalid items; log at debug level

---

### SEC-06 — BookmarkStore writes paths without validation

**Severity:** Medium  
**Confidence:** Confirmed

**File:** `Infrastructure/Storage/BookmarkStore.cs`  
**Method:** `Add`

**Observation:**
`filePath` stored in JSON without validation. Full paths including username are persisted.

**Recommended mitigation:**
- Validate `filePath` before saving
- Consider storing relative paths instead of absolute

---

### SEC-07 — WTD_REVOKE_NONE disables revocation checking

**Severity:** Medium  
**Confidence:** Confirmed

**File:** `Infrastructure/Update/UpdateService.cs`  
**Method:** `VerifyAuthenticodeSignature`

**Observation:**
Revocation checking disabled for offline compatibility. Revoked certificates would still pass.

**Recommended mitigation:**
- Try `WTD_REVOKE_WHOLECHAIN` with timeout, fallback to `_NONE` on connection failure
- Document trade-off in code comment

---

### SEC-08 — MarkdownRenderer HTML has no Content Security Policy

**Severity:** Medium  
**Confidence:** Confirmed

**File:** `Rendering/Web/MarkdownRenderer.cs`  
**Method:** `BuildHtml`

**Observation:**
Generated HTML lacks CSP meta tag. Allows inline scripts, external resources, `javascript:` URIs.

**Recommended mitigation:**
Add CSP header:
```html
<meta http-equiv="Content-Security-Policy" 
      content="default-src 'none'; style-src 'unsafe-inline'; img-src 'self' data:;">
```

---

### SEC-09 — Logger only sanitises Windows-style paths

**Severity:** Low  
**Confidence:** Confirmed

**File:** `Infrastructure/Logging/Logger.cs`  
**Method:** `SanitizePath`

**Observation:**
Regex pattern only catches `C:\Users\Username`. Does not sanitise UNC paths or other user-identifiable patterns.

**Recommended mitigation:**
- Expand pattern to catch UNC paths and AppData sub-paths
- Log only filename instead of full path

---

### SEC-10 — Downloaded installer written to shared temp directory

**Severity:** Low  
**Confidence:** Confirmed

**File:** `Infrastructure/Update/UpdateService.cs`  
**Method:** `DownloadAndInstallAsync`

**Observation:**
Installer written to `%TEMP%` which other user processes can access. TOCTOU race condition possible.

**Recommended mitigation:**
- Create unique subdirectory with restrictive ACLs
- Use `Path.GetRandomFileName()` for filename

---

### SEC-11 — No input validation on registry values

**Severity:** Low  
**Confidence:** Confirmed

**File:** `Infrastructure/Registration/AppRegistration.cs`  
**Method:** `IsRegistered`

**Observation:**
Registry value read and used in string comparison. User-controlled but handled safely.

**Mitigation:** None required; informational only.

---

### SEC-12 — File size limits are constants, not configurable

**Severity:** Informational  
**Confidence:** Confirmed

**File:** `Infrastructure/Configuration/AppSettings.cs`

**Observation:**
```csharp
public const long MaxPdfFileSizeBytes = 500 * 1024 * 1024;
```
Limits are `const`, not runtime configurable despite documentation suggesting otherwise.

**Recommended mitigation:**
- Change to `static readonly` properties loaded from settings
- Or fix documentation

---

### SEC-13 — WebView2 instances not disposed

**Severity:** Informational  
**Confidence:** Confirmed

**File:** `Rendering/Web/MarkdownRenderer.cs`

**Observation:**
`WebView2` implements `IDisposable` but instances are not explicitly disposed when tabs close. Process may continue running.

**Recommended mitigation:**
- Track WebView2 controls and dispose in `CancelLoad()`
- Subscribe to `Unloaded` event to clean up

---

### SEC-14 — AllowUnsafeBlocks enabled project-wide

**Severity:** Informational  
**Confidence:** Confirmed

**File:** `QuickView.csproj`

**Observation:**
`<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` required for PDFium bitmap rendering but allows unsafe code anywhere.

**Mitigation:** Standard pattern for WinUI + PDFium; document requirement in comment.

---

## Dependency Security

| Package | Version | Status |
|---|---|---|\
| `Microsoft.WindowsAppSDK` | 2.4.0 | Current |
| `Microsoft.EntityFrameworkCore.Sqlite` | 8.0.11 | .NET 8 LTS |
| `PDFiumCore` | 97.0.4690 | Check for updates |
| `CommunityToolkit.Mvvm` | 8.4.2 | Current |
| `Microsoft.Graphics.Win2D` | 1.4.0 | Current |

**PDFium note:** Version 97.0.4690 from 2024. Monitor for security patches.

---

## Native Code Summary

| DLL | Usage | Risk Level |
|---|---|---|\
| `pdfium.dll` | Full P/Invoke (~80 exports) | Medium (memory safety dependent on library version) |
| `kernel32.dll` | `GetFileAttributes` | Low |
| `shell32.dll` | `SHChangeNotify` | Low |
| `user32.dll` | `LoadImage`, `SendMessage` | Low |
| `wintrust.dll` | `WinVerifyTrust` | Medium (unmanaged marshalling) |

---

## Secure Development Recommendations

1. Add CSP to all HTML generated by `MarkdownRenderer`
2. Validate URLs before placing in `href`/`src` attributes
3. Restrict `pdfium.dll` loading to application directory
4. Add SHA-256 checksum verification for installer downloads
5. Apply `PathValidator` in `FolderScanner`
6. Disable JavaScript in `HtmlRenderer` WebView2
7. Dispose WebView2 instances on tab close
8. Make file size limits runtime-configurable
