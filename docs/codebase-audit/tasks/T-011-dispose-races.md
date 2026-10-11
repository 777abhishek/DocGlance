# T-011 — Fix Dispose races in the three native document wrappers

- **FIND:** FIND-002 (High) · Prereq: **T-010** (same files, sequenced) · **In-scope:** `Rendering/Document/SpreadsheetDocument.cs`, `Rendering/Document/DocxDocument.cs`, `Rendering/Presentation/PptxDocument.cs` (+ their test files)
- **Current behavior (evidence):** `Dispose()` frees the native handle and disposes the `SemaphoreSlim` without acquiring it. `RenderPageAsync`/`RenderSlideAsync`/`ExportToPdfAsync`/`GetSheetDataJsonAsync` check `_disposed` before awaiting the semaphore, then use `_handle` inside `Task.Run` — a concurrent `Dispose` frees the handle mid-native-call (UAF) and the `finally`-`Release()` throws `ObjectDisposedException`.

## Steps (identical pattern ×3)
1. `Dispose()`: `if (_disposed) return; _renderLock.Wait(); try { _disposed = true; free native handle; _handle = default; } finally { _renderLock.Release(); }` — **do not** dispose the semaphore (process-lifetime for a transient doc object is fine; finalizer not needed since nothing but the semaphore is managed-IDisposable at that point — if kept, dispose it only after Wait/Release completes and document the ordering).
2. Move the `ObjectDisposedException.ThrowIf` checks inside the semaphore-held region in every public method so `_disposed` is stable for the call duration.
3. `Dispose(bool)`/`IDisposable` shape stays simple; mark class `sealed` (already sealed).
4. Rust side unchanged this task (scale clamp is T-013).

## Verification
- New test per wrapper (skip-fact when `*.IsAvailable == false`): loop 100× { start render on big fixture; `Dispose()` after random 0–50 ms }; assert no unhandled exception and process survives. Uses the `PdfViewerServiceTests` native-DLL pattern.
- Suite green; build 0/0.

## Acceptance criteria
Concurrent dispose/render test passes deterministically (10 consecutive runs); no wrapper frees a handle while its semaphore is held elsewhere.

## Regression risks
`Dispose` now blocks up to one render duration (bounded, ≤~1 s typical, export longer) — acceptable at teardown; note in code comment. If a caller Dispose-storms, Wait is reentrant-safe by the `_disposed` guard.

## Rollback
`git revert` per-wrapper commit.

## Completion checklist
- [ ] 3 wrappers patched · [ ] 3 race tests added & passing ×10 · [ ] FIND-002 → Fixed

## Actual results
_(fill on completion)_
