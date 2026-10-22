#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #error PublishDir must point to a self-contained win-x64 publish directory
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts\installer"
#endif

[Setup]
AppId={{D17CBEB9-1812-4B01-9DD3-E4B4965CC225}
AppName=NormalKeyboardSwitcher2
AppVersion={#AppVersion}
AppPublisher=NormalKeyboardSwitcher2 contributors
AppPublisherURL=https://github.com/dims12/NormalKeyboardSwitcher2
AppSupportURL=https://github.com/dims12/NormalKeyboardSwitcher2/issues
AppUpdatesURL=https://github.com/dims12/NormalKeyboardSwitcher2/releases
DefaultDirName={localappdata}\Programs\NormalKeyboardSwitcher2
DefaultGroupName=NormalKeyboardSwitcher2
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible and not arm64
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
AppMutex=Local\NormalKeyboardSwitcher2
SetupMutex=Local\NormalKeyboardSwitcher2.Setup
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\NormalKeyboardSwitcher2.exe
SetupIconFile=..\..\NormalKeyboardSwitcher2\Assets\AppIcon.ico
OutputDir={#OutputDir}
OutputBaseFilename=NormalKeyboardSwitcher2-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\NormalKeyboardSwitcher2"; Filename: "{app}\NormalKeyboardSwitcher2.exe"
Name: "{group}\Uninstall NormalKeyboardSwitcher2"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\NormalKeyboardSwitcher2.exe"; Description: "Launch NormalKeyboardSwitcher2"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ExitCode: Integer;
  AppPath: String;
  Cleaned: Boolean;
begin
  { Wait until removal is confirmed and the running-app check has passed. }
  if CurUninstallStep <> usUninstall then
    Exit;
  AppPath := ExpandConstant('{app}\NormalKeyboardSwitcher2.exe');
  if FileExists(AppPath) then
  begin
    { Restore any shortcut recovery journal before deleting the executable.
      The helper also removes only the startup entry owned by this copy. }
    Cleaned := Exec(AppPath, '--uninstall-cleanup', ExpandConstant('{app}'),
      SW_HIDE, ewWaitUntilTerminated, ExitCode);
    if Cleaned then
      Cleaned := ExitCode = 0;
    if not Cleaned then
      RaiseException('Unable to prepare NormalKeyboardSwitcher2 for removal. ' +
        'Exit the switcher from its tray menu and retry. Your files have not been removed.');
  end;
end;
