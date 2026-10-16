# 08 — Dependency and Interop Rules (re-audit 2026-10-16)

## §1 Layer dependency table (encoded as tests in Phase 5)

| Layer | May reference | Must not reference |
|---|---|---|
| `Common`, `Core` | each other | WinUI/XAML, Rendering, Interop, Features, Services, UI |
| `Infrastructure` | `Common`, `Core` | XAML controls, Features, Rendering, Interop |
| `Interop` | `Infrastructure` (gate), `Core` models it returns | XAML controls, Features, Services |
| `Rendering` | `Interop`, `Core`, `Common`, `Infrastructure`, `Rendering.Shared` | `Microsoft.UI.Xaml.Controls` (media primitives via `Rendering.Shared/NativeBitmap` only), Features, Services |
| `Services` | `Core`, `Common`, `Infrastructure`, `Interop` (Translation only), `Rendering` (engines), `UI.Shell` types via `App` indirection where pre-existing | XAML controls (exception: `UpdateService` hosts no controls; `UpdateBanner` moves to `UI/Controls` in Phase 3) |
| `Features/*` | `Rendering` engines, `Services`, `Core`, `Common`, `Infrastructure` | `Interop/*` directly (rule §2.4) |
| `UI/Shell` | `Features`, `Services`, `Core`, `Common`, `Infrastructure` | `Interop/*` directly |

Verified against the current tree: `ViewerPage`/`ToolsPage`/viewers consume only `Rendering.*`; the sole `Interop` consumer outside `Rendering` is `Services/Translation` (sanctioned permanent exception, documented in `Interop/README.md`).

## §2 Native interop contract

1. **One binding file per engine**, `Interop/{Engine}/{Engine}Native.cs`, `[LibraryImport]` only. Verified: 25 pc_ + 14 wc_ + 11 gc_ + 12 dc_ + 5 qv_ declarations, zero engine `DllImport` (RA-07).
2. **Win32 P/Invoke (`DllImport`) is permitted only** in: `Program.cs`, `UI/Shell/TitleBarWindow.xaml.cs`, `Features/Settings/SettingsPage.xaml.cs`, `Services/Input/{GlobalHotkeyRegistration,QuickLookHotkeyService}.cs`, `Services/Registration/AppRegistration.cs`, `Services/Updates/UpdateService.cs`, `tools/EmbedIcon/EmbedIcon.cs`. These are OS-chrome calls (hotkeys, shell, icons, wintrust), not engine FFI.
3. **Ownership:** Rust allocates returned buffers/images/document handles; managed callers copy and release exactly once via the matching `*_free_*` export. Table: `Interop/README.md`. Input buffers are caller-supplied.
4. **No bypassing engines:** pages, viewer controls, and services (except Translation) must not call `Interop/*` — only `Rendering/*` engine classes. `ToolsPage` uses engines directly (sanctioned; no renderer indirection exists anymore).
5. **Serialization:** document-handle calls and disposal serialize through the owning engine's `NativeCallGate`.
6. **Threading:** `WriteableBitmap` construction only via `NativeBitmap` (UI dispatcher, initialized in `App.OnLaunched`); native parse/render work off-thread; results marshaled as RGBA buffers.
7. **Rust:** intact (AF-09). No export, struct, prefix, lockfile, or vendored change. The `translation_ffi` test-string rename (RA-08) is the only native diff since `25d7fa1`.

## §3 Cancellation and lifecycle rules

- One CTS per document load, owned by `ViewerPage`, passed into `LoadAsync`; `OperationCanceledException` is never an error.
- `CleanupAsync` is the single viewer cleanup seam — idempotent, called on document switch and tab close; native handles are freed by the engine objects the viewer owns.
- Pending annotation state flushes (`FlushPendingAsync`) before any new load or close — the deferred-save contract.

## §4 Enforcement

Phase 5 adds `DocGlance.Tests/Architecture/NamespaceDependencyTests.cs` asserting §1 (reference-blacklisting via compiled-assembly reflection or source scan). Violations fail `dotnet test` — deliberate exceptions must be added to this document first.
