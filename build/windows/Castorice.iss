; Inno Setup script for the Castorice installer. build/package-windows.ps1 runs it with
;   /DAppVersion=<version> /DSourceDir=<published build> /DOutputDir=<artifacts> /DArch=<x64|arm64>

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\artifacts\windows\win-" + Arch + "\Castorice"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts"
#endif
#if Arch == "arm64"
  #define AllowedArch "arm64"
#else
  #define AllowedArch "x64compatible"
#endif

[Setup]
; Keeps upgrades and the uninstall entry together across versions. Never change it.
AppId={{A25384F7-6E5C-439B-ABAE-399349CFA5BC}
AppName=Castorice
AppVersion={#AppVersion}
AppVerName=Castorice {#AppVersion}
AppPublisher=Castorice
AppPublisherURL=https://github.com/notjansel/castorice
AppSupportURL=https://github.com/notjansel/castorice/issues
DefaultDirName={autopf}\Castorice
DefaultGroupName=Castorice
DisableProgramGroupPage=yes
; Installs for the current user by default, so no administrator prompt; the dialog offers an
; install for everyone instead.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed={#AllowedArch}
ArchitecturesInstallIn64BitMode={#AllowedArch}
OutputDir={#OutputDir}
OutputBaseFilename=Castorice-{#AppVersion}-windows-{#Arch}-setup
SetupIconFile=..\..\src\Castorice.Desktop\Assets\castorice.ico
UninstallDisplayIcon={app}\Castorice.exe
UninstallDisplayName=Castorice
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Settings and pools live in %APPDATA%\Castorice and survive an uninstall on purpose.
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Castorice"; Filename: "{app}\Castorice.exe"
Name: "{autodesktop}\Castorice"; Filename: "{app}\Castorice.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Castorice.exe"; Description: "{cm:LaunchProgram,Castorice}"; Flags: nowait postinstall skipifsilent
