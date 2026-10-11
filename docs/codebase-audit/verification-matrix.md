# QuickView — Verification Matrix (2026-10-11)

Maps features/risks → the check that verifies them → status of that check **as actually executed**. "Not run" means exactly that; nothing here is assumed.

## Baseline (executed 2026-10-11 @ `40cdfcd`)

| Check | Command | Expected | Actual |
|---|---|---|---|
| Build | `dotnet build QuickView.csproj -c Debug` | 0 err / 0 warn | ✅ 0 / 0 (37 s) |
| Tests | `dotnet test QuickView.Tests/QuickView.Tests.csproj` | all pass | ✅ 148/148, 1 s |
| Rust | `cargo check` in each of `native/{deckcraft,wordcraft,gridcraft}_ffi` | clean | ✅ exit 0 ×3 |
| Packaging build | `build.ps1` full run incl. MSIX + launch | app launches | ⬜ Not run (launches interactive app; deferred to Phase G) |

## Findings → verification

| FIND | Verification method | Status |
|---|---|---|
| 001 native leaks | Tab-churn soak (20 tabs open/close) + document counter/memory | ⬜ Not run (test doesn't exist yet — T-010) |
| 002 dispose races | Concurrent dispose/render loop ×100 w/ native DLLs | ⬜ Not run (T-011) |
| 003 exception policy | Log inspection on forced XAML fault | ⬜ Not run |
| 004 buffer-page race | Stress-scroll on synthetic 500-page PDF + log check | ⬜ Not reproduced |
| 005 thumbnail DPI | Unit: `round(ptW·dpi/72)` + visual check | ⬜ Not run (T-013) |
| 006 FS_RECTF | Struct-vs-header review (done ✅ mismatch confirmed); post-fix visual matrix on annotated PDF + links | ⚠️ Mismatch verified; visual impact unverified |
| 007 FFI truncation | >20k-match DOCX fixture (blocked by FIND-011 wiring) / XLSX 5000-cap fixture | ⬜ Not run |
| 011 DOCX dead path | Dispatch test `.docx` → renderer type | ⬜ Not run (static grep evidence only) |
| 014 dead code | Post-removal build+test+grep | ⬜ Not run (T-001/022) |
| 018 git deps | `cargo update --dry-run` shows no movement after rev-pins | ⬜ Not run |
| 023 size guards | Oversize `.xlsx`/`.pptx` fixtures → error card | ⬜ Not run (T-012) |
| 027 format churn | Alloc-rate during grid scroll before/after | ⬜ Not run |
| 031 dead tests | Delete + suite green | ⬜ Not run (T-001) |
| 032 FFI tests | New suite ≥25 facts green incl. DLL-absent skip path | ⬜ Not run (T-040) |
| 033 CI | Green pipeline on branch | ⬜ Not run (T-002) |

## Feature regression matrix (Phase G gate — all ⬜ Not run)

| Feature | Manual check |
|---|---|
| PDF open/scroll/zoom/rotate/search/links/outline/thumbnails | matrix incl. password-protected + corrupt file |
| XLSX open, sheet switch, search, grid/style render | incl. merged cells, hidden rows/cols |
| PPTX open, slide scroll, thumbnails | widescreen + 4:3 decks |
| TXT/MD/code open, search, syntax highlight | incl. >cap file shows truncation notice |
| `.docx` / legacy `.doc/.xls/.ppt/.csv` | info card (until T-020 changes it) |
| Tabs: open/switch/close/tear-out-disabled/reorder, multi-window | no crash, memory returns to baseline |
| Updater: decline + verify-failure paths | no install on bad hash |
| Settings/theme/recent/bookmarks persistence | across restart |
| File associations (packaged + unpackaged HKCU) | double-click opens in existing instance |
