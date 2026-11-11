#define MyAppName "Methos"
#define MyAppVersion "1.0.2"
#define MyAppExeName "Methos.exe"

[Setup]
AppId={{F2A2D0B7-2E0C-4C5D-A91A-2CBB1F7E0C31}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Methos
DefaultDirName={localappdata}\Programs\Methos
DefaultGroupName=Methos
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\release
OutputBaseFilename=Methos-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
; Recursive publish rule includes the bundled UccRuntime, Interlude dependencies, and verified .uc reference sources.
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README-ES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
Name: "{app}\Archivos U\Comprimido"
Name: "{app}\Archivos U\Descomprimido"
Name: "{app}\Archivos XDAT\Comprimido"
Name: "{app}\Archivos XDAT\Descomprimido"

[Icons]
Name: "{autoprograms}\Methos"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Methos"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir Methos"; Flags: postinstall nowait skipifsilent
