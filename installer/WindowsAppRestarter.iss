#define AppName "Windows App Restarter"
#define AppPublisher "TimStewartJ"
#define AppExeName "WindowsAppRestarter.exe"

#ifndef AppVersion
#define AppVersion "0.1.0"
#endif

#ifndef SourceDir
#define SourceDir "..\artifacts\publish\win-x64"
#endif

[Setup]
AppId={{9DA67270-6C9F-4748-B1C9-D9889886D425}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\WindowsAppRestarter
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputBaseFilename=WindowsAppRestarterSetup
OutputDir=Output
PrivilegesRequired=lowest
Compression=lzma
SolidCompression=yes
UninstallDisplayIcon={app}\{#AppExeName}

[Tasks]
Name: startup; Description: "Start Windows App Restarter when I sign in"; GroupDescription: "Startup:"; Flags: checkedonce

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WindowsAppRestarter"; ValueData: """{app}\{#AppExeName}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
