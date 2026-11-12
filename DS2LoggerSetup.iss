#define AppName "DS2 Logger"
#define AppVersion "0.1.0"
#define AppExe "DS2Logger.exe"
[Setup]
AppId={{B2E3C0E2-8D6D-4B3B-9C21-DS2A10900001}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={autopf}\DS2 Logger
DefaultGroupName={#AppName}
OutputDir=Output
OutputBaseFilename=DS2-Logger-Setup
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
RestartIfNeededByRun=yes
[Files]
Source: "publish\DS2Logger.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "USBPcapSetup-1.5.4.0.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
[Icons]
Name: "{group}\DS2 Logger"; Filename: "{app}\DS2Logger.exe"
Name: "{autodesktop}\DS2 Logger"; Filename: "{app}\DS2Logger.exe"
[Run]
Filename: "{tmp}\USBPcapSetup-1.5.4.0.exe"; Parameters: "/S"; StatusMsg: "Instalowanie sterownika USBPcap (wymagane do przechwytywania USB)..."; Flags: runhidden waituntilterminated; Check: not IsUsbPcapInstalled
Filename: "{app}\DS2Logger.exe"; Description: "Uruchom DS2 Logger"; Flags: postinstall nowait skipifsilent

[Code]
function IsUsbPcapInstalled: Boolean;
 begin
   Result := RegKeyExists(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\USBPcap');
 end;
