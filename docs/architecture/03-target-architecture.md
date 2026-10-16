# 03 — Target Architecture (reconciled re-audit 2026-10-16)

The target is the **current tree minus the delta in `10-refactoring-roadmap.md`**. Structure is normative in `tree.md`; this document records the decisions and ownership rules behind it.

## 1. Selected architecture (unchanged in shape, now mostly built)

Smallest architecture that separates responsibilities while preserving working code: one app project + one test project; layering `Features → Rendering → Interop → Rust` with `Services` beside `Features`; a registry + capability contracts for the viewer; no DI container; Rust fully intact.

**Change from the original target (RA-02):** format viewers live in `Features/Viewer/Formats/` (not `Viewers/`) — the implemented name is adopted.

## 2. Ownership decisions

### Viewer boundaries
- **`ViewerPage` (shell coordinator, ~300 LOC):** registry-first dispatch, document lifecycle (load/switch/cancel/close), generic loading/error states, resize debounce, public API consumed by `TitleBarWindow`. It must contain **no format-specific rendering logic** and **no format literals** — the only extension handling is `registry.Resolve(ext)`.
- **`ViewerViewModel` (sole state owner):** page, total, zoom, natural zoom, rotation, fit mode, sidebar, loading/error. Viewers mutate it through `ISupportsZoom`/events; XAML chrome binds to it. No mirrors, no `SyncToViewModel`.
- **Format viewers (`Formats/{Pdf,Docx,Pptx,Spreadsheet,Text,Fallback}`):** each is a self-contained `UserControl` (or code-only control for Fallback) owning its viewport, caches, and format interactions, created by its factory via the registry. Mandatory contract: `IDocumentViewer` (`Control`, `LoadAsync`, `CleanupAsync`). Optional capabilities discovered by the shell via `ISupports{Zoom,Search,TextSelection,Outline,Annotations,Thumbnails}` — a capability absent from a viewer means the shell hides that affordance (toolbar button, sidebar tab, find bar behavior). This is how unsupported capabilities behave; there is no third path.
- **Shared UI (`Components/`):** toolbar, markup toolbar, find bar, dialogs, highlighter, popup style — owned by the shell, driven by contracts + ViewModel bindings.

### Capability contract (per capability: owner / implementer / discovery / cleanup / mandatory-optional)
| Capability | State owner | Implemented by | Shell discovery | Cleanup | Optional? |
|---|---|---|---|---|---|
| Load/Cleanup | ViewerPage lifecycle | all viewers | `IDocumentViewer` (registry) | `CleanupAsync` on tab close + before switch | mandatory |
| Zoom/Fit | ViewerViewModel | Pdf, Docx, Pptx, Spreadsheet | `as ISupportsZoom` | n/a | optional |
| Search | ViewerPage panel (query state); viewer owns hits/highlights | all five | `as ISupportsSearch` | `ClearHighlight` on document switch | optional |
| Text selection | viewer | Pdf, Docx, Pptx, Text, Spreadsheet | `as ISupportsTextSelection` | overlays cleared in `CleanupAsync` | optional |
| Outline | viewer (via engine) | Pdf, Docx, Pptx | `as ISupportsOutline` | n/a | optional |
| Thumbnails | viewer produces; shell hosts | Pdf, Docx, Pptx | `as ISupportsThumbnails` | cache cleared in `CleanupAsync` | optional |
| Annotations | viewer (deferred flush) | Pdf only | `as ISupportsAnnotations` | `FlushPendingAsync` before switch/close | optional |
| Fallback | shell | FallbackViewer (claims `.xls` + unclaimed) | registry fallback | no-op | — |

Selection models stay format-native (PDF text geometry, Office text maps, spreadsheet cells, plain TextBox) — `SearchLocation` is a discriminated record hierarchy precisely so the shell never forces them into one shape.

### Rendering & interop ownership
- Document handles: owned by engine classes (`PdfDocumentState`/`DocxDocument`/`SpreadsheetDocument`/`PptxDocument`); viewer controls may own one engine document, the shell never does (after Phase 2).
- Native allocations: Rust allocates → C# copies → exactly-once free via matching `*_free_*` (table: `Interop/README.md`).
- Serialization: per-engine `NativeCallGate` for handle calls vs. dispose.
- Buffers → pixels: `Rendering/Shared/NativeBitmap` (UI-dispatcher `WriteableBitmap`). **Boundary rule:** Rendering may use WinUI *media* primitives; never XAML controls. XAML belongs to `Features`/`UI`.
- Cancellation: one `CancellationTokenSource` per load owned by ViewerPage, passed into `LoadAsync`; viewport render loops cancel via viewer-internal CTSes tied to the same token.
- Cache/memory: `RenderBudget` caps page-cache bytes inside the owning viewer.
- UI thread: `Infrastructure/Extensions/UIThreadHelper`; `DispatcherQueue.TryEnqueue` for re-activation hops.
- Pages/controls never call `Interop/*` directly — only `Rendering/*` engines and `ToolsPage` (engines directly, sanctioned).

### Shell services ownership
Activation (`Services/Activation`), session (`Services/Session`), hotkeys (`Services/Input`), registration, storage, thumbnails, translation, updates — extracted, TitleBarWindow/App keep call-throughs only. `UpdateBanner` moves to `UI/Controls` (UI does not live in `Services`).

## 3. Lifecycle contract (open → switch → cancel → close)

1. **Open:** `TitleBarWindow.OpenDocument` → `ViewerPage` → `LoadDocumentAsync`: flush pending annotations (`ISupportsAnnotations.FlushPendingAsync`) → cancel previous CTS → cleanup previous viewer (`CleanupAsync`) → `registry.Resolve(ext).Create(ViewModel)` → host control → `LoadAsync(doc, ct)` → show chrome affordances per implemented capabilities.
2. **Switch:** same path; the flush-first rule makes deferred annotation saves safe.
3. **Cancel:** CTS cancellation; viewers treat `OperationCanceledException` as normal.
4. **Close (`CancelLoad`):** cancel CTSes → `CleanupAsync()` every viewer → dispose engine documents → free native handles → `ViewModel.Reset()`. A viewer must never leak a native handle or keep a render loop running on a disposed document — `CleanupAsync` is the single seam, and it must be idempotent.

## 4. Naming and modularity

See doc 04 (naming) and doc 06 (size targets with measured values). Summary: format folders hold engine + viewer + format models; `SPLIT/DISSOLVE` maps are in `tree.md` §2; no file is split for line count alone.

## 5. Enforcement

`DocGlance.Tests/Architecture/NamespaceDependencyTests.cs` (Phase 5) encodes the layer table in doc 08 §1 so violations fail `dotnet test`.
