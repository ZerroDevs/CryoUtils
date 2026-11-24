; CryoUtils installer script (Inno Setup 6.3+)
; Build from the repository root with build-installer.ps1, or directly:
;   ISCC.exe /DAppVersion=0.0.100 Installer\CryoUtils.iss

#ifndef AppVersion
#define AppVersion "0.0.100"
#endif

[Setup]
AppId={{D1E8A5B9-1FA2-4214-99EE-CFA864505CA9}
AppName=CryoUtils
AppVersion={#AppVersion}
AppVerName=CryoUtils {#AppVersion}
AppPublisher=ZeroNuxi - ZerroDevs
AppPublisherURL=https://github.com/ZerroDevs/CryoUtils
AppSupportURL=https://github.com/ZerroDevs/CryoUtils/issues
AppUpdatesURL=https://github.com/ZerroDevs/CryoUtils/releases
DefaultDirName={localappdata}\Programs\CryoUtils
DefaultGroupName=CryoUtils
; CryoUtils is an asInvoker utility. A per-user install keeps setup and updates
; free of UAC prompts and keeps the installation writable by the signed-in user.
PrivilegesRequired=lowest
DisableDirPage=yes
DisableProgramGroupPage=yes
UsePreviousAppDir=no
AllowNoIcons=yes
LicenseFile=..\LICENSE
SetupIconFile=..\Images\CryoUtils.ico
UninstallDisplayIcon={app}\CryoUtils.exe
ArchitecturesAllowed=x64compatible
MinVersion=10.0
OutputDir=Output
OutputBaseFilename=CryoUtils-Setup-{#AppVersion}
VersionInfoVersion={#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The publish folder contains the self-contained application. LICENSE, NOTICE
; and VERSION are added explicitly below and excluded from the recursive copy.
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Excludes: "*.pdb,LICENSE,NOTICE,VERSION"
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\NOTICE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\VERSION"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\CryoUtils"; Filename: "{app}\CryoUtils.exe"
Name: "{autodesktop}\CryoUtils"; Filename: "{app}\CryoUtils.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\CryoUtils.exe"; Description: "{cm:LaunchProgram,CryoUtils}"; Flags: nowait postinstall skipifsilent

[Code]
procedure StopCryoUtils;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM CryoUtils.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeSetup: Boolean;
begin
  StopCryoUtils;
  Result := True;
end;

function InitializeUninstall: Boolean;
begin
  StopCryoUtils;
  Result := True;
end;
