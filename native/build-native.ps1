# QuickView — build all native Rust libraries
# Run from: C:\Code\QuickView\native\
# Requires: rustup (winget install Rustlang.Rustup)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Continue"  # Don't treat stderr as terminating errors

Write-Host ""
Write-Host "  Building deckcraft_ffi.dll (DeckCraft PPTX renderer)..." -ForegroundColor Cyan
Write-Host ""

Push-Location "$PSScriptRoot\deckcraft_ffi"

try {
    # First-time setup: ensure the MSVC target is available (ignore output/errors)
    $null = rustup target add x86_64-pc-windows-msvc 2>&1

    Write-Host "  Running cargo build --release..." -ForegroundColor Gray
    cargo build --release 2>&1 | ForEach-Object { Write-Host "     $_" -ForegroundColor Gray }
    if ($LASTEXITCODE -ne 0) { 
        throw "cargo build failed with exit code $LASTEXITCODE" 
    }

    $dll = "target\release\deckcraft_ffi.dll"
    if (-not (Test-Path $dll)) { throw "DLL not found after build: $dll" }

    # Copy to all known QuickView output locations
    $targets = @(
        "..\..\bin\Debug\net8.0-windows10.0.19041.0\win-x64",
        "..\..\bin\Release\net8.0-windows10.0.19041.0\win-x64",
        "..\..\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\AppX",
        "..\..\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\AppX"
    )

    foreach ($dir in $targets) {
        if (Test-Path $dir) {
            Copy-Item $dll -Destination $dir -Force
            Write-Host "  Copied → $dir" -ForegroundColor Green
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
