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
- [ ] Files deleted · [ ] csproj updated · [ ] Build 0/0 · [ ] Tests 133/133 · [ ] Checklist+findings updated (FIND-031 → Fixed)

## Actual results
_(fill on completion)_
