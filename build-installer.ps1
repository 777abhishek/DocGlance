# ─────────────────────────────────────────────────────────────────────────────
#  QuickView — Installer build script
#  Produces:  installer\output\QuickView-Setup-x64.exe
#
#  Steps:
#    1. Publish the app (Release, x64) with WindowsAppSDKSelfContained
#    2. Patch the icon into the exe with rcedit
#    3. Check for / auto-install Inno Setup 6
#    4. Compile the .iss script with iscc
#
#  Note: WindowsAppSDKSelfContained=true in .csproj bundles Windows App SDK
#        runtime. The .NET runtime is also included via -r win-x64 publish.
#
#  Usage:
#    .\build-installer.ps1
#    .\build-installer.ps1 -SkipPublish   # reuse existing publish folder
#    .\build-installer.ps1 -Version 1.2.0 # override version string
# ─────────────────────────────────────────────────────────────────────────────
param(
    [switch]$SkipPublish,
    [string]$Version = "1.0.0"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root      = $PSScriptRoot
$Project   = "$Root\QuickView.csproj"
$PublishDir= "$Root\publish"
$Installer = "$Root\installer"
$IssFile   = "$Installer\QuickView.iss"
$OutputDir = "$Installer\output"
$rcedit    = "$Root\tools\rcedit.exe"
$Ico       = "$Root\Assets\AppIcon.ico"

# ── helpers ───────────────────────────────────────────────────────────────────
function Write-Step([string]$msg) { Write-Host "`n  $msg" -ForegroundColor Cyan }
function Write-OK  ([string]$msg) { Write-Host "  ✓  $msg" -ForegroundColor Green }
function Write-Fail([string]$msg) { Write-Host "  ✗  $msg" -ForegroundColor Red; exit 1 }
function Write-Info([string]$msg) { Write-Host "     $msg" -ForegroundColor Gray }

# ── 1. Publish ────────────────────────────────────────────────────────────────
if (-not $SkipPublish) {
    Write-Step "Publishing QuickView (Release, x64)…"

    if (Test-Path $PublishDir) {
        Remove-Item $PublishDir -Recurse -Force
    }

    # Clean stale Release obj to prevent CS0579 duplicate-attribute errors
    # that occur when switching from Debug → Release intermediates.
    $releaseObj = "$Root\obj\x64\Release"
    if (Test-Path $releaseObj) {
        Write-Info "Cleaning stale Release obj…"
        Remove-Item $releaseObj -Recurse -Force
    }

    $publishArgs = @(
        "publish", $Project,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",                  # bundle .NET 8 runtime
        "-p:Platform=x64",
        "-p:PublishSingleFile=false",                # keep DLLs separate (required by WinUI 3)
        "-p:PublishReadyToRun=true",                 # AOT compile for faster startup
        "-p:DebugType=embedded",                     # embed PDB for crash reports
        "-p:DebugSymbols=true",
        "-p:GenerateAssemblyInfo=false",             # avoid CS0579 duplicate attribute when -r + -p:Platform are both set
        "-p:GenerateTargetFrameworkAttribute=false", # same: suppress the second TargetFrameworkAttribute
        "--output", $PublishDir,
        "--nologo",
        "-v", "minimal"                              # reduce console noise
    )

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { Write-Fail "dotnet publish failed (exit $LASTEXITCODE)" }
    Write-OK "Published to $PublishDir"
} else {
    Write-Info "Skipping publish (using existing $PublishDir)"
    if (-not (Test-Path $PublishDir)) { Write-Fail "Publish folder not found: $PublishDir" }
}

# ── 2. Patch exe icon ─────────────────────────────────────────────────────────
$exePath = "$PublishDir\QuickView.exe"
if ((Test-Path $rcedit) -and (Test-Path $Ico) -and (Test-Path $exePath)) {
    Write-Step "Patching icon into QuickView.exe…"
    & $rcedit $exePath --set-icon $Ico 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { Write-OK "Icon patched" }
    else { Write-Info "rcedit warning (non-fatal)" }
}

# ── 3. Ensure Inno Setup 6 is installed ──────────────────────────────────────
Write-Step "Checking for Inno Setup 6…"

$iscc = $null

# Common install locations
$candidates = @(
    "C:\Program Files (x86)\Inno Setup 6\iscc.exe",
    "C:\Program Files\Inno Setup 6\iscc.exe"
)
foreach ($c in $candidates) {
    if (Test-Path $c) { $iscc = $c; break }
}

# Also check PATH
if (-not $iscc) {
    $found = Get-Command iscc -ErrorAction SilentlyContinue
    if ($found) { $iscc = $found.Source }
}

if (-not $iscc) {
    Write-Info "Inno Setup 6 not found — downloading and installing silently…"

    $isInstaller = "$env:TEMP\innosetup6-installer.exe"

    # Direct versioned download — more reliable than the download.php redirect.
    # Update the version number here when a new Inno Setup release ships.
    $isUrls = @(
        "https://files.jrsoftware.org/is/6/innosetup-6.3.3.exe",
        "https://github.com/jrsoftware/issrc/releases/download/is-6_3_3/innosetup-6.3.3.exe"
    )

    $downloaded = $false
    foreach ($isUrl in $isUrls) {
        Write-Info "Downloading Inno Setup from $isUrl …"
        try {
            Invoke-WebRequest -Uri $isUrl -OutFile $isInstaller -UseBasicParsing
            if ((Test-Path $isInstaller) -and (Get-Item $isInstaller).Length -gt 100000) {
                $downloaded = $true
                break
            }
        } catch {
            Write-Info "  URL failed: $_"
        }
    }

    if (-not $downloaded) {
        Write-Fail "Could not download Inno Setup 6.`nInstall it manually from https://jrsoftware.org/isdl.php then re-run with -SkipPublish."
    }

    Write-Info "Installing Inno Setup 6 silently…"
    Start-Process $isInstaller -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART" -Wait -ErrorAction Stop
    Remove-Item $isInstaller -Force -ErrorAction SilentlyContinue

    # Re-check
    foreach ($c in $candidates) {
        if (Test-Path $c) { $iscc = $c; break }
    }
    if (-not $iscc) { Write-Fail "Inno Setup install succeeded but iscc.exe not found at expected paths." }
    Write-OK "Inno Setup 6 installed"
} else {
    Write-OK "Found iscc at: $iscc"
}

# ── 4. Compile the installer ──────────────────────────────────────────────────
Write-Step "Compiling installer…"

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# Inject the version number into the .iss at build time via define
& $iscc `
    "/DAppVersion=$Version" `
    $IssFile

if ($LASTEXITCODE -ne 0) { Write-Fail "iscc failed (exit $LASTEXITCODE)" }

$result = "$OutputDir\QuickView-Setup-x64.exe"
if (-not (Test-Path $result)) { Write-Fail "Installer not produced at expected path: $result" }

$sizeMb = [math]::Round((Get-Item $result).Length / 1MB, 1)
Write-OK "Installer ready: $result  ($sizeMb MB)"
Write-Host ""
