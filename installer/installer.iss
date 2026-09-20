; HDREZKA for Windows — Inno Setup 6 script.
; Build steps:
;   1. dotnet publish src/HDREZKA.App/HDREZKA.App.csproj -c Release
;   2. Compile this file with Inno Setup 6 (iscc.exe).
; The installer lets the user pick any install folder.

#define MyAppName "HDREZKA"
#define MyAppVersion "1.1.2"
#define MyAppPublisher "voidboost"
#define MyAppExeName "HDREZKA.App.exe"
#define MyAppSourceDir "..\\src\\HDREZKA.App\\bin\\Release\\net8.0-windows10.0.19041.0\\win-x64\\publish"

[Setup]
AppId={{DCA90640-AAF7-4075-A2C1-5618B80DBEF1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=no
LicenseFile=..\LICENSE.md
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MyAppSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
