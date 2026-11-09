; Quick Scaler installer (Inno Setup 6)
; Keep AppVersion in sync with <Version> in QuickScaler.csproj.

#ifndef PublishDir
  #define PublishDir "bin\Release\net8.0-windows\win-x64\publish"
#endif

[Setup]
AppId={{8F3A2B1C-4D5E-4F60-9A7B-1C2D3E4F5A6B}
AppName=Quick Scaler
AppVersion=1.2
AppPublisher=Quick Scaler
DefaultDirName={autopf}\QuickScaler
DefaultGroupName=Quick Scaler
OutputDir=Output
OutputBaseFilename=QuickScaler_Setup_1.2
SetupIconFile=Assets\QuickScaler.ico
Compression=lzma
SolidCompression=yes
PrivilegesRequired=lowest
MinVersion=10.0.17763
ArchitecturesInstallIn64BitMode=x64os
ArchitecturesAllowed=x64compatible
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayName=Quick Scaler

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Quick Scaler"; Filename: "{app}\QuickScaler.exe"; Parameters: "--tray"
Name: "{autodesktop}\Quick Scaler"; Filename: "{app}\QuickScaler.exe"; Parameters: "--tray"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\QuickScaler.exe"; Description: "Launch Quick Scaler now"; Flags: nowait postinstall skipifsilent

[Code]
{ Framework-dependent publishes need the .NET 8 Desktop Runtime. }
function IsDotNet8DesktopRuntimeInstalled: Boolean;
var
  Found: Boolean;
begin
  Found :=
    RegKeyExists(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost') or
    RegKeyExists(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App') or
    RegKeyExists(HKCU, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost') or
    RegKeyExists(HKCU, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App');
  Result := Found;
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if not IsDotNet8DesktopRuntimeInstalled then
  begin
    if MsgBox('The .NET 8 Desktop Runtime was not found. Quick Scaler needs it to run.' + #13#10 + #13#10 +
      'Download it from https://dotnet.microsoft.com/download/dotnet/8.0 (x64 Desktop Runtime), then run the installer again.' + #13#10 + #13#10 +
      'Alternatively, use the self-contained build, which does not need the runtime.' + #13#10 + #13#10 +
      'Continue with the installation anyway?',
      mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;
