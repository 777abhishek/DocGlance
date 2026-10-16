# 08 — Dependency and Interoperability Rules

These rules describe the target state (doc 03/05) and are enforced after migration by `DocGlance.Tests/Architecture/NamespaceDependencyTests.cs`.

## 1. Layer order (may depend only downward)

```
Program / App                     (boot, wiring)
  → UI (shell, shared controls, styles)
    → Features (pages, viewers, components, contracts)
      → Services (app services)
        → Rendering (engines, Shared)
          → Interop (FFI bindings)
            → native DLLs (Rust)
Core, Common, Infrastructure      (referenced by any layer above Interop)
```

Concrete allowed references:

| From | May reference | Must NOT reference |
|---|---|---|
| `UI.*` | `Core`, `Common`, `Infrastructure`, `Services` (for control data, e.g. UpdateBanner→UpdateService), `Features` *(only shell hosting pages)* | `Rendering`, `Interop` |
| `Features.*` (pages/viewers) | `Core`, `Common`, `Infrastructure`, `Services`, `Rendering` | `Interop` (engines only), Win32 P/Invoke outside sanctioned files |
| `Features/Viewer/Viewers/*` | own format's `Rendering/<Format>`, shared Rendering, Contracts | other formats' viewers |
| `Services.*` | `Core`, `Common`, `Infrastructure`, `Rendering`; `Interop/Translation/*` — **permanent sanctioned exception, consumed only by `Services/Translation`** (translation is an engine without a document format; no wrapper class invented — AF-05) | `Microsoft.UI.Xaml.Controls` (controls — `UpdateBanner` moves out; goal zero); any Interop folder other than Translation |
| `Rendering.*` | `Core`, `Common`, `Infrastructure`, `Interop`, own format folder; **WinUI *media/imaging* primitives (`WriteableBitmap`) and `DispatcherQueue` are permitted** for pixel handoff (AF-02) | `Features`, `UI`, `Services`, `Microsoft.UI.Xaml.Controls` (XAML *controls*) |
| `Interop.*` | nothing but `System.Runtime.InteropServices` and BCL | everything else |
| `Core`, `Common` | BCL only | everything app-side (zero WinUI references — current `IFileRenderer` UIElement dependency dies with it; `UIThreadHelper` moves to Infrastructure) |

Two sanctioned exceptions, documented: `TitleBarWindow` constructs/pages reference `Features.*` pages (it is the shell host); `ToolsPage` references `Rendering.<Format>` engines directly (convert/merge/split are engine operations, no viewer involved).

## 2. C# ↔ Rust boundary rules

1. **One binding file per crate** in `Interop/`, `internal static partial`, `[LibraryImport]` only (migrate `PdfCraftNative`'s 25 `[DllImport]`s). `AllowUnsafeBlocks` stays scoped to Interop/NativeBitmap.
2. **Only engine classes (`Rendering/<Format>/*Document`, `PdfRenderer`, translation service) call Interop.** No page, viewer, or service calls `pc_/wc_/gc_/dc_/qv_` functions directly.
3. **Ownership contract** (to be restated in `Interop/README.md`, from the existing per-file docs): Rust allocates every returned buffer/document/image; C# must call the matching `<prefix>_free_*` export exactly once; handles are opaque `readonly struct`s; structs mirrored with `[StructLayout(LayoutKind.Sequential)]` and layout changes require synchronized commits both sides.
4. **Threading**: every native call that touches a document handle goes through the owning engine's gate (`NativeCallGate` pattern); `Dispose` acquires the same gate — this is the fix-class for the use-after-free documented in `docs/reliability/REPOSITORY_AUDIT.md` D1.
5. **Errors**: Rust `catch_unwind` returns null/zero/sentinel; engines convert to `InvalidOperationException("<Engine> <op> failed: <status>")`; viewers convert to error-card state; unknown errors logged via `Logger` with engine context. No error strings invented in bindings.
6. **Buffers**: pixel buffers copied to managed memory on a worker thread, handed to `NativeBitmap` on the UI dispatcher, freed immediately after copy — the existing `RenderPagesAsync` pattern, kept.
7. **String marshalling**: UTF-8 in and out, length-prefixed where the ABI requires; binding comments carry each crate's convention.
8. Prefixes: `pc_/wc_/gc_/dc_/qv_` all stay as-is (AF-09 — Rust intact). No new wrappers, no C++/CX, no CsWinRT projections — the flat C ABI is the entire interop surface.

## 3. XAML/resource rules

- Pages/controls may reference merged dictionaries in `UI/Styles|Icons|Templates` and `Microsoft.UI.Xaml.Controls` only.
- `ms-appx:///` resource URIs change only when a file moves; moves update URIs in the same commit (checklist in doc 10 Phase 6).
- Code-behind may not contain: FFI calls, file-index/EF access, update-check logic — events to ViewModel/services only (existing clean pages already comply; ViewerPage is the migration target).

## 4. State and messaging rules

- Viewer state (page/zoom/rotation/fit/sidebar/loading/error) lives **only** in `ViewerViewModel`; viewers receive and report via contracts — no private field mirrors (F3).
- Cross-window/cross-tab communication stays: static roots (`TitleBarWindow.Current`, `App.AllWindows`) are permitted legacy roots; new cross-component signals go through services or the registry, not new statics.
- Settings changes: subscribe `AppSettings.SettingsChanged`; no direct file reads outside `SettingsStore`/stores.

## 5. Prohibited dependency examples (current violations these rules retire)

| Violation today | Rule broken | Fixed by |
|---|---|---|
| `Rendering/Office/*Renderer` returns `UIElement` | Rendering→UI | deletion (F4) |
| `Rendering/Presentation/PptxViewerControl` | Rendering→UI | move to `Features/Viewer/Viewers/Pptx` (F6) |
| `Services/Updates/UpdateBanner.xaml` | Services→XAML | move to `UI/Controls` (F8) |
| `Services/Documents/IPdfViewerService` returns `WriteableBitmap` | Services→WinUI types | moves to `Rendering/Pdf`, where WinUI *media* types are permitted by the AF-02 boundary |
| `Core/Interfaces/IFileRenderer` references `UIElement` | Core→WinUI | deleted with pipeline |
| `Core/Extensions/UIThreadHelper` in Core | Core→WinUI | moves to `Infrastructure` |
| `ToolsPage` code-behind calls engines | Features→Rendering | **allowed exception** (documented) |
