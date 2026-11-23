# DocGlance — build all native Rust libraries
# Run from: C:\Code\DocGlance\native\
# Requires: rustup (winget install Rustlang.Rustup)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Continue"  # Don't treat stderr as terminating errors

# Ensure MSVC tools and Windows SDK are discoverable by rustc and cc-rs (for VS 2026 / Dev18)
$vsPath = "C:\Program Files\Microsoft Visual Studio\18\Community"
$msvcDir = "$vsPath\VC\Tools\MSVC"
if (Test-Path $msvcDir) {
    $msvcVer = (Get-ChildItem $msvcDir | Sort-Object Name -Descending | Select-Object -First 1).FullName
    $sdkLib = "C:\Program Files (x86)\Windows Kits\10\Lib\10.0.26100.0"

    $env:VSINSTALLDIR = "$vsPath\"
    $env:VCINSTALLDIR = "$vsPath\VC\"
    $env:WindowsSdkDir = "C:\Program Files (x86)\Windows Kits\10\"
    $env:WindowsSDKVersion = "10.0.26100.0\"
    $env:PATH = "$msvcVer\bin\HostX64\x64;$env:PATH"
    $env:LIB = "$msvcVer\lib\x64;$sdkLib\um\x64;$sdkLib\ucrt\x64"
}

$targets = @(
    "..\..\bin\Debug\net8.0-windows10.0.19041.0\win-x64",
    "..\..\bin\Release\net8.0-windows10.0.19041.0\win-x64",
    "..\..\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\AppX",
    "..\..\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\AppX"
)

# ── 1. Build deckcraft_ffi.dll (DeckCraft PPTX renderer) ──────────────────────
Write-Host ""
Write-Host "  Building deckcraft_ffi.dll (DeckCraft PPTX renderer)..." -ForegroundColor Cyan
Write-Host ""

Push-Location "$PSScriptRoot\deckcraft_ffi"
try {
    $null = rustup target add x86_64-pc-windows-msvc 2>&1

    Write-Host "  Running cargo build --release..." -ForegroundColor Gray
    cargo build --release 2>&1 | ForEach-Object { Write-Host "     $_" -ForegroundColor Gray }
    if ($LASTEXITCODE -ne 0) { 
        throw "cargo build failed for deckcraft_ffi with exit code $LASTEXITCODE" 
    }

    $dll = "target\release\deckcraft_ffi.dll"
    if (-not (Test-Path $dll)) { throw "DLL not found after build: $dll" }

    foreach ($dir in $targets) {
        if (Test-Path $dir) {
            Copy-Item $dll -Destination $dir -Force
            Write-Host "  Copied deckcraft_ffi.dll → $dir" -ForegroundColor Green
        }
    }

    $size = [math]::Round((Get-Item $dll).Length / 1MB, 1)
    Write-Host ""
    Write-Host "  ✓ deckcraft_ffi.dll  ($size MB)" -ForegroundColor Green
    Write-Host ""
}
finally {
    Pop-Location
}

# ── 5. Build translation_ffi.dll (offline Firefox Translations engine) ────────
Write-Host ""
Write-Host "  Building translation_ffi.dll (offline translation engine)..." -ForegroundColor Cyan
Write-Host ""

Push-Location "$PSScriptRoot\translation_ffi"
try {
    Write-Host "  Running cargo build --release..." -ForegroundColor Gray
    cargo build --release 2>&1 | ForEach-Object { Write-Host "     $_" -ForegroundColor Gray }
    if ($LASTEXITCODE -ne 0) {
        throw "cargo build failed for translation_ffi with exit code $LASTEXITCODE"
    }

    $dll = "target\release\translation_ffi.dll"
    if (-not (Test-Path $dll)) { throw "DLL not found after build: $dll" }

    foreach ($dir in $targets) {
        if (Test-Path $dir) {
            Copy-Item $dll -Destination $dir -Force
            Write-Host "  Copied translation_ffi.dll -> $dir" -ForegroundColor Green
        }
    }

    $size = [math]::Round((Get-Item $dll).Length / 1MB, 1)
    Write-Host ""
    Write-Host "  ✓ translation_ffi.dll  ($size MB)" -ForegroundColor Green
    Write-Host ""
}
finally {
    Pop-Location
}

# ── 4. Build pdfcraft_ffi.dll (PdfCraft PDF renderer) ────────────────────────
Write-Host ""
Write-Host "  Building pdfcraft_ffi.dll (PdfCraft PDF renderer)..." -ForegroundColor Cyan
Write-Host ""

Push-Location "$PSScriptRoot\pdfcraft_ffi"
try {
    Write-Host "  Running cargo build --release..." -ForegroundColor Gray
    cargo build --release 2>&1 | ForEach-Object { Write-Host "     $_" -ForegroundColor Gray }
    if ($LASTEXITCODE -ne 0) {
        throw "cargo build failed for pdfcraft_ffi with exit code $LASTEXITCODE"
    }

    $dll = "target\release\pdfcraft_ffi.dll"
    if (-not (Test-Path $dll)) { throw "DLL not found after build: $dll" }

    foreach ($dir in $targets) {
        if (Test-Path $dir) {
            Copy-Item $dll -Destination $dir -Force
            Write-Host "  Copied pdfcraft_ffi.dll -> $dir" -ForegroundColor Green
        }
    }

    $size = [math]::Round((Get-Item $dll).Length / 1MB, 1)
    Write-Host ""
    Write-Host "  ✓ pdfcraft_ffi.dll  ($size MB)" -ForegroundColor Green
    Write-Host ""
}
finally {
    Pop-Location
}

# ── 2. Build wordcraft_ffi.dll (WordCraft DOCX renderer) ──────────────────────
Write-Host ""
Write-Host "  Building wordcraft_ffi.dll (WordCraft DOCX renderer)..." -ForegroundColor Cyan
Write-Host ""

Push-Location "$PSScriptRoot\wordcraft_ffi"
try {
    Write-Host "  Running cargo build --release..." -ForegroundColor Gray
    cargo build --release 2>&1 | ForEach-Object { Write-Host "     $_" -ForegroundColor Gray }
    if ($LASTEXITCODE -ne 0) { 
        throw "cargo build failed for wordcraft_ffi with exit code $LASTEXITCODE" 
    }

    $dll = "target\release\wordcraft_ffi.dll"
    if (-not (Test-Path $dll)) { throw "DLL not found after build: $dll" }

    foreach ($dir in $targets) {
        if (Test-Path $dir) {
            Copy-Item $dll -Destination $dir -Force
            Write-Host "  Copied wordcraft_ffi.dll → $dir" -ForegroundColor Green
        }
    }

    $size = [math]::Round((Get-Item $dll).Length / 1MB, 1)
    Write-Host ""
    Write-Host "  ✓ wordcraft_ffi.dll  ($size MB)" -ForegroundColor Green
    Write-Host ""
}
finally {
    Pop-Location
}

# ── 3. Build gridcraft_ffi.dll (GridCraft XLSX renderer) ──────────────────────
Write-Host ""
Write-Host "  Building gridcraft_ffi.dll (GridCraft XLSX renderer)..." -ForegroundColor Cyan
Write-Host ""

Push-Location "$PSScriptRoot\gridcraft_ffi"
try {
    Write-Host "  Running cargo build --release..." -ForegroundColor Gray
    cargo build --release 2>&1 | ForEach-Object { Write-Host "     $_" -ForegroundColor Gray }
    if ($LASTEXITCODE -ne 0) { 
        throw "cargo build failed for gridcraft_ffi with exit code $LASTEXITCODE" 
    }

    $dll = "target\release\gridcraft_ffi.dll"
    if (-not (Test-Path $dll)) { throw "DLL not found after build: $dll" }

    foreach ($dir in $targets) {
        if (Test-Path $dir) {
            Copy-Item $dll -Destination $dir -Force
            Write-Host "  Copied gridcraft_ffi.dll → $dir" -ForegroundColor Green
        }
    }

    $size = [math]::Round((Get-Item $dll).Length / 1MB, 1)
    Write-Host ""
    Write-Host "  ✓ gridcraft_ffi.dll  ($size MB)" -ForegroundColor Green
    Write-Host ""
}
finally {
    Pop-Location
}
