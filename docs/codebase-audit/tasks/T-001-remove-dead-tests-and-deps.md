# T-001 — Remove dead tests and unused Markdig dependency

- **FIND:** FIND-031, FIND-014 (Markdig part)
- **Goal:** stop 15 false-confidence tests from masking the (real) coverage gap; drop an unused package.
- **Current behavior (evidence):** `QuickView.Tests/Rendering/Web/WebSecurityTests.cs` re-implements a regex "from MarkdownRenderer" — that class no longer exists anywhere in source. `QuickView.csproj:65` references `Markdig 1.4.0` with zero `using Markdig` in the tree.

## Steps
1. `git status` — clean tree required.
2. Delete `QuickView.Tests/Rendering/Web/WebSecurityTests.cs` (and the now-empty `Rendering/Web` test dir).
3. Remove the `Markdig` `PackageReference` line from `QuickView.csproj`.
4. `grep -rn "Markdig\|WebSecurityTests" --include=*.cs --include=*.csproj .` (excluding bin/obj) → expect only doc/comment mentions; clean the two stale comments (`App.xaml.cs:142` "HTML and Markdown removed…", `IFileRenderer.cs` CleanupAsync doc mentioning WebView2) **only if** in-scope files allow — these two comment fixes are in-scope here.
5. Build + test.

## Verification
- `dotnet build QuickView.csproj -c Debug` → 0 err / 0 warn.
- `dotnet test QuickView.Tests/QuickView.Tests.csproj` → **133 passed** (148 − 15), 0 failed.

## Acceptance criteria
Suite green at 133; grep step 4 clean; no other files changed.

## Out of scope
Any new tests (T-040), any other dead code (T-022).

## Rollback
Single commit; `git revert <sha>`.

## Completion checklist
- [x] Files deleted · [x] csproj updated · [x] Build 0/0 · [x] Tests 133/133 · [x] Checklist+findings updated (FIND-031 → Fixed)

## Actual results
**Completed: 2026-10-11**

✅ All steps completed successfully
- Deleted `QuickView.Tests/Rendering/Web/WebSecurityTests.cs` and empty parent directories
- Removed Markdig package reference from QuickView.csproj line 65
- Cleaned up stale comment in App.xaml.cs:142 about "HTML and Markdown removed"
- IFileRenderer.cs comment checked — mentions WebView2 as example only, left unchanged
- Build: 0 errors / 0 warnings (110.1s)
- Tests: 133/133 passed (3.5s)
- Commit: 538fcb9 "T-001: Remove dead WebSecurityTests and unused Markdig package"

**Findings resolved:**
- FIND-031: Fixed — 15 false-confidence tests removed
- FIND-014 (Markdig part): Fixed — unused package removed
