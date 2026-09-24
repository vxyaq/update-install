#define AppName "Ksyxis Tweaks"
#define AppPublisher "Ksyxis Tweaks"
#define AppExeName "KsyxisTweaks.exe"
#ifndef PublishDir
  #define PublishDir "publish"
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{7B2E4C3A-0D9A-4B5C-9A2D-2A8B1F4E6C70}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\KsyxisTweaks
DefaultGroupName={#AppName}
OutputDir=output
OutputBaseFilename=KsyxisTweaks-Setup-{#AppVersion}-x64
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\{#AppExeName}
WizardStyle=modern

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Uruchom {#AppName}"; Flags: nowait postinstall skipifsilent
