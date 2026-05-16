; Inno Setup script for ScrollerCapture 1.0.0
; Build publish output first:  build.bat publish
; Compile with Inno Setup 6:     iscc installer\ScrollerCapture.iss

#define MyAppName "ScrollerView By Unnamed10110"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Unnamed10110"
#define MyAppURL "https://github.com/Unnamed10110/ScrollerCapture"
#define MyAppExeName "ScrollerCapture.exe"
#define MyAppPublishDir "..\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
#define MyAppId "{{A7C4E9F2-3B1D-4F6A-9C8E-2D5F7A1B3E90}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=LICENSE.txt
SetupIconFile=..\Assets\ScrollerCapture.ico
OutputDir=output
OutputBaseFilename=ScrollerCapture-{#MyAppVersion}-setup
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
ShowLanguageDialog=no
VersionInfoVersion=1.0.0.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoCopyright=Copyright (C) {#MyAppPublisher}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "launchafter"; Description: "Launch {#MyAppName} when setup finishes"; GroupDescription: "Other options:"; Flags: checkedonce

[Files]
; Self-contained publish folder (run build.bat publish before compiling this script).
Source: "{#MyAppPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "Screen scroll capture and editor"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; Comment: "Screen scroll capture and editor"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent; Tasks: launchafter

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\ScrollerView"

[Code]
function PublishDir(): String;
begin
  Result := ExpandConstant('{src}..\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish');
end;

function InitializeSetup(): Boolean;
var
  Dir, Exe: String;
begin
  Dir := PublishDir();
  Exe := Dir + '\{#MyAppExeName}';
  if not DirExists(Dir) then
  begin
    MsgBox('Publish output not found:' + #13#10 + Dir + #13#10 + #13#10 +
      'From the repository root, run:' + #13#10 +
      '  build.bat publish' + #13#10 + #13#10 +
      'Then compile this script again with Inno Setup.',
      mbError, MB_OK);
    Result := False;
  end
  else if not FileExists(Exe) then
  begin
    MsgBox('ScrollerCapture.exe was not found:' + #13#10 + Exe + #13#10 +
      'Run build.bat publish and try again.', mbError, MB_OK);
    Result := False;
  end
  else
    Result := True;
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  if MsgBox('Close {#MyAppName} if it is running, then click OK to continue uninstall.',
    mbInformation, MB_OKCANCEL) = IDCANCEL then
  begin
    Result := False;
    Exit;
  end;
  Exec('taskkill', '/IM {#MyAppExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;
