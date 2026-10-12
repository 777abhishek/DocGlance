# QuickView Native FFI Libraries

## Overview

QuickView uses three Rust FFI libraries to render Office documents:

- **deckcraft_ffi** - PowerPoint (PPTX) rendering, text extraction, search
- **gridcraft_ffi** - Excel (XLSX) rendering, text extraction, search  
- **wordcraft_ffi** - Word (DOCX) rendering, text extraction, search

## Building

```powershell
.\build-native.ps1
```

This builds all three DLLs and copies them to the appropriate bin directories.

## Current Versions (Stable)

As of 2026-10-12, all libraries are at **v0.6.1** and working correctly:
- **deckcraft**: commit `688c7ef5`
- **gridcraft**: commit `fc45b7f6`
- **wordcraft**: commit `159b84f5`

## When to Update Dependencies

**Only update if:**
- ❌ There's a critical bug affecting your users
- ❌ You need a specific new feature
- ❌ Security vulnerability announced

**Do NOT update:**
- ✅ "Just to stay current" - if it works, don't touch it
- ✅ Automatically on every build - risks breaking production

## How to Update (When Needed)

### 1. Update All Dependencies

```powershell
# Update all three projects
cd native\deckcraft_ffi
cargo update

cd ..\gridcraft_ffi  
cargo update

cd ..\wordcraft_ffi
cargo update
```

### 2. Rebuild and Test

```powershell
cd ..
.\build-native.ps1
```

### 3. Test Document Rendering

Test with various documents:
- **PPTX**: Complex slides, text extraction, search
- **XLSX**: Large spreadsheets, formulas, search
- **DOCX**: Formatted documents, tables, search

### 4. Commit Changes

```powershell
git add native\*/Cargo.lock
git commit -m "Update native dependencies to v0.x.x"
```

## Manual Update to Specific Version

If you want to update to a specific commit:

```toml
# In Cargo.toml, change from:
deckcraft-pptx = { git = "https://github.com/storytold/deckcraft", branch = "main" }

# To:
deckcraft-pptx = { git = "https://github.com/storytold/deckcraft", rev = "abc12345" }
```

Then run `cargo build`.

## Dependency Philosophy

These are **external rendering libraries**. We use them for:
- Parsing complex Office file formats
- Rendering to pixels
- Extracting text
- Search functionality

**We don't need bleeding edge updates** - we need stability and predictability.

Think of them like system libraries: update carefully, test thoroughly, and only when necessary.

## Troubleshooting

### Build fails after update
```powershell
# Clean and rebuild
cd native\deckcraft_ffi
cargo clean
cargo build --release
```

### Revert to last known good version
```powershell
git checkout Cargo.lock
cargo build --release
```

## References

- [DeckCraft](https://github.com/storytold/deckcraft) - PPTX renderer
- [GridCraft](https://github.com/storytold/gridcraft) - XLSX engine
- [WordCraft](https://github.com/storytold/wordcraft) - DOCX layout engine
