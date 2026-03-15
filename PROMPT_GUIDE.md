# RemoteRun — Prompt Guide

> This file accumulates context-rich prompt templates for working with this codebase.
> Each session appends a new dated section. Never delete old sections.
> Use these as the opening context when starting a new Copilot/LLM session.

---

## ── How to use this file ─────────────────────────────────────────────────────

When starting a new session, paste the **Context Block** below first, then
append your specific request. Templates in **Section 3** cover the most common
task categories with the exact phrasing that produces the best results.

---

## ── Session 1 · 2026-03-15 ──────────────────────────────────────────────────

### 1. Context Block (paste at the start of every session)

```
App: RemoteRun
Language: C# (.NET 8.0-windows), InnoSetup
Project root: d:\Projects\WinProjects\AdvancedRun-Rework\
Solution: RemoteRun.slnx

Key files:
  Program.cs        – entry point, UAC auto-elevation, dispatch
  Options.cs        – immutable CLI options + parser
  NativeApi.cs      – ALL Win32 P/Invoke (advapi32 + kernel32)
  TokenRunner.cs    – local SYSTEM execution via token duplication (fast path)
  ServiceRunner.cs  – temporary Windows service installer/runner (local + remote)
  ServiceWorker.cs  – SCM service worker (reads config.ini, runs command, writes output)
  setup.iss         – InnoSetup dual-arch (x64/x86) installer

Architecture:
  - Single self-contained exe; dual-mode (client vs. SCM service via --service flag).
  - Local fast path: DuplicateTokenEx from winlogon/services/lsass → CreateProcessWithTokenW.
  - Local/remote fallback: GUID-named temporary service + file-based IPC
    (config.ini / output.txt / exitcode.txt in %TEMP%\RemoteRun_<GUID>).
  - Remote: copies exe to \\machine\admin$, installs service via remote SCM.
  - Interactive vs. capture auto-detected via Console.IsInputRedirected.

Build:
  dotnet publish -c Release -r win-x64 --self-contained true
  ISCC.exe /DMyAppArch=x64 setup.iss

Version: 1.0 · Author: LoveDoLove
```

---

### 2. Quick-Reference Prompt Starters

Use one of these as-is or as a base. Replace `<…>` placeholders.

#### 2a. Bug fix
```
[RemoteRun context above]

Bug: <describe the symptom and where it manifests — file + function>.
Reproduce: <exact command or scenario>.
Fix it. Show only the changed lines with 3 lines of surrounding context.
```

#### 2b. New feature
```
[RemoteRun context above]

Feature: <concise description>.
Constraints:
  - Keep the "no external dependencies" rule.
  - All new Win32 APIs must go in NativeApi.cs.
  - New CLI flags must be added to Options.cs (parser) and PrintUsage() in Program.cs.
  - Do not break the existing interactive-detection logic in TokenRunner.

Implement it. Show diffs only; do not reprint unchanged code.
```

#### 2c. Refactor / improve
```
[RemoteRun context above]

Refactor goal: <what you want to clean up or improve>.
Rules:
  - No behavior changes.
  - No new abstractions unless strictly necessary.
  - Keep all P/Invoke in NativeApi.cs.
Show only changed sections.
```

#### 2d. Security review
```
[RemoteRun context above]

Perform a security review scoped to: <TokenRunner | ServiceRunner | all>.
Focus on: privilege escalation paths, temp-file race conditions,
command-line injection, handle leaks, unexpected SYSTEM process targeting.
List findings as: [SEVERITY] File.cs:functionName — description → fix.
```

#### 2e. Add a CLI flag
```
[RemoteRun context above]

Add a new CLI flag: <flag name, e.g. -o / --output-file>.
Behavior: <what it does>.
Touch: Options.cs (add property + parser case), Program.cs PrintUsage(),
and wherever the flag affects behavior.
Show only the changed hunks.
```

#### 2f. Installer change (setup.iss)
```
[RemoteRun context above]

Change the InnoSetup installer (setup.iss):
<describe the change — new shortcut, registry entry, post-install action, etc.>.
Show only the changed/added lines in the ISS file.
```

#### 2g. Extend remote execution
```
[RemoteRun context above]

Remote execution currently relies on \\machine\admin$ and the current user's
network credentials. I want to: <describe the extension — credential passthrough,
alternate share, WMI alternative, etc.>.
List the Win32 APIs needed, add them to NativeApi.cs, then implement.
```

#### 2h. Output / encoding fix
```
[RemoteRun context above]

Problem: captured output (output.txt) uses UTF-8 but child programs may write
OEM/ANSI codepage text. Fix so that the actual console codepage of the child
is used for decoding.
Affected files: ServiceWorker.cs, TokenRunner.cs (RelayPipe).
Constraints:
  - Do not use managed Process/StreamReader; stay on raw ReadFile P/Invoke.
Show changed code only.
```

#### 2i. Add stdin piping
```
[RemoteRun context above]

Currently hStdInput = IntPtr.Zero in all non-interactive code paths.
Add stdin piping so that the caller can pipe data into the SYSTEM process.
Touch: NativeApi.cs (no new imports needed), TokenRunner.LaunchWithToken(),
ServiceWorker.ExecuteCommand().
Show changed sections only.
```

#### 2j. Version bump
```
[RemoteRun context above]

Bump the version to <X.Y>.
Touch: RemoteRun.csproj (AssemblyVersion, FileVersion), Program.cs PrintUsage()
header line, setup.iss #define MyAppVersion.
Show the three changed lines only.
```

---

### 3. Reusable Constraints (append to any prompt)

```
Constraints (always apply):
  - No external NuGet packages.
  - Windows-only: net8.0-windows, no cross-platform abstractions.
  - All Win32 P/Invoke stays in NativeApi.cs.
  - Dual-mode exe must remain: the same binary must work as client AND SCM service.
  - GUID temp directories for all IPC; clean up in finally blocks.
  - Interactive mode detection: !Console.IsInputRedirected && !Console.IsOutputRedirected.
  - Overflow guard on TimeoutSeconds * 1000 → uint.
  - Service polling interval: 250 ms.
  - Timeout for SERVICE_RUNNING wait: 10 000 ms (hardcoded).
```

---

### 4. File Editing Rules (append when doing code edits)

```
Editing rules:
  - Show only changed hunks with 3–5 lines of surrounding context.
  - Do not reprint entire files.
  - Do not add docstrings or comments to code you did not change.
  - One logical change per code block.
  - Use multi_replace_string_in_file for multiple independent changes.
```

---

### 5. Things to Know Before Asking

| Topic | Detail |
|---|---|
| Service name derivation | `RemoteRunSvc_<GUID>` — GUID taken from temp dir name suffix |
| Admin share path math | Local: `%TEMP%\RemoteRun_<GUID>` / Remote: `%SystemRoot%\RemoteRun_<GUID>` mapped via `admin$` |
| Why `--service` is internal | SCM invokes the same exe with `--service --workdir <dir>`; `Program.cs` detects this before any other parsing |
| Why token duplication is preferred | No service install/delete overhead; sub-100ms for local execution |
| Service binary path quoting | `"<exePath>" --service --workdir "<dir>"` — spaces in paths handled via outer quotes |
| Output merge order | ServiceWorker: stdout first, stderr appended on a new line |
| `NoWait` + service | Output is never collected; `exitcode.txt` may not exist → `ReadExitCode` returns -1 |
| UAC approach | Runtime ShellExecute "runas" — no embedded manifest required |
| Encoding | UTF-8 end-to-end; OEM console codepage output will be mis-decoded (known limitation) |
| Installer AppId | `{F3C4B2A1-D5E6-4F7A-8B9C-0D1E2F3A4B5C}` — do not change across versions |

---

### 6. Suggested Future Improvements (not yet implemented)

These are things the project does not yet do, flagged for future prompting:

- [ ] **Credential passthrough**: `-u <user> -p <pass>` for remote execution (needs `WNetAddConnection2` or `LogonUser` + `ImpersonateLoggedOnUser`)
- [ ] **stdin piping**: pipe data into the SYSTEM child process (currently `hStdInput = IntPtr.Zero`)
- [ ] **Output codepage**: auto-detect child process console codepage and decode accordingly
- [ ] **Separate stderr**: write `stderr.txt` in addition to `output.txt` so callers can distinguish
- [ ] **ARM64 support**: add `win-arm64` publish profile and ISS arch entry
- [ ] **Log file option**: `-l <file>` to write output to a file instead of stdout
- [ ] **Multiple targets**: `\\host1 \\host2 cmd.exe` — fan-out remote execution
- [ ] **Session targeting**: run in a specific logon session (RDP, console session 0, etc.)
- [ ] **Code-sign the exe**: add sign step in publish workflow for SmartScreen bypass
- [ ] **GitHub Actions CI**: automate `dotnet publish` + ISCC + artifact upload

---
