#define MyAppName "Methos U Editor"
#define MyAppVersion "1.0.2"
#define MyAppExeName "Methos U Editor.exe"

[Setup]
AppId={{F2A2D0B7-2E0C-4C5D-A91A-2CBB1F7E0C31}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Methos
DefaultDirName={localappdata}\Programs\Methos U Editor
DefaultGroupName=Methos U Editor
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\release
OutputBaseFilename=Methos-U-Editor-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README-ES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
Name: "{userdocs}\Methos U Editor\u completo"
Name: "{userdocs}\Methos U Editor\u por partes"

[Icons]
Name: "{autoprograms}\Methos U Editor"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Methos U Editor"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir Methos U Editor"; Flags: postinstall nowait skipifsilent
