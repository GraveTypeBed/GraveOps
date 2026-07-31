#define MyAppName "GraveOps"
#define MyAppVersion "2.0.0-rc2"
#define MyAppPublisher "GraveOps"
#define MyAppExeName "GraveOps.exe"

[Setup]
AppId={{0C406F81-B8B5-4C27-922B-C2B38C9A1E5E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\GraveOps
DefaultGroupName=GraveOps
OutputBaseFilename=GraveOps-Setup-2.0-RC2
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\GraveOps.App\Assets\graveops-drive.ico
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion=2.0.0.0
VersionInfoProductVersion=2.0.0.0
VersionInfoProductTextVersion={#MyAppVersion}
CloseApplications=yes
RestartApplications=no

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\GraveOps"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\GraveOps"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch GraveOps"; Flags: nowait postinstall skipifsilent
