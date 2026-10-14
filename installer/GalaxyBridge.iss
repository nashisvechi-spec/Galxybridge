#ifndef AppVersion
  #error AppVersion must be supplied by Build-WindowsInstaller.ps1
#endif
#ifndef AppSource
  #error AppSource must be supplied by Build-WindowsInstaller.ps1
#endif
#ifndef SetupOutput
  #error SetupOutput must be supplied by Build-WindowsInstaller.ps1
#endif

[Setup]
; Keep this ID stable: upgrades must reuse the same installation/uninstaller.
AppId={{E7EA5A53-03BA-490F-ABBD-0F19AC1384DE}
AppName=Galaxy Bridge
AppMutex=GalaxyBridge.CurrentUser.{username}
AppVersion={#AppVersion}
AppPublisher=Galaxy Bridge
AppPublisherURL=https://github.com/nashisvechi-spec/Galxybridge
DefaultDirName={localappdata}\Programs\GalaxyBridge
DefaultGroupName=Galaxy Bridge
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
UsePreviousAppDir=yes
DisableProgramGroupPage=yes
WizardStyle=modern
SetupIconFile=..\src\GalaxyBridge.Windows\Resources\App.ico
UninstallDisplayIcon={app}\GalaxyBridge.exe
UninstallDisplayName=Galaxy Bridge
OutputDir={#SetupOutput}
OutputBaseFilename=GalaxyBridge-Setup-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
CloseApplicationsFilter=GalaxyBridge.exe
RestartApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Excludes: "*.apk,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Galaxy Bridge\Galaxy Bridge"; Filename: "{app}\GalaxyBridge.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Galaxy Bridge"; Filename: "{app}\GalaxyBridge.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; Only remove this application-owned autostart value when uninstalling.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "GalaxyBridge"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\GalaxyBridge.exe"; Description: "{cm:LaunchProgram,Galaxy Bridge}"; Flags: nowait postinstall skipifsilent

; App settings/identity and received files are outside {app}. Do not delete them
; during upgrades or uninstall, and do not replace them with packaged defaults.
