; Inno Setup Script for RemoteRun v1.0
; Run programs as NT AUTHORITY\SYSTEM locally or on remote machines.
;
; Build RemoteRun first (for each architecture):
;   dotnet publish RemoteRun\RemoteRun.csproj -c Release -r win-x64 --self-contained true -o .\publish\windows-latest-x64\RemoteRun
;   dotnet publish RemoteRun\RemoteRun.csproj -c Release -r win-x86 --self-contained true -o .\publish\windows-latest-x86\RemoteRun
; Then compile this script with Inno Setup Compiler, passing the target architecture:
;   ISCC.exe /DMyAppArch=x64 setup.iss
;   ISCC.exe /DMyAppArch=x86 setup.iss

; Default to x64 if not specified on the command line (/DMyAppArch=x86 to override)
#ifndef MyAppArch
  #define MyAppArch "x64"
#endif

#define MyAppName      "RemoteRun"
#define MyAppVersion   "1.0"
#define MyAppPublisher "LoveDoLove"
#define MyAppURL       "https://github.com/LoveDoLove/AdvancedRun-Rework"
#define MyAppExeName   "RemoteRun.exe"
#define MyAppSourceDir "publish\windows-latest-" + MyAppArch + "\RemoteRun"

[Setup]
; Unique installer identity for RemoteRun
AppId={{F3C4B2A1-D5E6-4F7A-8B9C-0D1E2F3A4B5C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; RemoteRun requires Administrator; reflect that in the installer too
PrivilegesRequired=admin
; Output Settings
OutputDir=Output
OutputBaseFilename=RemoteRun_Setup_{#MyAppArch}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
#if MyAppArch == "x64"
; x64 installer: restrict to 64-bit systems and install in 64-bit Program Files
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; Optional desktop shortcut
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; All published files (exe, DLLs, runtime libraries, etc.) from the dotnet publish output directory
Source: "{#MyAppSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Documentation
Source: "RemoteRun\README.md"; DestDir: "{app}"; Flags: ignoreversion isreadme
; License
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; ── Start Menu ──────────────────────────────────────────────────────────────

; Main RemoteRun shortcut
Name: "{group}\RemoteRun"; Filename: "{app}\RemoteRun.exe"; \
    Comment: "Run programs as NT AUTHORITY\SYSTEM"

; "System CMD" – appears in Windows Search / Start Menu search.
; Typing "System CMD" and pressing Enter opens an interactive SYSTEM
; command prompt without having to open an admin terminal manually.
Name: "{group}\System CMD"; Filename: "{app}\RemoteRun.exe"; \
    Parameters: "cmd.exe"; \
    Comment: "Open an interactive NT AUTHORITY\SYSTEM command prompt"

; Uninstall shortcut
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

; ── Desktop (optional task) ───────────────────────────────────────────────
Name: "{autodesktop}\RemoteRun"; Filename: "{app}\RemoteRun.exe"; \
    Tasks: desktopicon; \
    Comment: "Run programs as NT AUTHORITY\SYSTEM"

[Run]
; After installation the user can immediately open a SYSTEM command prompt.
Filename: "{app}\RemoteRun.exe"; Parameters: "cmd.exe"; \
    Description: "Open a SYSTEM command prompt now";