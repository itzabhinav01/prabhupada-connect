; Inno Setup Script for Prabhupāda Connect
; Generates a single standalone installer exe for 1-click installation

#define MyAppName "Prabhupāda Connect"
#define MyAppVersion "2.0"
#define MyAppPublisher "Bhaktivedanta Research Group"
#define MyAppURL "https://github.com/itzabhinav01/prabhupada-connect"
#define MyAppExeName "Launch.bat"

[Setup]
AppId={{9B78D8D3-64E2-4C1C-9E9A-A3E54A62C6E1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName=C:\vedabase versions\modern vedabase v2
DisableDirPage=no
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\dist
OutputBaseFilename=PrabhupadaConnect-Setup-v2.0
SetupIconFile=..\App\VedaBaseModern2.UI\Assets\AppIcon.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\Assets\AppIcon.ico
ChangesAssociations=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Release binaries (WinUI 3 + .NET 10)
Source: "..\App\VedaBaseModern2.UI\bin\Release\net10.0-windows10.0.26100.0\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Database corpus (267MB SQLite FTS5 database)
Source: "..\Database\prabhupada_corpus.db"; DestDir: "{app}\Database"; Flags: ignoreversion

; Launchers and shortcut scripts
Source: "..\Launch.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Launch.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Create-Desktop-Shortcut.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Create-Desktop-Shortcut.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\AppIcon.ico"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Prabhupada Connect"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\AppIcon.ico"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: shellexec postinstall nowait skipifsilent
