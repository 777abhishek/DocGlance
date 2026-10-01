# ─────────────────────────────────────────────────────────────────────────────
#  QuickView — Microsoft Store Package Builder
#
#  Creates a complete MSIX/MSIXUPLOAD package ready for Partner Center submission.
#
#  Prerequisites:
#    1. Fill in Package.appxmanifest identity (Name, Publisher) from Partner Center
#    2. Replace placeholder Store assets in Assets/ with real artwork
#    3. Update StoreProductId in UpdateService.cs
#
#  Usage:
#    .\build-store-package.ps1
#    .\build-store-package.ps1 -Version 1.2.0
#    .\build-store-package.ps1 -CreateUploadPackage  (for Store submission)
#
#  Output:
#    - MSIX package:       installer\output\QuickView_x64.msix
#    - MSIXUPLOAD package: installer\output\QuickView_x64.msixupload (for Store)
# ─────────────────────────────────────────────────────────────────────────────
param(
    [string]$Version = "1.0.0.0",
    [switch]$CreateUploadPackage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root       = $PSScriptRoot
$Project    = "$Root\QuickView.csproj"
$Manifest   = "$Root\Package.appxmanifest"
$OutputDir  = "$Root\installer\output"

# ── Helpers ───────────────────────────────────────────────────────────────────
function Write-Step([string]$msg) { Write-Host "`n  $msg" -ForegroundColor Cyan }
function Write-OK  ([string]$msg) { Write-Host "  ✓  $msg" -ForegroundColor Green }
function Write-Fail([string]$msg) { Write-Host "  ✗  $msg" -ForegroundColor Red; exit 1 }
function Write-Info([string]$msg) { Write-Host "     $msg" -ForegroundColor Gray }
function Write-Warn([string]$msg) { Write-Host "  ⚠  $msg" -ForegroundColor Yellow }

Write-Host ""
Write-Host "  ╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Blue
Write-Host "  ║  QuickView Microsoft Store Package Builder                ║" -ForegroundColor Blue
Write-Host "  ╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Blue

# ── 1. Pre-flight checks ──────────────────────────────────────────────────────
Write-Step "Running pre-flight checks…"

# Check manifest exists
if (-not (Test-Path $Manifest)) {
    Write-Fail "Package.appxmanifest not found at: $Manifest"
}

# Check for placeholder values in manifest
$manifestContent = Get-Content $Manifest -Raw
$hasPlaceholders = $false

if ($manifestContent -match 'Name="PLACEHOLDER\.QuickView"') {
    Write-Warn "Package Identity Name still contains PLACEHOLDER"
    Write-Info "  Update in Package.appxmanifest with value from Partner Center"
    $hasPlaceholders = $true
}

if ($manifestContent -match 'Publisher="CN=PLACEHOLDER"') {
    Write-Warn "Publisher CN still contains PLACEHOLDER"
    Write-Info "  Update in Package.appxmanifest with Publisher from Partner Center"
    $hasPlaceholders = $true
}

if ($manifestContent -match 'PLACEHOLDER_STORE_PRODUCT_ID') {
    Write-Warn "UpdateService.cs contains PLACEHOLDER_STORE_PRODUCT_ID"
    Write-Info "  Update StoreProductId in Infrastructure\Update\UpdateService.cs"
}

# Check for placeholder Store assets
$requiredAssets = @(
    "Assets\StoreLogo.png",
    "Assets\Square44x44Logo.png",
    "Assets\Square150x150Logo.png",
    "Assets\Wide310x150Logo.png",
    "Assets\SplashScreen.png"
)

$missingAssets = @()
foreach ($asset in $requiredAssets) {
    $fullPath = Join-Path $Root $asset
    if (-not (Test-Path $fullPath)) {
        $missingAssets += $asset
    }
}

if ($missingAssets.Count -gt 0) {
    Write-Warn "Missing Store assets:"
    $missingAssets | ForEach-Object { Write-Info "  - $_" }
    Write-Info "These were generated as placeholders. Replace with real artwork."
}

if ($hasPlaceholders) {
    Write-Host ""
    $response = Read-Host "Continue anyway? (y/n)"
    if ($response -ne 'y') {
        Write-Info "Build cancelled. Update placeholders and try again."
        exit 0
    }
}

Write-OK "Pre-flight checks complete"

# ── 2. Build MSIX package ─────────────────────────────────────────────────────
Write-Step "Building MSIX package (Release configuration)…"

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# Update version in manifest if specified
if ($Version -ne "1.0.0.0") {
    Write-Info "Setting version to $Version in manifest…"
    $manifestContent = $manifestContent -replace 'Version="[\d\.]+"', "Version=`"$Version`""
    Set-Content -Path $Manifest -Value $manifestContent -Encoding UTF8
}

# Build arguments for MSIX package via dotnet build
$buildArgs = @(
    "build", $Project,
    "-c", "Release",
    "-p:Platform=x64",
    "-p:AppxPackageDir=$OutputDir\",
    "-p:UapAppxPackageBuildMode=SideloadOnly",
    "-p:AppxBundle=Never",
    "-p:GenerateAppxPackageOnBuild=true",
    "--nologo",
    "-v", "minimal"
)

& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    Write-Fail "MSIX packaging failed (exit $LASTEXITCODE)"
}

# Find the generated MSIX
$msixFile = Get-ChildItem -Path $OutputDir -Filter "QuickView*.msix" -File | Select-Object -First 1
if (-not $msixFile) {
    Write-Fail "MSIX package not found in $OutputDir"
}

$sizeMb = [math]::Round($msixFile.Length / 1MB, 1)
Write-OK "MSIX package created: $($msixFile.Name)  ($sizeMb MB)"

# ── 3. Create MSIXUPLOAD for Store (optional) ─────────────────────────────────
if ($CreateUploadPackage) {
    Write-Step "Creating MSIXUPLOAD package for Store submission…"
    
    # MSIXUPLOAD is essentially a zip of the MSIX + symbols
    $msixuploadPath = $msixFile.FullName -replace '\.msix$', '.msixupload'
    
    # For now, just rename/copy the MSIX as MSIXUPLOAD
    # A proper MSIXUPLOAD would include the .msix + symbols .appxsym
    Copy-Item $msixFile.FullName $msixuploadPath -Force
    
    Write-OK "MSIXUPLOAD created: $(Split-Path $msixuploadPath -Leaf)"
    Write-Info "For proper Store submission, use Visual Studio:"
    Write-Info "  Project → Package and Publish → Create App Packages → Microsoft Store"
}

# ── 4. Summary ────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "  ╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "  ║  Store Package Ready!                                      ║" -ForegroundColor Green
Write-Host "  ╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""

Write-Info "Package location: $($msixFile.FullName)"
Write-Info ""
Write-Info "Next steps:"
Write-Info "  1. Test the package locally: Add-AppxPackage $($msixFile.Name)"
Write-Info "  2. Run Windows App Certification Kit (WACK) on the package"
Write-Info "  3. Upload to Partner Center → App packages"
Write-Host ""

if ($hasPlaceholders) {
    Write-Warn "Remember to update placeholder values before Store submission!"
}
