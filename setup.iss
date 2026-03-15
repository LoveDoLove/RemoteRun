; Inno Setup Script for AdvancedRun v1.51
; This script packages AdvancedRun.exe and its associated configuration/help files.

[Setup]
; Basic Application Information
AppId={{A7B2C1D0-E3F4-4B5C-9D8A-71351171E4E4}
AppName=AdvancedRun
AppVersion=1.51
AppPublisher=NirSoft / LoveDoLove
AppPublisherURL=https://www.nirsoft.net
AppSupportURL=https://www.nirsoft.net
AppUpdatesURL=https://www.nirsoft.net
DefaultDirName={autopf}\AdvancedRun
DefaultGroupName=AdvancedRun
AllowNoIcons=yes
LicenseFile=readme.txt
; Output Settings
OutputDir=Output
OutputBaseFilename=AdvancedRun_Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern

; ArchitecturesInstallIn64BitMode=x64 ensures it installs to "Program Files" instead of "Program Files (x86)"
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The main executable
Source: "AdvancedRun.exe"; DestDir: "{app}"; Flags: ignoreversion
; Help file
Source: "AdvancedRun.chm"; DestDir: "{app}"; Flags: ignoreversion
; Command script
Source: "AdvancedRunSysCmd.cmd"; DestDir: "{app}"; Flags: ignoreversion
; RemoteRun - C# command tool for NT AUTHORITY\SYSTEM
Source: "RemoteRun\bin\Release\net8.0-windows\RemoteRun.exe"; DestDir: "{app}"; Flags: ignoreversion; Check: FileExists(ExpandConstant('{src}\RemoteRun\bin\Release\net8.0-windows\RemoteRun.exe'))
; Configuration files
Source: "SYSTEM.cfg"; DestDir: "{app}"; Flags: ignoreversion
; Documentation
Source: "readme.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme

[Icons]
; Main Application Shortcut
Name: "{group}\AdvancedRun"; Filename: "{app}\AdvancedRun.exe"
; Searchable Command Shortcut (This allows it to appear in Windows Search)
Name: "{group}\AdvancedRun System Command"; Filename: "{app}\AdvancedRunSysCmd.cmd"; IconFilename: "{app}\AdvancedRun.exe"
; RemoteRun shortcut
Name: "{group}\RemoteRun (SYSTEM command tool)"; Filename: "{app}\RemoteRun.exe"; Check: FileExists(ExpandConstant('{app}\RemoteRun.exe'))
; Help and Uninstall
Name: "{group}\AdvancedRun Help"; Filename: "{app}\AdvancedRun.chm"
Name: "{group}\{cm:UninstallProgram,AdvancedRun}"; Filename: "{uninstallexe}"
; Desktop shortcut (Optional)
Name: "{autodesktop}\AdvancedRun"; Filename: "{app}\AdvancedRun.exe"; Tasks: desktopicon

[Run]
; Option to launch the app after installation
Filename: "{app}\AdvancedRun.exe"; Description: "{cm:LaunchProgram,AdvancedRun}"; Flags: nowait postinstall skipifsilent

[Code]
// Custom code can be added here if needed to check for specific system requirements
// or to handle custom registry keys.