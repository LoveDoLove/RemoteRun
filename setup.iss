; Inno Setup Script for RemoteRun v1.0
; Run programs as NT AUTHORITY\SYSTEM locally or on remote machines.
;
; Build RemoteRun first:
;   dotnet build -c Release RemoteRun\RemoteRun.csproj
; Then compile this script with Inno Setup Compiler.

#define MyAppName      "RemoteRun"
#define MyAppVersion   "1.0"
#define MyAppPublisher "LoveDoLove"
#define MyAppURL       "https://github.com/LoveDoLove/AdvancedRun-Rework"
#define MyAppExeName   "RemoteRun.exe"

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
OutputBaseFilename=RemoteRun_Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
; Install to the 64-bit Program Files folder on 64-bit systems
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; Optional desktop shortcut
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; RemoteRun main executable (build with: dotnet build -c Release RemoteRun\RemoteRun.csproj)
Source: "RemoteRun\bin\Release\net8.0-windows\RemoteRun.exe"; DestDir: "{app}"; Flags: ignoreversion
; Documentation
Source: "RemoteRun\README.md"; DestDir: "{app}"; Flags: ignoreversion isreadme
; License
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; ── Start Menu ──────────────────────────────────────────────────────────────

; Main RemoteRun shortcut
Name: "{group}\RemoteRun"; Filename: "{app}\RemoteRun.exe"; \
    Flags: runasadmin; \
    Comment: "Run programs as NT AUTHORITY\SYSTEM"

; "System CMD" – appears in Windows Search / Start Menu search.
; Typing "System CMD" and pressing Enter opens an interactive SYSTEM
; command prompt without having to open an admin terminal manually.
Name: "{group}\System CMD"; Filename: "{app}\RemoteRun.exe"; \
    Parameters: "cmd.exe"; \
    Flags: runasadmin; \
    Comment: "Open an interactive NT AUTHORITY\SYSTEM command prompt"

; Uninstall shortcut
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

; ── Desktop (optional task) ───────────────────────────────────────────────
Name: "{autodesktop}\RemoteRun"; Filename: "{app}\RemoteRun.exe"; \
    Flags: runasadmin; Tasks: desktopicon; \
    Comment: "Run programs as NT AUTHORITY\SYSTEM"

[Run]
; After installation the user can immediately open a SYSTEM command prompt.
Filename: "{app}\RemoteRun.exe"; Parameters: "cmd.exe"; \
    Description: "Open a SYSTEM command prompt now"; \
    Flags: nowait postinstall skipifsilent runasadmin