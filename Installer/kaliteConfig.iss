#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\\publish\\win-x64"
#endif

#define MyAppName "kaliteConfig"
#define MyAppExe "kaliteConfig.exe"

[Setup]
AppId={{422E40DD-B1A5-4157-907F-690182582441}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=..\\dist
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=admin
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableReadyPage=yes
DisableFinishedPage=yes
DisableWelcomePage=yes
CloseApplications=force
RestartApplications=no
SetupLogging=yes
VersionInfoVersion={#MyAppVersion}
UninstallDisplayName={#MyAppName}
SetupIconFile=..\src\kaliteConfig\Assets\kaliteConfig.ico
UninstallDisplayIcon={app}\{#MyAppExe}

[Files]
Source: "redist\VC_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "redist\MicrosoftEdgeWebView2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "{#PublishDir}\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
Root: HKLM; Subkey: "SOFTWARE\KaliteOS"; ValueType: dword; ValueName: "IsInstalled"; ValueData: "0"; Flags: createvalueifdoesntexist uninsdeletekeyifempty
Root: HKLM; Subkey: "SOFTWARE\KaliteOS"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: createvalueifdoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"

[Run]
Filename: "{tmp}\VC_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing Microsoft Visual C++ runtime..."; Flags: waituntilterminated runhidden; Check: NeedsVCRedist
Filename: "{tmp}\MicrosoftEdgeWebView2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installing Microsoft Edge WebView2 runtime..."; Flags: waituntilterminated runhidden; Check: NeedsWebView2
Filename: "{app}\{#MyAppExe}"; Parameters: "--tray"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall shellexec

[UninstallRun]
; Autostart is a Task Scheduler logon task (elevation), not an HKCU Run value;
; remove it so uninstalling doesn't leave a task pointing at a deleted exe.
Filename: "{sys}\schtasks.exe"; Parameters: "/delete /tn ""kaliteConfig"" /f"; Flags: runhidden; RunOnceId: "RemoveStartupTask"

[Code]
const
  BM_CLICK = $00F5;

function NeedsVCRedist: Boolean;
var
  Installed: Cardinal;
  Version: String;
  Major, Minor, Dot: Integer;
begin
  Result := True;
  if not RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) then
    Exit;
  if Installed <> 1 then
    Exit;
  if not RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Version', Version) then
    Exit;
  if (Length(Version) > 0) and (Version[1] = 'v') then
    Delete(Version, 1, 1);
  Dot := Pos('.', Version);
  if Dot = 0 then
    Exit;
  Major := StrToIntDef(Copy(Version, 1, Dot - 1), 0);
  Delete(Version, 1, Dot);
  Dot := Pos('.', Version);
  if Dot = 0 then
    Minor := StrToIntDef(Version, 0)
  else
    Minor := StrToIntDef(Copy(Version, 1, Dot - 1), 0);
  Result := (Major < 14) or ((Major = 14) and (Minor < 44));
end;

function NeedsWebView2: Boolean;
var
  Version: String;
begin
  Result := True;
  if RegQueryStringValue(HKLM64, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version)
     and (Version <> '') and (Version <> '0.0.0.0') then
    Result := False
  else if RegQueryStringValue(HKLM32, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version)
     and (Version <> '') and (Version <> '0.0.0.0') then
    Result := False
  else if RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version)
     and (Version <> '') and (Version <> '0.0.0.0') then
    Result := False;
end;

const
  KaliteOsEdition = 'KALITEOS11';
  KaliteOsOrganization = 'KALITEOS';

function KaliteOsValueMatches(const Value, Expected: String): Boolean;
begin
  Result := Trim(UpperCase(Value)) = Expected;
end;

{ kaliteConfig only installs on a KaliteOS 11 image: every value written by the verification
  file has to match exactly, otherwise setup stops before it writes anything. }
function KaliteOsMarkerFound: Boolean;
var
  Edition, Organization, Manufacturer, Model: String;
begin
  Result := False;
  if not RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\Windows NT\CurrentVersion', 'EditionSubVersion', Edition) then Exit;
  if not RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\Windows NT\CurrentVersion', 'RegisteredOrganization', Organization) then Exit;
  if not RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\OEMInformation', 'Manufacturer', Manufacturer) then Exit;
  if not RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\OEMInformation', 'Model', Model) then Exit;
  Result := KaliteOsValueMatches(Edition, KaliteOsEdition)
    and KaliteOsValueMatches(Organization, KaliteOsOrganization)
    and KaliteOsValueMatches(Manufacturer, KaliteOsOrganization)
    and KaliteOsValueMatches(Model, KaliteOsEdition);
end;

function InitializeSetup(): Boolean;
begin
  Result := KaliteOsMarkerFound;
  if not Result then
    MsgBox('You are not on KaliteOS.' + #13#10 + #13#10 +
           'kaliteConfig only runs on KaliteOS, so setup will not install it.',
           mbError, MB_OK);
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpWelcome) or (CurPageID = wpReady) then
  begin
    PostMessage(WizardForm.NextButton.Handle, BM_CLICK, 0, 0);
  end;
end;

procedure KillRunningAppInstances;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM kaliteConfig.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM kaliteConfig-Consumer.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  KillRunningAppInstances;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    KillRunningAppInstances;
  if CurStep = ssPostInstall then
  begin
    RegWriteStringValue(HKLM, 'SOFTWARE\KaliteOS', 'InstallPath', ExpandConstant('{app}'));
  end;
end;
