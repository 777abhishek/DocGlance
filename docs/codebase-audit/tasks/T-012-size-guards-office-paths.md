# T-012 — File-size guards on XLSX/PPTX load paths

- **FIND:** FIND-023 · Prereq: none · **In-scope:** `Features/Viewer/Partials/ViewerPage.Pdf.cs` (`LoadPptxAsync`, `LoadXlsxAsync`)
- **Evidence:** Neither loader validates before `*Document.OpenAsync` → `File.ReadAllBytesAsync` unbounded. PDF/text/activation paths all guard via `FileSizeValidator.IsFileValidAndSafe`.

## Steps
1. First line of each loader: `if (!FileSizeValidator.IsFileValidAndSafe(filePath, out var err)) { ShowErrorCard(err); return; }`.
2. Confirm `AppSettings` has sane caps for `.xlsx/.xlsm/.xltx/.xltm/.pptx/.ppt/.pptm/.ppsx` (extend the switch in `FileSizeValidator.GetMaxSizeForExtension` if any listed ext falls to default).
3. Tests: unit test that an oversized temp fixture (over cap for its extension) fails validation (existing `FileSizeValidatorTests` pattern).

## Verification
Build 0/0; suite green; manual: dummy 3 GB `.xlsx` (fsutil sparse) → error card, memory flat.

## Acceptance criteria
Every native-load path validates size; test proves the guard; no user-visible change for normal files.

## Rollback
Revert single commit.

## Completion checklist
- [ ] Guards in both loaders · [ ] Extension caps complete · [ ] Test · [ ] FIND-023 → Fixed

## Actual results
_(fill on completion)_
