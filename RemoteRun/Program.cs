using System.Diagnostics;
using System.Security.Principal;

namespace RemoteRun;

/// <summary>
/// RemoteRun – Lightweight remote command tool.
///
/// Runs a program as NT AUTHORITY\SYSTEM on the local machine or on a
/// remote machine (via a temporary Windows service), similar to PsExec
/// and AdvancedRun.
///
/// Usage:
///   RemoteRun.exe [options] program [arguments]
///   RemoteRun.exe [options] \\computer program [arguments]
///
/// Two privilege-elevation methods are used:
///   Local   – Token duplication from an existing SYSTEM process (fast, no service).
///   Remote  – Temporary Windows service installed via the remote admin share.
/// </summary>
internal static class Program
{
    static int Main(string[] args)
    {
        // ── Internal service-mode invocation by SCM ────────────────────────
        // When SCM starts the service it passes: --service --workdir <dir>
        if (args.Length > 0 && args[0] == "--service")
        {
            ServiceWorker.Run(args);
            return 0;
        }

        // ── Auto-elevate if not running as Administrator ───────────────────
        // Re-launch via ShellExecute with the "runas" verb so Windows shows
        // the UAC prompt, then wait for the elevated copy to finish.
        if (!IsAdministrator())
            return Elevate(args);

        // ── Parse user-facing arguments ────────────────────────────────────
        Options? options = Options.Parse(args);
        if (options == null)
        {
            PrintUsage();
            return 1;
        }

        if (options.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        // ── Dispatch ───────────────────────────────────────────────────────
        try
        {
            return options.RemoteComputer != null
                ? RunRemote(options)
                : RunLocal(options);
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"[!] Access denied: {ex.Message}");
            Console.Error.WriteLine("[!] Ensure you are running as Administrator.");
            return 5;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[!] Error: {ex.Message}");
            return 1;
        }
    }

    // ── Local ──────────────────────────────────────────────────────────────

    private static int RunLocal(Options options)
    {
        Console.Error.WriteLine($"[*] Running '{options.Program}' as NT AUTHORITY\\SYSTEM (local)...");

        // Fast path: token duplication (no service required).
        if (TokenRunner.TryRunAsSystem(options, out int exitCode))
            return exitCode;

        // Fallback: temporary service.
        Console.Error.WriteLine("[*] Token method unavailable, falling back to service method...");
        return ServiceRunner.RunLocal(options);
    }

    // ── Remote ─────────────────────────────────────────────────────────────

    private static int RunRemote(Options options)
    {
        Console.Error.WriteLine(
            $"[*] Running '{options.Program}' on \\\\{options.RemoteComputer} as NT AUTHORITY\\SYSTEM...");
        return ServiceRunner.RunRemote(options);
    }

    // ── Elevation helpers ──────────────────────────────────────────────────

    /// <summary>Returns true when the current process has Administrator privileges.</summary>
    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Re-launches this executable with the "runas" ShellExecute verb so that
    /// Windows shows a UAC elevation prompt.  Waits for the elevated process to
    /// finish and returns its exit code.
    /// </summary>
    private static int Elevate(string[] args)
    {
        string? exe = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName;

        if (string.IsNullOrEmpty(exe))
        {
            Console.Error.WriteLine("[!] Cannot determine the executable path for elevation.");
            return 1;
        }

        // Re-join arguments, re-quoting any that contain spaces.
        string arguments = string.Join(" ", args.Select(
            a => a.Contains(' ') ? $"\"{a}\"" : a));

        var psi = new ProcessStartInfo
        {
            FileName        = exe,
            Arguments       = arguments,
            Verb            = "runas",   // triggers UAC elevation prompt
            UseShellExecute = true,
        };

        try
        {
            using Process? proc = Process.Start(psi);
            if (proc == null) return 0;
            proc.WaitForExit();
            return proc.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The user clicked "No" on the UAC prompt.
            Console.Error.WriteLine(
                "[!] Elevation cancelled. Administrator privileges are required.");
            return 5;
        }
    }

    // ── Usage ──────────────────────────────────────────────────────────────

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            RemoteRun v1.0 - Lightweight remote command tool
            Run programs as NT AUTHORITY\SYSTEM locally or on remote machines.
            Requires Administrator privileges.

            Usage:
              RemoteRun.exe [options] program [arguments]
              RemoteRun.exe [options] \\computer program [arguments]

            Options:
              -w <directory>   Set working directory for the launched process
              -d               Don't wait for the process to finish
              -t <seconds>     Timeout in seconds (default: 60; 0 = unlimited)
              -h, --help       Show this help

            Examples:
              RemoteRun.exe                                        (open SYSTEM cmd.exe)
              RemoteRun.exe cmd.exe                              (interactive shell)
              RemoteRun.exe cmd.exe "/c whoami /all"            (capture output)
              RemoteRun.exe -w "C:\Windows\System32" cmd.exe "/c dir"
              RemoteRun.exe \\192.168.1.100 cmd.exe "/c ipconfig /all"
              RemoteRun.exe \\MYSERVER -t 120 powershell.exe "-Command Get-Process"

            Notes:
              • Running with no arguments opens an interactive NT AUTHORITY\SYSTEM
                command prompt (cmd.exe). Administrator privileges are required.
              • If not running as Administrator, a UAC elevation prompt is shown
                automatically so no manual "Run as Administrator" is needed.
              • Interactive vs. captured output is detected automatically.
                When stdin is a real console (not piped), the child process
                inherits the terminal so interactive programs like cmd.exe or
                PowerShell work fully (you can type commands normally).
                When stdin is redirected/piped, stdout and stderr are captured
                and forwarded — suitable for scripting and automation.
              • Local execution uses token duplication from an existing SYSTEM
                process (e.g. winlogon.exe) for maximum speed.
              • Remote execution copies this executable to the target machine's
                admin share (\\computer\admin$), installs a temporary Windows
                service, and removes everything on completion.
              • Full admin rights on the remote machine are required for remote
                execution (same requirement as PsExec).
            """);
    }
}
