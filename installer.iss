#define MyAppName "Zdenko, zachráň ma!"
#define MyAppVersion "2.9"
#define MyAppPublisher "Zdenko Ivančík"
#define MyAppExeName "ZachranaDisku.exe"
#define MyAppId "{{80516B42-DD69-4D58-A61B-6C0C881B6894}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright=© 2026 {#MyAppPublisher}
DefaultDirName={autopf}\ZachranaDisku
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=dist
OutputBaseFilename=ZachranaDisku-Setup-{#MyAppVersion}
SetupIconFile=app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
LicenseFile=LICENSE.txt
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
AllowNoIcons=yes
VersionInfoVersion=2.9.0.0
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Záchrana a oprava NTFS/FAT32 diskov

[Languages]
Name: "sk"; MessagesFile: "compiler:Languages\Slovak.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Exe sa pri inštalácii rozbalí cez ExtractTemporaryFile a skopíruje sa ručne.
; Takto zostáva v inštalátore iba raz a dá sa z neho spustiť aj prenosná verzia.
Source: "dist\{#MyAppExeName}"; DestDir: "{app}"; Flags: dontcopy
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: shellexec nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\{#MyAppExeName}"

[Code]
var
  ModePage: TInputOptionWizardPage;
  PortableLaunch: Boolean;
  AutoUpdate: Boolean;

function HasCommandLineSwitch(const Value: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Value) = 0 then
    begin
      Result := True;
      Exit;
    end;
end;

function InitializeSetup: Boolean;
begin
  AutoUpdate := HasCommandLineSwitch('/AUTOUPDATE');
  Result := True;
end;

procedure InitializeWizard;
begin
  ModePage := CreateInputOptionPage(wpWelcome,
    'Spôsob spustenia',
    'Chcete program nainštalovať alebo len spustiť?',
    'Prenosná verzia nepotrebuje inštaláciu. Program sa rozbalí vedľa inštalátora ' +
    '(alebo do lokálneho priečinka) a hneď sa spustí.',
    True, False);
  ModePage.Add('Nainštalovať do počítača');
  ModePage.Add('Spustiť bez inštalácie (prenosná verzia)');
  ModePage.Values[0] := True;
end;

function CopyPortableExe: string;
var
  Source, TargetDir, Target: string;
begin
  Target := '';
  ExtractTemporaryFile('{#MyAppExeName}');
  Source := ExpandConstant('{tmp}\{#MyAppExeName}');

  { Najprv skúsime priečinok, odkiaľ bol spustený inštalátor. }
  TargetDir := ExtractFileDir(ExpandConstant('{srcexe}'));
  Target := TargetDir + '\{#MyAppExeName}';
  if not CopyFile(Source, Target, False) then
  begin
    { Ak sa tam nedá zapisovať (napr. CD/USB), použi Lokálne AppData. }
    TargetDir := ExpandConstant('{localappdata}\ZachranaDisku\portable');
    ForceDirectories(TargetDir);
    Target := TargetDir + '\{#MyAppExeName}';
    if not CopyFile(Source, Target, False) then
      Target := '';
  end;

  Result := Target;
end;

function LaunchPortable: Boolean;
var
  Target: string;
  ErrorCode: Integer;
begin
  Result := False;
  Target := CopyPortableExe;
  if Target = '' then
  begin
    MsgBox('Prenosnú verziu sa nepodarilo rozbaliť. Skúste spustiť inštaláciu.', mbError, MB_OK);
    Exit;
  end;

  if not ShellExec('', Target, '', ExtractFileDir(Target), SW_SHOWNORMAL, ewNoWait, ErrorCode) then
  begin
    MsgBox('Program sa nepodarilo spustiť (kód ' + IntToStr(ErrorCode) + ').', mbError, MB_OK);
    Exit;
  end;

  PortableLaunch := True;
  Result := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (ModePage <> nil) and (CurPageID = ModePage.ID) and ModePage.Values[1] then
  begin
    if LaunchPortable then
    begin
      WizardForm.Close;
      Result := False;
    end
    else
      Result := False;
  end;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  if PortableLaunch then
    Confirm := False;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Target: string;
  ErrorCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    ExtractTemporaryFile('{#MyAppExeName}');
    Target := ExpandConstant('{app}\{#MyAppExeName}');
    if not CopyFile(ExpandConstant('{tmp}\{#MyAppExeName}'), Target, False) then
      MsgBox('Nepodarilo sa nainštalovať ' + ExpandConstant('{app}\{#MyAppExeName}') + '.',
        mbError, MB_OK)
    else if AutoUpdate then
    begin
      { Po tichej aktualizácii znova spustíme novú verziu. }
      ShellExec('', Target, '', ExtractFileDir(Target), SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
  end;
end;







