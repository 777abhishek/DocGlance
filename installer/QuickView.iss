; ─────────────────────────────────────────────────────────────────────────────
;  QuickView — Inno Setup 6 script
;  Produces:  QuickView-Setup-x64.exe
;
;  Self-contained installer: all .NET runtime DLLs are bundled.
;  Windows App SDK 2.4 is downloaded and installed on demand if missing.
;
;  To compile:
;    iscc installer\QuickView.iss
;  Or use build-installer.ps1 which does publish + compile in one step.
; ─────────────────────────────────────────────────────────────────────────────

#define AppName      "QuickView"
#define AppVersion   "1.0.0"
#define AppPublisher "QuickView Contributors"
#define AppURL       "https://github.com/your-org/QuickView"
#define AppExeName   "QuickView.exe"
#define PublishDir   "..\publish"

; ── Installer metadata ────────────────────────────────────────────────────────
[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
; Stable GUID — Windows uses this to recognise upgrades as the same app.
; Do NOT change this once you have shipped a release.
AppId={{A3F7D2C1-85B4-4E9F-B6D3-2F8C1E9A4B7D}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
; Compression
Compression=lzma2/ultra64
SolidCompression=yes
; Output
OutputDir=output
OutputBaseFilename=QuickView-Setup-x64
; Require 64-bit Windows 10 1809+
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
MinVersion=10.0.17763
; Icon
SetupIconFile=..\Assets\AppIcon.ico
; Wizard style — classic (no sidebar/header images)
WizardStyle=classic
; Dual-mode install: admin or current user (casual users install without UAC)
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
; Uninstall
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
; Close app if running during install/uninstall
CloseApplications=yes
; Create uninstall entry in Add/Remove Programs
CreateUninstallRegKey=yes
; Version info embedded into the installer EXE (visible in Explorer → Properties)
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoCompany={#AppPublisher}
VersionInfoCopyright=Copyright © 2025 {#AppPublisher}

; ── Languages ────────────────────────────────────────────────────────────────
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

; ── Optional tasks ───────────────────────────────────────────────────────────
[Tasks]
Name: "desktopicon";  Description: "Create a &desktop shortcut";           GroupDescription: "Additional icons:"; Flags: unchecked
Name: "startupicon";  Description: "Launch QuickView at &Windows startup"; GroupDescription: "Additional icons:"; Flags: unchecked

; ── Files to install ─────────────────────────────────────────────────────────
[Files]
; All published output files (dotnet publish output)
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; ── Shortcuts ────────────────────────────────────────────────────────────────
[Icons]
Name: "{autoprograms}\{#AppName}";            Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}";      Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
Name: "{autostartup}\{#AppName}";    Filename: "{app}\{#AppExeName}"; Tasks: startupicon

; ── Registry — "Open with QuickView" shell registration ──────────────────────
[Registry]
Root: HKA; Subkey: "Software\Classes\Applications\QuickView.exe";                    ValueType: string; ValueName: ""; ValueData: "QuickView File Viewer"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\QuickView.exe\DefaultIcon";        ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},0"
Root: HKA; Subkey: "Software\Classes\Applications\QuickView.exe\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""

; ── Run: Windows App SDK + launch ─────────────────────────────────────────────
[Run]
; 1. Install Windows App SDK if it was downloaded
Filename: "{tmp}\windowsappsdk-installer.exe"; \
    Parameters: "--quiet"; \
    StatusMsg: "Installing Windows App SDK runtime…"; \
    Flags: waituntilterminated skipifdoesntexist

; 2. Launch the app after install (optional, skipped in silent mode)
Filename: "{app}\{#AppExeName}"; \
    Description: "Launch {#AppName}"; \
    Flags: nowait postinstall skipifsilent

; ── Uninstall: kill the process first ────────────────────────────────────────
[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /F /IM {#AppExeName} 2>nul"; Flags: runhidden; RunOnceId: "KillQuickView"

; ── Pascal/Delphi code — download Windows App SDK if needed ───────────────────
[Code]

{ ──────────────────────────────────────────────────────────────────────────── }
{ Check whether Windows App SDK 2.x is installed                               }
{                                                                               }
{ The SDK ships as an MSIX framework package — it does NOT write the registry  }
{ keys that were checked before. The only reliable detection is Get-AppxPackage.}
{ ──────────────────────────────────────────────────────────────────────────── }
function NeedsWindowsAppSdk: Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  {
    PowerShell: get any WindowsAppRuntime package whose version starts with 2.
    Exit 0 = found, non-zero = not found.
    This covers 2.0, 2.1, 2.4 etc. so we never re-install on minor updates.
  }
  if Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
          '-NonInteractive -Command ' +
          '"if (Get-AppxPackage -Name ''Microsoft.WindowsAppRuntime.*'' | ' +
          ' Where-Object { $_.Version -like ''2.*'' }) { exit 0 } else { exit 1 }"',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    if ResultCode = 0 then
      Result := False;
end;

{ ──────────────────────────────────────────────────────────────────────────── }
{ Download a file from URL to Dest using PowerShell                            }
{ Returns True on success.                                                      }
{ ──────────────────────────────────────────────────────────────────────────── }
function DownloadFile(const URL, Dest: string): Boolean;
var
  StatusCode: Integer;
begin
  Result := False;
  WizardForm.StatusLabel.Caption := 'Downloading ' + ExtractFileName(Dest) + '…';
  WizardForm.ProgressGauge.Style := npbstMarquee;

  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
              '-ExecutionPolicy Bypass -NonInteractive -Command ' +
              '"Invoke-WebRequest -Uri ''' + URL + ''' ' +
              '-OutFile ''' + Dest + ''' -UseBasicParsing"',
              '', SW_HIDE, ewWaitUntilTerminated, StatusCode) then
  begin
    { powershell.exe not found — very unlikely }
    Exit;
  end;

  WizardForm.ProgressGauge.Style := npbstNormal;
  Result := (StatusCode = 0) and FileExists(Dest);
end;

{ ──────────────────────────────────────────────────────────────────────────── }
{ Called just before files are copied — download what we need                  }
{ ──────────────────────────────────────────────────────────────────────────── }
procedure CurStepChanged(CurStep: TSetupStep);
var
  SdkUrl:     string;
  SdkDest:    string;
begin
  if CurStep = ssInstall then
  begin
    { ── Windows App SDK 2.4 ── }
    if NeedsWindowsAppSdk then
    begin
      { Stable redirect URL — always points to the latest 2.x release. }
      SdkUrl  := 'https://aka.ms/windowsappsdk/2.4/latest/windowsappruntimeinstall-x64.exe';
      SdkDest := ExpandConstant('{tmp}\windowsappsdk-installer.exe');
      if not DownloadFile(SdkUrl, SdkDest) then
        MsgBox(
          'Windows App SDK runtime download failed.' + #13#10 +
          'Please install it manually from:' + #13#10 +
          'https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads' + #13#10 + #13#10 +
          'The app may not launch until the runtime is installed.',
          mbInformation, MB_OK);
    end;
  end;
end;
