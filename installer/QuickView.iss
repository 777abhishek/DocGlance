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
; Require 64-bit Windows 10 2004+ (19041)
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
MinVersion=10.0.19041
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
Name: "desktopicon";     Description: "Create a &desktop shortcut";           GroupDescription: "Additional icons:"; Flags: unchecked
Name: "startupicon";     Description: "Launch QuickView at &Windows startup"; GroupDescription: "Additional icons:"; Flags: unchecked
Name: "setdefaultpdf";   Description: "Set as default viewer for &PDF files";  GroupDescription: "File associations:"; Flags: unchecked
Name: "setdefaultimage"; Description: "Set as default viewer for common &image formats (PNG, JPG, etc.)"; GroupDescription: "File associations:"; Flags: unchecked

; ── Files to install ─────────────────────────────────────────────────────────
[Files]
; All published output files (dotnet publish output)
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; ── Shortcuts ────────────────────────────────────────────────────────────────
[Icons]
Name: "{autoprograms}\{#AppName}";            Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}";      Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
Name: "{autostartup}\{#AppName}";    Filename: "{app}\{#AppExeName}"; Tasks: startupicon

; ── Registry — File type icons and "Open with QuickView" shell registration ──
[Registry]
; Register as a known application
Root: HKA; Subkey: "Software\Classes\Applications\QuickView.exe";                    ValueType: string; ValueName: ""; ValueData: "QuickView File Viewer"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\QuickView.exe\DefaultIcon";        ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},0"
Root: HKA; Subkey: "Software\Classes\Applications\QuickView.exe\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""

; ── PDF Files ──
Root: HKA; Subkey: "Software\Classes\.pdf\OpenWithProgids";                ValueType: none;   ValueName: "QuickView.PDF";       Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\QuickView.PDF";                       ValueType: string; ValueName: "";                    ValueData: "PDF Document"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\QuickView.PDF\DefaultIcon";           ValueType: string; ValueName: "";                    ValueData: "{app}\Assets\FileTypeIcons\PDF.ico,0"
Root: HKA; Subkey: "Software\Classes\QuickView.PDF\shell\open\command";    ValueType: string; ValueName: "";                    ValueData: """{app}\{#AppExeName}"" ""%1"""

; ── Text Files (TXT, MD, LOG) ──
Root: HKA; Subkey: "Software\Classes\.txt\OpenWithProgids";                ValueType: none;   ValueName: "QuickView.Text";      Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.md\OpenWithProgids";                 ValueType: none;   ValueName: "QuickView.Markdown";  Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.log\OpenWithProgids";                ValueType: none;   ValueName: "QuickView.Log";       Flags: uninsdeletevalue

Root: HKA; Subkey: "Software\Classes\QuickView.Text";                      ValueType: string; ValueName: "";                    ValueData: "Text Document"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\QuickView.Text\DefaultIcon";          ValueType: string; ValueName: "";                    ValueData: "{app}\Assets\FileTypeIcons\Text.ico,0"
Root: HKA; Subkey: "Software\Classes\QuickView.Text\shell\open\command";   ValueType: string; ValueName: "";                    ValueData: """{app}\{#AppExeName}"" ""%1"""

Root: HKA; Subkey: "Software\Classes\QuickView.Markdown";                  ValueType: string; ValueName: "";                    ValueData: "Markdown Document"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\QuickView.Markdown\DefaultIcon";      ValueType: string; ValueName: "";                    ValueData: "{app}\Assets\FileTypeIcons\Markdown.ico,0"
Root: HKA; Subkey: "Software\Classes\QuickView.Markdown\shell\open\command"; ValueType: string; ValueName: "";                  ValueData: """{app}\{#AppExeName}"" ""%1"""

Root: HKA; Subkey: "Software\Classes\QuickView.Log";                       ValueType: string; ValueName: "";                    ValueData: "Log File"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\QuickView.Log\DefaultIcon";           ValueType: string; ValueName: "";                    ValueData: "{app}\Assets\FileTypeIcons\LOG.ico,0"
Root: HKA; Subkey: "Software\Classes\QuickView.Log\shell\open\command";    ValueType: string; ValueName: "";                    ValueData: """{app}\{#AppExeName}"" ""%1"""

; ── Code Files (JSON, XML, various programming languages) ──
Root: HKA; Subkey: "Software\Classes\.json\OpenWithProgids";               ValueType: none;   ValueName: "QuickView.Code";      Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.xml\OpenWithProgids";                ValueType: none;   ValueName: "QuickView.Code";      Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.cs\OpenWithProgids";                 ValueType: none;   ValueName: "QuickView.Code";      Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.py\OpenWithProgids";                 ValueType: none;   ValueName: "QuickView.Code";      Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.java\OpenWithProgids";               ValueType: none;   ValueName: "QuickView.Code";      Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.cpp\OpenWithProgids";                ValueType: none;   ValueName: "QuickView.Code";      Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.js\OpenWithProgids";                 ValueType: none;   ValueName: "QuickView.Code";      Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.ts\OpenWithProgids";                 ValueType: none;   ValueName: "QuickView.Code";      Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.html\OpenWithProgids";               ValueType: none;   ValueName: "QuickView.Code";      Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.css\OpenWithProgids";                ValueType: none;   ValueName: "QuickView.Code";      Flags: uninsdeletevalue

Root: HKA; Subkey: "Software\Classes\QuickView.Code";                      ValueType: string; ValueName: "";                    ValueData: "Code File"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\QuickView.Code\DefaultIcon";          ValueType: string; ValueName: "";                    ValueData: "{app}\Assets\FileTypeIcons\Code.ico,0"
Root: HKA; Subkey: "Software\Classes\QuickView.Code\shell\open\command";   ValueType: string; ValueName: "";                    ValueData: """{app}\{#AppExeName}"" ""%1"""

; ── Office Files (DOCX, XLSX, PPTX, CSV) ──
Root: HKA; Subkey: "Software\Classes\.docx\OpenWithProgids";               ValueType: none;   ValueName: "QuickView.Word";      Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.doc\OpenWithProgids";                ValueType: none;   ValueName: "QuickView.Word";      Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.xlsx\OpenWithProgids";               ValueType: none;   ValueName: "QuickView.Excel";     Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.xls\OpenWithProgids";                ValueType: none;   ValueName: "QuickView.Excel";     Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.pptx\OpenWithProgids";               ValueType: none;   ValueName: "QuickView.PowerPoint"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.ppt\OpenWithProgids";                ValueType: none;   ValueName: "QuickView.PowerPoint"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.csv\OpenWithProgids";                ValueType: none;   ValueName: "QuickView.CSV";       Flags: uninsdeletevalue

Root: HKA; Subkey: "Software\Classes\QuickView.Word";                      ValueType: string; ValueName: "";                    ValueData: "Word Document"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\QuickView.Word\DefaultIcon";          ValueType: string; ValueName: "";                    ValueData: "{app}\Assets\FileTypeIcons\Word.ico,0"
Root: HKA; Subkey: "Software\Classes\QuickView.Word\shell\open\command";   ValueType: string; ValueName: "";                    ValueData: """{app}\{#AppExeName}"" ""%1"""

Root: HKA; Subkey: "Software\Classes\QuickView.Excel";                     ValueType: string; ValueName: "";                    ValueData: "Excel Spreadsheet"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\QuickView.Excel\DefaultIcon";         ValueType: string; ValueName: "";                    ValueData: "{app}\Assets\FileTypeIcons\Excel.ico,0"
Root: HKA; Subkey: "Software\Classes\QuickView.Excel\shell\open\command";  ValueType: string; ValueName: "";                    ValueData: """{app}\{#AppExeName}"" ""%1"""

Root: HKA; Subkey: "Software\Classes\QuickView.PowerPoint";                ValueType: string; ValueName: "";                    ValueData: "PowerPoint Presentation"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\QuickView.PowerPoint\DefaultIcon";    ValueType: string; ValueName: "";                    ValueData: "{app}\Assets\FileTypeIcons\PowerPoint.ico,0"
Root: HKA; Subkey: "Software\Classes\QuickView.PowerPoint\shell\open\command"; ValueType: string; ValueName: "";                ValueData: """{app}\{#AppExeName}"" ""%1"""

Root: HKA; Subkey: "Software\Classes\QuickView.CSV";                       ValueType: string; ValueName: "";                    ValueData: "CSV File"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\QuickView.CSV\DefaultIcon";           ValueType: string; ValueName: "";                    ValueData: "{app}\Assets\FileTypeIcons\CSV.ico,0"
Root: HKA; Subkey: "Software\Classes\QuickView.CSV\shell\open\command";    ValueType: string; ValueName: "";                    ValueData: """{app}\{#AppExeName}"" ""%1"""

; Optional: Set as default for PDF and images (if tasks selected)
Root: HKA; Subkey: "Software\Classes\.pdf"; ValueType: string; ValueName: ""; ValueData: "QuickView.PDF"; Tasks: setdefaultpdf
Root: HKA; Subkey: "Software\Classes\.png"; ValueType: string; ValueName: ""; ValueData: "QuickView.Image"; Tasks: setdefaultimage
Root: HKA; Subkey: "Software\Classes\.jpg"; ValueType: string; ValueName: ""; ValueData: "QuickView.Image"; Tasks: setdefaultimage
Root: HKA; Subkey: "Software\Classes\.jpeg"; ValueType: string; ValueName: ""; ValueData: "QuickView.Image"; Tasks: setdefaultimage

; ── Run: VC++ Redistributable + Windows App SDK + launch ──────────────────────
[Run]
; 1. Install VC++ Redistributable 2015-2022 x64 if downloaded
Filename: "{tmp}\vc_redist.x64.exe"; \
    Parameters: "/install /quiet /norestart"; \
    StatusMsg: "Installing Visual C++ Redistributable…"; \
    Flags: waituntilterminated skipifdoesntexist

; 2. Install Windows App SDK if it was downloaded
Filename: "{tmp}\windowsappsdk-installer.exe"; \
    Parameters: "--quiet"; \
    StatusMsg: "Installing Windows App SDK runtime…"; \
    Flags: waituntilterminated skipifdoesntexist

; 3. Launch the app after install (optional, skipped in silent mode)
Filename: "{app}\{#AppExeName}"; \
    Description: "Launch {#AppName}"; \
    Flags: nowait postinstall skipifsilent

; ── Uninstall: kill the process first ────────────────────────────────────────
[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /F /IM {#AppExeName} 2>nul"; Flags: runhidden; RunOnceId: "KillQuickView"

; ── Pascal/Delphi code — download dependencies if needed ──────────────────────
[Code]

{ ──────────────────────────────────────────────────────────────────────────── }
{ Check whether Visual C++ Redistributable 2015-2022 x64 is installed          }
{                                                                               }
{ Checks registry keys written by both old (2015-2019) and new (2022+) installers}
{ ──────────────────────────────────────────────────────────────────────────── }
function NeedsVCRedist: Boolean;
var
  Major: Cardinal;
begin
  Result := True;
  { Check 2015-2022 unified runtime (14.x) - most common }
  if RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 
                        'Major', Major) then
    if Major >= 14 then
      Result := False;
  
  { Also check older registry location for 2015-2019 }
  if Result then
    if RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\DevDiv\VC\Servicing\14.0\RuntimeMinimum', 
                          'Version', Major) then
      if Major >= 14 then
        Result := False;
end;

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
  VCUrl:      string;
  VCDest:     string;
  SdkUrl:     string;
  SdkDest:    string;
begin
  if CurStep = ssInstall then
  begin
    { ── Visual C++ Redistributable 2015-2022 x64 ── }
    if NeedsVCRedist then
    begin
      { Stable permalink — always points to latest 2015-2022 x64 unified runtime }
      VCUrl  := 'https://aka.ms/vs/17/release/vc_redist.x64.exe';
      VCDest := ExpandConstant('{tmp}\vc_redist.x64.exe');
      if not DownloadFile(VCUrl, VCDest) then
        MsgBox(
          'Visual C++ Redistributable download failed.' + #13#10 +
          'Please install it manually from:' + #13#10 +
          'https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist' + #13#10 + #13#10 +
          'The app may not launch until the runtime is installed.',
          mbInformation, MB_OK);
    end;
    
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
