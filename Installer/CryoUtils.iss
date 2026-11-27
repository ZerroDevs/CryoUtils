; CryoUtils installer script (Inno Setup 6.3+)
; Build from the repository root with build-installer.ps1, or directly:
;   ISCC.exe /DAppVersion=0.0.101 /DVariant=Full Installer\CryoUtils.iss
;   ISCC.exe /DAppVersion=0.0.101 /DVariant=Lite Installer\CryoUtils.iss

#ifndef AppVersion
#define AppVersion "0.0.101"
#endif

#ifndef Variant
#define Variant "Full"
#endif

#if Variant == "Lite"
#define PublishDir "..\publish\win-x64-lite"
#define BaseOutputName "CryoUtils-Setup-Lite-" + AppVersion
#define AppVariantVerName "CryoUtils " + AppVersion + " (Lite)"
#else
#define PublishDir "..\publish\win-x64"
#define BaseOutputName "CryoUtils-Setup-" + AppVersion
#define AppVariantVerName "CryoUtils " + AppVersion
#endif

[Setup]
AppId={{D1E8A5B9-1FA2-4214-99EE-CFA864505CA9}
AppName=CryoUtils
AppVersion={#AppVersion}
AppVerName={#AppVariantVerName}
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
OutputBaseFilename={#BaseOutputName}
VersionInfoVersion={#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

#if Variant == "Lite"
[InstallDelete]
; Clean leftover self-contained runtime DLLs when switching from Full Offline to Lite.
Type: files; Name: "{app}\*.dll"
Type: filesandordirs; Name: "{app}\en"
Type: filesandordirs; Name: "{app}\ar"
#endif

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Excludes: "*.pdb,LICENSE,NOTICE,VERSION"
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\NOTICE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\VERSION"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\CryoUtils"; Filename: "{app}\CryoUtils.exe"
Name: "{autodesktop}\CryoUtils"; Filename: "{app}\CryoUtils.exe"; Tasks: desktopicon

[Run]
Filename: "{sys}\compact.exe"; Parameters: "/c /s:""{app}"" /i /q"; Flags: runhidden waituntilterminated skipifdoesntexist
Filename: "{app}\CryoUtils.exe"; Description: "{cm:LaunchProgram,CryoUtils}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\CryoUtils.exe"; Parameters: "--uninstall-cleanup"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "RemoveCryoContextMenu"

[Code]
#if Variant == "Lite"
const
  DotNet10DesktopUrl = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe';
  DotNet10SetupFile = 'windowsdesktop-runtime-10-win-x64.exe';

var
  DownloadPage: TDownloadWizardPage;
  RuntimeDownloaded: Boolean;
#endif

procedure StopCryoUtils;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM CryoUtils.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

#if Variant == "Lite"
function HasDotNet10Prefix(const Value: String): Boolean;
begin
  Result := (Length(Value) >= 3) and (Copy(Value, 1, 3) = '10.');
end;

function IsDotNetDesktop10Installed: Boolean;
var
  Names: TArrayOfString;
  I: Integer;
  FindRec: TFindRec;
begin
  Result := False;
  if RegGetValueNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
    begin
      if HasDotNet10Prefix(Names[I]) then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;

  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), FindRec) then
  begin
    FindClose(FindRec);
    Result := True;
  end;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(
    'Installing .NET 10 Desktop Runtime',
    'CryoUtils Lite requires Microsoft .NET 10 Desktop Runtime (x64).',
    nil);
  DownloadPage.ShowBaseNameInsteadOfUrl := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ErrorMsg: String;
begin
  Result := True;
  if (CurPageID = wpReady) and (not IsDotNetDesktop10Installed) and (not RuntimeDownloaded) then
  begin
    DownloadPage.Clear;
    DownloadPage.Add(DotNet10DesktopUrl, DotNet10SetupFile, '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
        RuntimeDownloaded := True;
      except
        if DownloadPage.AbortedByUser then
          Log('Runtime download aborted by user.')
        else begin
          ErrorMsg := Format('Could not download .NET 10 Desktop Runtime (%s). Connect to the internet or use the CryoUtils Offline Setup.', [GetExceptionMessage]);
          SuppressibleMsgBox(ErrorMsg, mbCriticalError, MB_OK, IDOK);
        end;
        Result := False;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  TempInstaller: String;
  ResultCode: Integer;
  Switches: String;
begin
  Result := '';
  if IsDotNetDesktop10Installed then
    Exit;

  TempInstaller := ExpandConstant('{tmp}\') + DotNet10SetupFile;
  if not RuntimeDownloaded then
  begin
    try
      DownloadTemporaryFile(DotNet10DesktopUrl, DotNet10SetupFile, '', nil);
      RuntimeDownloaded := True;
    except
      Result := Format('CryoUtils Lite requires .NET 10 Desktop Runtime (x64), and downloading it failed: %s. Connect to the internet or install CryoUtils-Setup (Full Offline).', [GetExceptionMessage]);
      Exit;
    end;
  end;

  if not FileExists(TempInstaller) then
  begin
    Result := 'The downloaded .NET 10 Desktop Runtime installer could not be found.';
    Exit;
  end;

  if WizardSilent then
    Switches := '/install /quiet /norestart'
  else
    Switches := '/install /passive /norestart';

  if not ShellExec('', TempInstaller, Switches, '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := Format('Failed to launch .NET 10 Desktop Runtime installer (code %d).', [ResultCode]);
    Exit;
  end;

  if (ResultCode = 3010) then
    NeedsRestart := True
  else if (ResultCode <> 0) and (ResultCode <> 1638) then
  begin
    Result := Format('.NET 10 Desktop Runtime installation did not complete (exit code %d).', [ResultCode]);
    Exit;
  end;
end;
#endif

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
