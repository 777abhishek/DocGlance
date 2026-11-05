# 01 — Repository Inventory (verified 2026-10-16, commit `10db6a8`, clean tree)

Full evidence per file lives in `09-file-migration-map.md`; this document is the area-level inventory with measured sizes. "App source" = compiled into `DocGlance.csproj` (excludes `tools/`, `DocGlance.Tests/`, `native/`).

## Root (11 files)
`DocGlance.csproj`, `Program.cs` (232), `App.xaml` (22) + `App.xaml.cs` (208), `Package.appxmanifest`, `app.manifest`, `.gitignore`, `build.ps1`, `build-installer.ps1`, `README.md`, `LICENSE`.

## App source: 150 files, 28,766 LOC

| Area | Files | LOC | Notes |
|---|---|---|---|
| `Common/` (formatters, validators, detectors) | 5 | 495 | pure helpers, no UI |
| `Core/` (constants, enums, interfaces, models) | 11 | 434 | PDF models already relocated to `Rendering/Pdf/Models` |
| `Infrastructure/` (concurrency, config, diagnostics, logging, extensions) | 8 | 1,218 | `UIThreadHelper` moved here from Core |
| `Interop/` | 5 + README | 835 | all `[LibraryImport]`; see doc 08 |
| `Rendering/` (Pdf 14, Docx 1, Xlsx 2, Pptx 1, Shared 3, Code 1) | 22 | 3,004 | `Code/CodeRenderer` is a deletion candidate |
| `Features/Home` | 3 | 1,419 | 652 of it is HomePage.xaml markup |
| `Features/Tools` | 2 | 1,767 | calls engines directly — sanctioned |
| `Features/Settings` | 2 | 1,061 | — |
| `Features/Viewer` shell + registry + contracts | 11 | 1,043 | ViewerPage.xaml 501, .xaml.cs 352, VM 131, registry 58, contracts ×8 |
| `Features/Viewer/Partials` | 13 | 6,104 | the remaining refactor surface — see tree §2.1 |
| `Features/Viewer/Components` | 9 (+3 XAML) | 1,691 | incl. new PdfMarkupToolbar + ViewerToolbarIcons |
| `Features/Viewer/Formats` | 7 | 2,111 | Text 231, Spreadsheet 1,479, Pptx 273, Fallback 115 |
| `Services/` (activation, fs, input, registration, session, storage, thumbnails, translation, updates) | 31 | 4,861 | UpdateBanner (101) slated to move to `UI/Controls` |
| `UI/` (shell, controls, icons, templates, styles) | 11 | 2,058 | incl. deletion candidate PageThumbnailSidebar (180) |
| `Properties/AssemblyInfo.cs` | 1 | 16 | — |

Largest files: SpreadsheetViewer.xaml.cs 1,382 · ViewerPage.Pdf.cs 1,365 · ToolsPage.xaml.cs 1,176 · TitleBarWindow.xaml.cs 1,052 · ViewerPage.PdfAnnotations.cs 859 · ViewerPage.PdfTextSelection.cs 730 · HomePage.xaml 652 · ViewerPage.Viewport.cs 642 · UpdateService.cs 600.

## Tests: 17 files, 2,146 LOC
Mirroring layout post-`10db6a8`: `Common/` ×4, `Core/Constants` ×1, `Features/Home` ×1, `Features/Viewer` ×2 (ViewModel 351, Registry 71), `Infrastructure/` ×2, `Rendering/Docx` ×1, `Rendering/Pdf` ×3, `Services/Translation` ×3.

## Build/dev tools (excluded from app compilation)
`tools/EmbedIcon/` (cs + csproj) — PE icon embedder; `tools/imggen/` (Program.cs + csproj) — asset generator.

## Native (INTACT)
5 crates under `native/`: `pdfcraft_ffi` (lib.rs 2,184; pc_ ×27; `vendor/` = third-party hayro/lopdf, 4.1 MB), `wordcraft_ffi` (895; wc_ ×14), `gridcraft_ffi` (746; gc_ ×11), `deckcraft_ffi` (575; dc_ ×12), `translation_ffi` (584; qv_ ×5). Each has committed `Cargo.toml` + `Cargo.lock`. Only change since baseline `25d7fa1`: test temp-dir string rename in `translation_ffi` (RA-08).

## Assets, installer, docs
`Assets/`: AppIcon.ico, AppIconNew.png (source artifact), Preview.jpg, BadgeLogo/SmallTile/SplashScreen ladders (+scale variants), Square44x44/71x71/150x150/310x310/Wide310x150/StoreLogo sets, `FileTypeIcons/` (12 .ico+.png pairs), `Fonts/FluentSystemIcons-Regular.ttf`. `installer/`: Inno script, Winget manifest generator, assets, local-only generated `output/`. `docs/`: this `architecture/` set; `offline-translation.md` deleted by user decision (recoverable from history).

## Not repository content (untracked/local)
Local private signing bundles, local `.env` files, build logs, `bin/`, `obj/`, `native/*/target/`, `installer/output/`, and `tools/*/obj/`.

## Classification
Maintained app source (150) · tests (17) · build tools (6) · docs (14) · assets/resources (`Assets/`, `installer/assets/`) · third-party (`native/pdfcraft_ffi/vendor/`) · generated (`installer/winget/`) · build outputs (untracked list above). No vendored .NET dependencies; NuGet packages: EFCore.Sqlite 8.0.11, WindowsAppSDK 2.4.0, Win2D 1.4.0, CommunityToolkit.Mvvm 8.4.2, System.Security.Permissions 8.0.0.
