# RemoteRun

A lightweight C# command-line tool for Windows that runs programs as
**NT AUTHORITY\SYSTEM** locally or on remote machines — similar in
spirit to PsExec and AdvancedRun, but implemented as a minimal,
dependency-free .NET 8 executable.

---

## How it works

### Local execution (fast path)

1. Enables `SeDebugPrivilege` on the calling process.
2. Finds a process already running as SYSTEM (e.g. `winlogon.exe`).
3. Opens and duplicates its primary token.
4. Launches the target program under that token using
   `CreateProcessWithTokenW`, capturing stdout/stderr via pipes.

No service is installed for local execution, making it very fast.

### Remote execution (service path)

1. Copies `RemoteRun.exe` to `\\machine\admin$` (maps to `%SystemRoot%`).
2. Installs a temporary Windows service on the remote machine via the
   remote Service Control Manager.
3. The service worker reads a config file from the temp directory,
   executes the command, captures output to a file, then stops itself.
4. The caller reads the output file and prints it.
5. The service and all temporary files are deleted on completion.

The same executable serves as both the installer/client **and** the
service binary (dual-mode via `--service` internal flag).

---

## Requirements

- Windows Vista / Server 2008 or later (local and remote).
- **Administrator** privileges on the local machine.
- For remote execution: full Administrator rights on the target machine
  and the `admin$` share must be accessible.
- .NET 8 runtime **or** publish as a self-contained single-file exe
  (see *Building* below).

---

## Usage

```
RemoteRun.exe [options] program [arguments]
RemoteRun.exe [options] \\computer program [arguments]

Options:
  -w <directory>   Working directory for the launched process
  -d               Don't wait for the process to finish
  -t <seconds>     Timeout in seconds (default: 60; 0 = unlimited)
  -h, --help       Show help
```

### Examples

```bat
:: Open an interactive SYSTEM command prompt (you can type commands freely)
RemoteRun.exe cmd.exe

:: Run a single command as SYSTEM and capture its output
RemoteRun.exe cmd.exe "/c whoami /all"

:: Run a command with a specific working directory
RemoteRun.exe -w "C:\Windows\System32" cmd.exe "/c dir"

:: Run ipconfig on a remote machine as SYSTEM
RemoteRun.exe \\192.168.1.100 cmd.exe "/c ipconfig /all"

:: Run a PowerShell command on a remote server with 2-minute timeout
RemoteRun.exe \\MYSERVER -t 120 powershell.exe "-Command Get-Process"
```

### Interactive vs. captured-output mode

RemoteRun detects the mode automatically:

| Situation | Behaviour |
|-----------|-----------|
| Running from a real terminal (stdin is keyboard) | **Interactive** – the child process shares your console; interactive programs like `cmd.exe` or `PowerShell` work fully. You can type commands normally. |
| stdin is piped / redirected | **Captured** – stdout and stderr are captured via pipes and forwarded to the caller; useful for scripting. |

---

## Building

```bat
:: Debug build
dotnet build RemoteRun\RemoteRun.csproj

:: Release build
dotnet build -c Release RemoteRun\RemoteRun.csproj

:: Publish as a self-contained single-file exe (no runtime required on target)
dotnet publish RemoteRun\RemoteRun.csproj -c Release -r win-x64 ^
    -p:PublishSingleFile=true -p:SelfContained=true
```

The self-contained output is placed in
`RemoteRun\bin\Release\net8.0-windows\win-x64\publish\RemoteRun.exe`.

---

## Project structure

| File | Purpose |
|---|---|
| `RemoteRun.csproj` | .NET 8 project targeting `net8.0-windows` |
| `Program.cs` | Entry point, argument dispatch, usage text |
| `Options.cs` | Command-line argument model and parser |
| `NativeApi.cs` | All Win32 P/Invoke declarations |
| `TokenRunner.cs` | Local SYSTEM execution via token duplication |
| `ServiceRunner.cs` | Service install/start/wait/cleanup (local fallback + remote) |
| `ServiceWorker.cs` | In-process SCM service dispatcher and command executor |

---

## Security notes

- RemoteRun requires you to **already be an Administrator**; it does not
  bypass UAC or perform any unauthorized privilege escalation.
- Admin → SYSTEM elevation is the same mechanism used by Windows itself
  (e.g., Task Scheduler, PsExec, AdvancedRun).
- Temporary files written to disk contain the plain-text command and its
  output.  They are deleted immediately after the run completes.
- The temporary service is removed from the SCM on every run (success or
  failure).
