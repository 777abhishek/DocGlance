# 07 — Shared Component and Service Matrix

Outcome vocabulary: **S+S** = shared UI *and* shared logic · **S/F** = shared UI, format-specific logic · **Contract** = shared contract, multiple implementations · **Format** = fully format-specific · **N/A** = not applicable today.

| Capability | Outcome | Shared side (owner) | Format-specific side | Decision basis (evidence) |
|---|---|---|---|---|
| File open / activation / IPC | S+S | `Program.cs`, `ActivationService`, `ActivationIpc`, `ActivationPaths` | — | Already shared; only location changes |
| File validation | S+S | `Common/Validators` | — | Used on activation path; extend to drag-drop/picker later (noted, not a bug fix) |
| Format ID + viewer selection | Contract | `DocumentViewerRegistry` + `IDocumentViewerFactory` | each factory declares extensions | Replaces if/else (`ViewerPage.Pdf.cs:120`) and dead `RendererRegistry` (F4) |
| Document lifecycle | Contract | ViewerPage calls `LoadAsync/CleanupAsync` | each viewer/engine frees its handles | Today `CancelLoad` hand-codes per-format teardown (`ViewerPage.xaml.cs:207-253`) |
| Toolbar | S/F | `ViewerToolbar` visual + `ViewerPage.Toolbar.cs` wiring | capability checks decide visibility (PDF markup tools only when `ISupportsAnnotations`) | Toolbar already shared; its imperative setters get replaced by ViewModel state |
| Zoom / fit | S/F | `ViewerViewModel` state + toolbar; `ISupportsZoom` | per-viewer math (page images vs grid scaling) | Two zoom engines today bridged by events (`ViewerPage.xaml.cs:81-89`) |
| Page/slide/sheet navigation | S/F | page indicator + `ViewerViewModel.CurrentPage` | unit semantics stay per-viewer; **no unified page model** | Formats' "pages" differ fundamentally (pdf page ≠ worksheet) |
| Outline / TOC | Contract | sidebar tree UI (shell) | `ISupportsOutline` (Pdf, Docx, Pptx) | `ViewerPage.Sidebar.cs` already hosts tree; loaders are per-format |
| Thumbnails sidebar | S/F | sidebar host + `PageThumbnailItem` | `ISupportsThumbnails` (Pdf, Docx, Pptx render their own) | `LoadDocxThumbnailsAsync`/`LoadSlideThumbnailsAsync` in Pdf.cs partial are per-format |
| Search | S/F | search panel UI + next/prev + `ISupportsSearch` | per-engine `*_search_text` + highlighter (PDF overlay vs text runs vs grid cells) | `ViewerPage.Search.cs` branches per format today — execution moves to viewers |
| Text selection + copy | Contract | copy command, context menu, clipboard | `ISupportsTextSelection` (3 real implementations: PDF rects, Office runs, cell ranges) | Explicitly *not* merged — three genuinely different models (doc 03 §8) |
| Annotations | Format (PDF) + shared toolbar entry | toolbar tools | `ISupportsAnnotations` — PDF only via `PdfAnnotationLayer` | No other format has annotation support; don't force it |
| Translation | S+F | `TranslationDialog` launch + selected-text source via `ISupportsTextSelection` | translation engine (`Services/Translation` + `translation_ffi`) untouched | Dialog already format-agnostic |
| Presentation mode | Format (PPTX) | shell forwards F5 | `PptxViewer` | `ViewerPage.Presentation.cs` |
| Rendering host integration | Contract | `NativeBitmap`, `RenderBudget` (Rendering/Shared) | each viewer's render loop | NativeBitmap already shared correctly |
| Unsupported-format state | S+S | `FallbackViewer` info card | — | Absorbs `ViewerInfoCard` + `ShowInfoCard` |
| Loading / error states | S+S | `ViewerViewModel.IsLoading/ErrorMessage` + overlays | — | Exists; keeps |
| Document metadata card | S+S | `DocumentInfoDialog` | — | Exists, shared |
| Settings / theme | S+S | `AppSettings`/`SettingsStore`, `App.SetTheme`, style dictionaries | — | Keep; `TranslationSettings` pattern documented as the second sanctioned per-feature settings file |
| Status bar / view state | S+S | `ViewerViewModel` + toolbar labels | — | Today split between fields+VM (F3) → VM only |
| Home / recents / index | S+S | `Home*`, `FileIndex*`, `Thumbnail*` | — | Audit-clean |
| Tools (convert/merge/split) | S+S (page) | `ToolsPage` calls engines directly | engine methods already exposed (`wc_export_to_pdf`, `pc_merge_pdfs`) | No new abstraction needed |
| Updates | S+S | `UpdateService` + `UpdateBanner` (moved to UI) | — | Only location fix (F8) |
| Quick Look hotkey | S+S | `QuickLookHotkeyService` + `GlobalHotkeyRegistration` | — | Extracted from TitleBarWindow (F7) |
| Session restore | S+S | `SessionService` + `SessionStateStore` | viewer state captured via `ISupports*` getters | Extracted from TitleBarWindow |
| Logging / diagnostics | S+S | `Infrastructure` | — | Audit-clean, tested |
| Clipboard | S+S | within `ISupportsTextSelection` implementations + shared command | — | `Windows.ApplicationModel.DataTransfer` used uniformly |

## Ownership summary after restructuring

- **Shell owns**: chrome, toolbar visuals, sidebar/search/translation/dialog hosting, state (`ViewerViewModel`), keyboard routing, registry selection.
- **Viewers own**: rendering loops, selection models, zoom math, format search execution, native document lifetime.
- **Engines own**: FFI calls, handle safety (gates), format parsing/rendering primitives.
- **Services own**: activation, indexing, thumbnails, storage, translation engine mgmt, updates, session, hotkey.
- Deliberately **not shared**: annotation model (PDF), sheet data model (XLSX), slide text maps (PPTX), pagination layout (DOCX) — per doc 03 principle 7.
