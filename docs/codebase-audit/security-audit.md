# QuickView — Security Audit (2026-10-11)

**Threat model:** local desktop viewer; the attacker controls *file content* (and file name/path) of documents the user opens, plus anything reachable from the machine (network via updater). No server-side surface exists. Severity is calibrated to that model.

## Attack-surface map (verified)

| Entry point | Path | Validation |
|---|---|---|
| Explorer double-click / activation | `Program.cs` redirect → `App.OnLaunched` → `TryOpenFromActivationArgs` → `OpenFilePath` | `FileSizeValidator.IsFileValidAndSafe` (`App.xaml.cs:235`) ✓ |
| Command line | `TryOpenFromCommandLine` (`App.xaml.cs:196-218`) | same ✓ |
| Home-page scan | `FolderScanner` → `PathValidator.IsValidFilePath` (`FolderScanner.cs:119`) ✓ |
| PDF open | `PdfRenderer.OpenDocumentAsync:53` validator ✓ |
| Text/code open | `TextFileReader.ReadCapped:17` validator + char cap ✓ |
| **XLSX/PPTX open** | `LoadXlsxAsync`/`LoadPptxAsync` (`ViewerPage.Pdf.cs:71-74`) | **none — FIND-023** |
| Updater | `UpdateService` (GitHub API over HTTPS) | SHA-256 + Authenticode ✓ |

## Findings (full detail in findings.md)

| ID | Severity | Status | Summary |
|---|---|---|---|
| FIND-023 | Medium | Confirmed | No file-size validation on XLSX/PPTX loaders; `File.ReadAllBytesAsync` unbounded → memory DoS on huge/hostile file. Fix: one guard per loader (caps already exist per-extension). |
| FIND-018 | Medium | Confirmed | Nine Rust engine crates as unpinned `git` deps — supply-chain drift on `cargo update`. Pin `rev`. |
| FIND-024 | Low | Confirmed | `PDFiumCore 97.0.4690` (≈2022 build) ships an old `pdfium.dll`; years of PDFium security fixes missing. Local-viewer blast radius (crash/RCE-with-complicated-preconditions) → Low. Upgrade path documented. |
| FIND-002 | High *(stability)* | Confirmed | Dispose race → native UAF — security-adjacent (memory corruption), listed under lifecycle. |
| FIND-009 | Medium | Needs Investigation | Unbounded `scale` param into `wc_render_page` — hostile caller only reachable internally today; clamp in Rust anyway. |

## Resolved since the 2026-10-01 audit (verified fixed)

- **SEC-01/SEC-02 (WebView2 XSS / JS in local HTML):** WebView2 and both Web renderers fully deleted from source. Only stale mentions remain (comments in `App.xaml.cs:142`, `IFileRenderer.cs` — cleanup with FIND-014/008).
- **SEC-03 (installer integrity):** SHA-256 checksum verified before Authenticode `WinVerifyTrust` (`UpdateService.cs:113-131`).
- **SEC-04 (DLL hijacking):** `pdfium.dll` loaded via `NativeLibrary.Load` from `AppContext.BaseDirectory` (`Pdfium.cs:21-37`).
- **SEC-05 (scanner path validation):** applied (`FolderScanner.cs:119`).

## Verified good practices

- `PathValidator`: reparse-point (symlink/junction), UNC, invalid-char, traversal checks before open.
- OOXML parsed **inside the Rust crates** (no C# `System.IO.Compression`+XML surface); panics wrapped in `catch_unwind` with null returns (FIND-021).
- Text capped at `maxChars`; logging sanitizes usernames (`Logger.cs` PathPattern); logs live under `%LOCALAPPDATA%\QuickView\Logs`.
- `TextFileReader` never slurps whole files; bounded 64 KB stream reads.
- Native grid JSON consumed via `System.Text.Json` (strict) — no `eval`-style parsing anywhere.

## Unassessed this pass (next security review)

- `WinVerifyTrust` revocation flags (old SEC-07) — code not re-read this round.
- `Package.appxmanifest` capabilities enumeration.
- Installer scripts (`build-installer.ps1`, `QuickView.iss`) hardening and winget manifest accuracy.
- Rust upstream crates' own CVE surface (zip/XML parsers inside storytold/* — out of repo; mitigated by rev-pinning + upgrade policy per FIND-018).
- PDFium CVE delta between build 4690 and current (FIND-024 depends on it).
