# ─────────────────────────────────────────────────────────────────────────────
#  QuickView build + launch script
#  Usage:  .\build.ps1          (build only if changed, then launch)
#          .\build.ps1 -Rebuild  (clean build)
#          .\build.ps1 -NoBuild  (just kill old instance and relaunch)
# ─────────────────────────────────────────────────────────────────────────────
param(
    [switch]$Rebuild,
    [switch]$NoBuild
)

$Project  = "$PSScriptRoot\QuickView.csproj"
$Exe      = "$PSScriptRoot\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\QuickView.exe"
$LogFile  = "$PSScriptRoot\build.log"
$ExeName  = "QuickView"

# ── helpers ──────────────────────────────────────────────────────────────────
function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "  $msg" -ForegroundColor Cyan
}

function Write-OK([string]$msg)   { Write-Host "  ✓  $msg" -ForegroundColor Green  }
function Write-Fail([string]$msg) { Write-Host "  ✗  $msg" -ForegroundColor Red    }
function Write-Info([string]$msg) { Write-Host "     $msg" -ForegroundColor Gray   }

# ── 1. kill running instance ──────────────────────────────────────────────────
Write-Step "Stopping any running instance of $ExeName…"
$procs = Get-Process -Name $ExeName -ErrorAction SilentlyContinue
if ($procs) {
    $procs | Stop-Process -Force
    Start-Sleep -Milliseconds 600
    Write-OK "Killed $($procs.Count) process(es)"
} else {
    Write-Info "None running"
}

# ── 1b. kill any stale XAML compiler / MSBuild processes that may lock the DLL
#         These are left over when a previous build was interrupted mid-flight.
$lockProcs = @("MSBuild","dotnet") |
    ForEach-Object { Get-Process -Name $_ -ErrorAction SilentlyContinue } |
    Where-Object   { $_ -ne $null }
if ($lockProcs) {
    Write-Info "Stopping $($lockProcs.Count) stale build process(es)…"
    $lockProcs | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
}

# Delete the intermediate XAML DLL that may still be locked on disk
$lockedDll = "$PSScriptRoot\obj\x64\Debug\net8.0-windows10.0.19041.0\intermediatexaml\QuickView.dll"
if (Test-Path $lockedDll) {
    try   { Remove-Item $lockedDll -Force; Write-Info "Removed locked intermediate DLL" }
    catch { Write-Info "Could not remove $lockedDll (still locked?) — trying anyway" }
}

# ── 2. build ──────────────────────────────────────────────────────────────────
if (-not $NoBuild) {

    $buildArgs = @(
        "build", $Project,
        "-p:Platform=x64"
    )
    if ($Rebuild) {
        $buildArgs += "--no-incremental"
        Write-Step "Clean build…"
    } else {
        Write-Step "Incremental build…"
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
    $errors   = (Select-String -Path $LogFile -Pattern "^\s*\d+ Error"   | Select-Object -Last 1).Line -replace '^\s+',''
    $warnings = (Select-String -Path $LogFile -Pattern "^\s*\d+ Warning" | Select-Object -Last 1).Line -replace '^\s+',''

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

# ── 3. verify exe exists ──────────────────────────────────────────────────────
if (-not (Test-Path $Exe)) {
    Write-Fail "Exe not found: $Exe"
    exit 1
}

# ── 3b. stamp app icon into the exe PE resources ─────────────────────────────
# WinUI 3 unpackaged apps don't embed the ApplicationIcon from .csproj.
# rcedit patches the PE directly so the icon appears in Explorer, taskbar,
# and Alt+Tab without requiring a packaged build.
$rcedit = "$PSScriptRoot\tools\rcedit.exe"
$ico    = "$PSScriptRoot\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\Assets\AppIcon.ico"
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

# ── 4. launch ─────────────────────────────────────────────────────────────────
Write-Step "Launching $ExeName…"
$proc = Start-Process $Exe -PassThru
Start-Sleep -Seconds 3

if ($proc.HasExited) {
    Write-Fail "App crashed immediately (exit code $($proc.ExitCode))"
    Write-Info "Check build.log for details"
    exit 1
}

Write-OK "App running  —  PID $($proc.Id)"
Write-Host ""
