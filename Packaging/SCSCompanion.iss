#define MyAppName "SCS Companion"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "SCS Companion contributors"
#define MyAppExeName "SCSCompanion.exe"

[Setup]
AppId={{EAD9EC9E-759C-4815-B087-A0F38418BBAF}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\SCS Companion
DefaultGroupName=SCS Companion
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\release
OutputBaseFilename=SCSCompanion-Setup-1.0.0-win-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion=1.0.0.0
VersionInfoDescription=Standalone Stanton SCS.3d companion
VersionInfoProductName=SCS Companion
VersionInfoProductVersion=1.0.0
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SCS Companion"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\SCS Companion"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch SCS Companion"; Flags: nowait postinstall skipifsilent
