# Dependency Strategy for QuickView Native Libraries

## Current Situation

QuickView uses three **external rendering libraries** via FFI:
- **deckcraft** - PPTX (PowerPoint) parsing and rendering
- **gridcraft** - XLSX (Excel) parsing and rendering  
- **wordcraft** - DOCX (Word) parsing and rendering

These are NOT your libraries - they're maintained by the [storytold](https://github.com/storytold) organization.

## The Problem with "Always Latest"

### What We Just Configured (Auto-Update on Every Build)
```toml
deckcraft-pptx = { git = "https://github.com/storytold/deckcraft", branch = "main" }
```
+ `cargo update` before every build

**Risks:**
1. **Breaking Changes** - Upstream can break your FFI interface at any time
2. **Unpredictable Behavior** - Different builds may use different versions
3. **Difficult Debugging** - "It worked yesterday" becomes impossible to diagnose
4. **No Rollback** - If a bad commit breaks your app, you can't easily go back
5. **Build Reproducibility** - Two developers building the same commit get different binaries

## Recommended Strategy: Pin to Tested Commits

### Option 1: Pin to Specific Commits (RECOMMENDED)
```toml
[dependencies]
deckcraft-pptx = { git = "https://github.com/storytold/deckcraft", rev = "688c7ef5" }
```

**Advantages:**
- ✅ Reproducible builds
- ✅ Control over when to update
- ✅ Test updates in isolation
- ✅ Easy rollback if issues arise
- ✅ CI/CD produces consistent artifacts

**Update Process:**
1. Update `Cargo.toml` with new commit hash
2. Test thoroughly in development
3. Commit the change with test results
4. Deploy with confidence

### Option 2: Track Branch with Manual Updates (Current Setup)
```toml
[dependencies]
deckcraft-pptx = { git = "https://github.com/storytold/deckcraft", branch = "main" }
```

**Keep:**
- Leave branch tracking in place
- Remove automatic `cargo update` from build script

**Update Process:**
1. Manually run `cargo update` when you want updates
2. Test the changes
3. Commit `Cargo.lock` with the new hashes
4. Deploy

This gives you control while staying current.

### Option 3: Use Released Versions (IDEAL - if available)
```toml
[dependencies]
deckcraft-pptx = "0.6.1"
```

**Check if these crates are published to crates.io:**
```powershell
cargo search deckcraft-pptx
cargo search gridcraft-core
cargo search wordcraft-docx
```

If they're published, use semantic versioning for stability.

## When to Auto-Update

**Good use cases:**
- Internal libraries you control
- Development/testing environments
- Proof-of-concept projects
- When you're actively contributing to the upstream library

**Bad use cases:**
- Production applications (like QuickView)
- FFI boundaries (ABI stability is critical)
- External dependencies you don't control
- Applications with users who expect stability

## Current State (After Recent Changes)

You're now on **Option 2** with aggressive auto-update:
- Branch: `main` tracking
- Auto-update: ✅ Runs on every build
- Control: ❌ None - gets latest code automatically

## Recommended Changes for QuickView

### 1. Remove Auto-Update from Build Script
The build script should just build, not update:
```powershell
# Remove this line:
cargo update

# Keep only:
cargo build --release
```

### 2. Choose Your Strategy

**For Production Stability (Recommended):**
```toml
# Pin to tested commits
deckcraft-pptx = { git = "https://github.com/storytold/deckcraft", rev = "688c7ef5" }
```

**For Staying Current with Control:**
```toml
# Track main, but update manually
deckcraft-pptx = { git = "https://github.com/storytold/deckcraft", branch = "main" }
```

### 3. Commit Cargo.lock
Always commit `Cargo.lock` to git for applications. This ensures everyone builds with the same dependencies.

## Update Workflow (Recommended)

### Monthly or Quarterly:
1. Create a branch: `git checkout -b update-native-deps`
2. Update dependencies: `cd native/<project> && cargo update`
3. Rebuild: `.\native\build-native.ps1`
4. Test all document types: PPTX, DOCX, XLSX
5. Check for regressions:
   - Rendering quality
   - Search functionality
   - Text extraction
   - Export features
6. If all tests pass: `git commit -am "Update native dependencies"`
7. Merge to main

### On-Demand (Bug Fixes):
1. Check upstream repos for relevant fixes
2. Update to specific commit if needed
3. Test the specific fix
4. Deploy

## Monitoring Upstream Changes

Set up GitHub notifications:
```
Watch → Custom → Releases only
```
For:
- https://github.com/storytold/deckcraft
- https://github.com/storytold/gridcraft
- https://github.com/storytold/wordcraft

## Current Versions (as of 2026-10-12)
- **deckcraft**: v0.6.1 @ `688c7ef5`
- **gridcraft**: v0.6.1 @ `fc45b7f6`
- **wordcraft**: v0.6.1 @ `159b84f5`

All packages are stable at version 0.6.1.

## TL;DR

**What You Asked For:** "Update all Cargo repos and add something every time it builds using latest updates"

**What That Means:** Every build fetches the absolute latest code from GitHub main branches, potentially breaking your app without warning.

**What You Probably Want:** Control over when dependencies update, with testing in between.

**Recommendation:** Remove `cargo update` from build script, commit `Cargo.lock`, and update dependencies manually on a schedule after testing.
