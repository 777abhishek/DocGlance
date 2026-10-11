# T-002 — Add solution file, CI workflow, and root README

- **FIND:** FIND-033, FIND-037
- **Goal:** make "clone → build → test" a two-command experience and enforce green on every push.

## Steps
1. `dotnet new sln -n QuickView` at repo root; `dotnet sln add QuickView.csproj QuickView.Tests/QuickView.Tests.csproj`.
2. Build via the sln: `dotnet build QuickView.sln -c Debug` → 0/0. (Watch for the `GenerateAssemblyInfo=false` interplay — none expected.)
3. Create `.github/workflows/ci.yml` (windows-latest):
   - `dotnet build QuickView.sln -c Debug` (treat warnings as errors via `-p:TreatWarningsAsErrors=true` **only if** currently 0-warning — verified baseline says yes).
   - `dotnet test QuickView.Tests/QuickView.Tests.csproj --nologo`.
   - Rust job: for each of `native/deckcraft_ffi`, `native/wordcraft_ffi`, `native/gridcraft_ffi`: `cargo check --locked`.
4. Root `README.md`: what QuickView is; prerequisites (.NET SDK 8+ (SDK 10 used locally), rustup, Windows 10 19041+ SDK); build (`build.ps1`, packaged/unpackaged flags); native libs (`native/build-native.ps1`); tests; link to `docs/codebase-audit/README.md`.

## Verification
- Local: sln build/test green.
- CI: push branch, confirm green run (or `act` if unavailable — then record "CI unverified locally" honestly).

## Acceptance criteria
Sln builds+tests green locally; workflow file passes YAML lint; README contains working commands (copy-paste verified).

## Out of scope
Release/publish jobs, installer CI, cargo build --release in CI.

## Rollback
`git revert`; sln/workflow/README are additive.

## Completion checklist
- [ ] sln added · [ ] CI green or honestly marked · [ ] README commands verified · [ ] FIND-033/037 → Fixed

## Actual results
_(fill on completion)_
