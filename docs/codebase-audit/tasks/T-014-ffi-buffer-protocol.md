# T-014 — FFI result-buffer truncation (search/extract)

- **FIND:** FIND-007 · Prereq: none (DOCX part untestable until D-04; XLSX part testable now) · **In-scope:** `native/gridcraft_ffi/src/lib.rs`, `native/wordcraft_ffi/src/lib.rs`, `Rendering/Document/{SpreadsheetDocument,DocxDocument}.cs`
- **Evidence:** Fixed C# buffers (64 KB DOCX search, 128/256 KB XLSX) vs Rust caps (20 000 / 5 000 matches); Rust returns `min(len, buf_len)` — C# can't detect truncation; worst case ~800 KB result string.

## Steps — choose ONE protocol, implement uniformly:
- **Option 1 (recommended, two-call length protocol):** each `*_search_text`/`*_extract_*text` returns the *required* length when called with `buf == null`; C# calls once for size, allocates, calls again. Rust change is mechanical; ABI additive (no signature change).
- **Option 2 (interim):** raise C# buffers to 1 MB and lower Rust match caps so worst-case fits with margin; add a `truncated` flag byte at the end of the result (sentinel `;…` marker) so C# can warn.
2. Update `SheetDataJsonTests`-successor tests: fixture with >cap matches asserts exact capped count and no partial final record.
3. `cargo check` ×3; build/test green.

## Acceptance criteria
Truncation is either impossible (Option 1) or detected-and-reported (Option 2) for both XLSX and DOCX search; tests prove it.

## Rollback
Revert Rust + C# sides together (single commit or paired commits); never mixed partially.

## Completion checklist
- [ ] Protocol chosen & logged in decision-log · [ ] Both crates updated · [ ] C# callers updated · [ ] Truncation tests · [ ] FIND-007 → Fixed

## Actual results
_(fill on completion)_
