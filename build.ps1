# ─────────────────────────────────────────────────────────────────────────────
#  QuickView build + launch script
#
#  Supports both build modes:
#    - Packaged (MSIX): default, for Microsoft Store submission
#    - Unpackaged (EXE): for direct distribution (GitHub releases)
#
#  Usage:
#    .\build.ps1                   (incremental build + launch)
#    .\build.ps1 -Rebuild           (clean build)
#    .\build.ps1 -NoBuild           (kill old instance and relaunch)
#    .\build.ps1 -Unpackaged        (build as traditional EXE, no MSIX)
#    .\build.ps1 -Configuration Release  (Release build instead of Debug)
# ─────────────────────────────────────────────────────────────────────────────
param(
    [switch]$Rebuild,
    [switch]$NoBuild,
    [switch]$Unpackaged,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$Project  = "$PSScriptRoot\QuickView.csproj"
$LogFile  = "$PSScriptRoot\build.log"
$ExeName  = "QuickView"

# Determine output path based on build mode
if ($Unpackaged) {
    $BinDir = "$PSScriptRoot\bin\x64\$Configuration\net8.0-windows10.0.19041.0\win-x64"
    $Exe = "$BinDir\QuickView.exe"
} else {
    # Packaged (MSIX) output goes to a different path
    $BinDir = "$PSScriptRoot\bin\$Configuration\net8.0-windows10.0.19041.0\win-x64"
    $Exe = "$BinDir\QuickView.exe"
}

# ── Helpers ───────────────────────────────────────────────────────────────────
function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "  $msg" -ForegroundColor Cyan
}

function Write-OK([string]$msg)   { Write-Host "  ✓  $msg" -ForegroundColor Green  }
function Write-Fail([string]$msg) { Write-Host "  ✗  $msg" -ForegroundColor Red    }
function Write-Info([string]$msg) { Write-Host "     $msg" -ForegroundColor Gray   }

# ── Display build mode ────────────────────────────────────────────────────────
if ($Unpackaged) {
    Write-Host ""
    Write-Host "  ╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Yellow
    Write-Host "  ║  Building as UNPACKAGED EXE (for GitHub releases)         ║" -ForegroundColor Yellow
    Write-Host "  ╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "  ╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Blue
    Write-Host "  ║  Building as PACKAGED MSIX (for Microsoft Store)          ║" -ForegroundColor Blue
    Write-Host "  ╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Blue
}

# ── 1. Kill running instance ──────────────────────────────────────────────────
Write-Step "Stopping any running instance of $ExeName…"
$procs = Get-Process -Name $ExeName -ErrorAction SilentlyContinue
if ($procs) {
    $procs | Stop-Process -Force
    Start-Sleep -Milliseconds 600
    Write-OK "Killed $($procs.Count) process(es)"
} else {
    Write-Info "None running"
}

# ── 1b. Kill stale build processes ────────────────────────────────────────────
$lockProcs = @("MSBuild","dotnet") |
    ForEach-Object { Get-Process -Name $_ -ErrorAction SilentlyContinue } |
    Where-Object   { $_ -ne $null }
if ($lockProcs) {
    Write-Info "Stopping $($lockProcs.Count) stale build process(es)…"
    $lockProcs | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
}

# Delete locked intermediate XAML DLL
$objPath = if ($Unpackaged) { "x64\$Configuration" } else { "$Configuration" }
$lockedDll = "$PSScriptRoot\obj\$objPath\net8.0-windows10.0.19041.0\intermediatexaml\QuickView.dll"
if (Test-Path $lockedDll) {
    try   { Remove-Item $lockedDll -Force; Write-Info "Removed locked intermediate DLL" }
    catch { Write-Info "Could not remove locked DLL (still locked?) — trying anyway" }
}

# ── 2. Build native libraries ────────────────────────────────────────────────
if (-not $NoBuild) {
    Write-Step "Building native Rust libraries…"
    $nativeBuildScript = "$PSScriptRoot\native\build-native.ps1"
    if (Test-Path $nativeBuildScript) {
        try {
            & $nativeBuildScript
            Write-OK "Native libraries built successfully"
        } catch {
            Write-Fail "Native build failed: $_"
            exit 1
        }
    } else {
        Write-Info "No native build script found, skipping…"
    }
}

# ── 3. Build managed code ─────────────────────────────────────────────────────
if (-not $NoBuild) {

    $buildArgs = @(
        "build", $Project,
        "-c", $Configuration
    )

    # For unpackaged builds, we need to explicitly set Platform=x64 and remove PACKAGED symbol
    if ($Unpackaged) {
        $buildArgs += "-p:Platform=x64"
        # Remove PACKAGED define to build as traditional unpackaged app
        $buildArgs += "-p:DefineConstants=`$(DefineConstants.Replace(';PACKAGED',''))"
    }

    if ($Rebuild) {
        $buildArgs += "--no-incremental"
        Write-Step "Clean build ($Configuration)…"
    } else {
        Write-Step "Incremental build ($Configuration)…"
    }

    Write-Info "Log → $LogFile"

    # Run build, tee to log and capture exit code
    $startTime = Get-Date
    $output = & dotnet @buildArgs 2>&1
    $output | Set-Content -Path $LogFile -Force -Encoding UTF8
    $output | ForEach-Object { Write-Host "  $_" }
    $exitCode  = $LASTEXITCODE
    $elapsed   = [math]::Round(((Get-Date) - $startTime).TotalSeconds, 1)

    # Parse summary from log
    $errorsLine   = (Select-String -Path $LogFile -Pattern "^\s*\d+ Error"   | Select-Object -Last 1).Line
    $warningsLine = (Select-String -Path $LogFile -Pattern "^\s*\d+ Warning" | Select-Object -Last 1).Line
    $errors   = if ($errorsLine)   { $errorsLine   -replace '^\s+','' } else { "0 Error(s)" }
    $warnings = if ($warningsLine) { $warningsLine -replace '^\s+','' } else { "0 Warning(s)" }

    if ($exitCode -ne 0) {
        Write-Fail "Build FAILED in ${elapsed}s  —  $errors  |  $warnings"
        Write-Info "Full log: $LogFile"
        Write-Host ""

        # Show only error lines for quick scanning
        Write-Host "  ── Errors ─────────────────────────────────────────────" -ForegroundColor Red
        Select-String -Path $LogFile -Pattern "error" |
            Where-Object { $_.Line -notmatch "0 Error" } |
            ForEach-Object { Write-Host "  $($_.Line.Trim())" -ForegroundColor Red }
        Write-Host ""
        exit 1
    }

    Write-OK "Build succeeded in ${elapsed}s  —  $errors  |  $warnings"
}

# ── 4. Verify exe exists ──────────────────────────────────────────────────────
if (-not (Test-Path $Exe)) {
    Write-Fail "Exe not found: $Exe"
    Write-Info "Expected at: $Exe"
    exit 1
}

# ── 4b. Stamp app icon (unpackaged builds only) ───────────────────────────────
if ($Unpackaged) {
    $rcedit = "$PSScriptRoot\tools\rcedit.exe"
    $ico    = "$BinDir\Assets\AppIcon.ico"
    if ((Test-Path $rcedit) -and (Test-Path $ico)) {
        Write-Step "Stamping icon into exe…"
        & $rcedit $Exe --set-icon $ico 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-OK "Icon embedded into QuickView.exe"
        } else {
            Write-Info "rcedit warning (non-fatal): exit $LASTEXITCODE"
        }
    } else {
        Write-Info "Skipping icon stamp (rcedit or ico not found)"
    }
} else {
    Write-Info "Skipping icon stamp (packaged builds use manifest assets)"
}

# ── 5. Launch ─────────────────────────────────────────────────────────────────
Write-Step "Launching $ExeName…"
$proc = Start-Process $Exe -PassThru
Start-Sleep -Seconds 3

if ($proc.HasExited) {
    Write-Fail "App crashed immediately (exit code $($proc.ExitCode))"
    Write-Info "Check $LogFile for details"
    exit 1
}

Write-OK "App running  —  PID $($proc.Id)"
Write-Host ""
Write-Info "Build mode: $(if ($Unpackaged) { 'Unpackaged EXE' } else { 'Packaged MSIX' })"
Write-Info "Configuration: $Configuration"
Write-Host ""
