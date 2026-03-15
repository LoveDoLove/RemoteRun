using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace RemoteRun;

/// <summary>
/// Runs a process as NT AUTHORITY\SYSTEM by installing a temporary Windows
/// service that executes the command on behalf of the caller.
///
/// Used for:
///   • Local execution when the token-duplication approach fails.
///   • Remote execution (the service is installed on the remote machine via
///     the admin share and the remote Service Control Manager).
///
/// Communication between the installer (this class) and the service worker
/// (<see cref="ServiceWorker"/>) uses a temporary directory:
///   %TEMP%\RemoteRun_&lt;GUID&gt;  (local)
///   \\machine\admin$\RemoteRun_&lt;GUID&gt;  (remote, maps to %SystemRoot%\...)
///
/// Directory layout:
///   config.ini   – command + arguments + working directory
///   output.txt   – captured stdout + stderr (written by service)
///   exitcode.txt – decimal exit code (written by service)
/// </summary>
internal static class ServiceRunner
{
    private const string ServiceBaseName = "RemoteRunSvc";
    private const string RemoteExeName   = "RemoteRun.exe";

    // ── Public entry points ────────────────────────────────────────────────

    /// <summary>Run a command locally as SYSTEM using a temporary service.</summary>
    public static int RunLocal(Options options)
    {
        string exePath  = System.Reflection.Assembly.GetExecutingAssembly().Location;
        string guid     = Guid.NewGuid().ToString("N");
        string svcName  = $"{ServiceBaseName}_{guid}";
        string workDir  = Path.Combine(Path.GetTempPath(), $"RemoteRun_{guid}");

        Directory.CreateDirectory(workDir);
        try
        {
            WriteConfig(workDir, options);

            // Service binary: "<exePath>" --service --workdir "<workDir>"
            string binPath = $"\"{exePath}\" --service --workdir \"{workDir}\"";

            Console.Error.WriteLine($"[*] Installing temporary service '{svcName}'...");
            InstallAndRun(null, svcName, binPath, workDir, options);

            return ReadExitCode(workDir);
        }
        finally
        {
            SafeDeleteDirectory(workDir);
        }
    }

    /// <summary>
    /// Copy RemoteRun.exe to the remote machine's admin share, install and
    /// start a temporary service there, collect output, then clean up.
    /// </summary>
    public static int RunRemote(Options options)
    {
        string machine  = options.RemoteComputer!;
        string adminShare = $@"\\{machine}\admin$";

        // Verify admin share reachable
        if (!Directory.Exists(adminShare))
            throw new IOException($"Cannot reach admin share {adminShare}. Ensure you have admin access.");

        string guid        = Guid.NewGuid().ToString("N");
        string svcName     = $"{ServiceBaseName}_{guid}";
        string remoteDir   = Path.Combine(adminShare, $"RemoteRun_{guid}");
        string remoteExe   = Path.Combine(adminShare, RemoteExeName);
        // On the remote machine, admin$ maps to %SystemRoot% (e.g. C:\Windows)
        string localWorkDir = $@"%SystemRoot%\RemoteRun_{guid}";

        // 1. Copy this executable to the admin share.
        string localExe = System.Reflection.Assembly.GetExecutingAssembly().Location;
        Console.Error.WriteLine($"[*] Copying {RemoteExeName} to {adminShare}...");
        File.Copy(localExe, remoteExe, overwrite: true);

        Directory.CreateDirectory(remoteDir);
        try
        {
            WriteConfig(remoteDir, options);

            // Service binary path on the remote machine uses %SystemRoot% expansion
            // because the Windows service sub-system expands environment variables.
            string binPath = $@"%SystemRoot%\{RemoteExeName} --service --workdir ""{localWorkDir}""";

            Console.Error.WriteLine($"[*] Installing service '{svcName}' on \\\\{machine}...");
            InstallAndRun(machine, svcName, binPath, remoteDir, options);

            return ReadExitCode(remoteDir);
        }
        finally
        {
            SafeDeleteDirectory(remoteDir);
            try { File.Delete(remoteExe); } catch { /* best effort */ }
        }
    }

    // ── Service lifecycle ──────────────────────────────────────────────────

    private static void InstallAndRun(
        string? machine, string svcName, string binPath,
        string workDir, Options options)
    {
        IntPtr hScm = NativeApi.OpenSCManager(machine, null, NativeApi.SC_MANAGER_ALL_ACCESS);
        if (hScm == IntPtr.Zero)
            throw new UnauthorizedAccessException(
                $"OpenSCManager({machine ?? "local"}) failed: Win32 error {Marshal.GetLastWin32Error()}");

        try
        {
            IntPtr hSvc = NativeApi.CreateService(
                hScm, svcName, "RemoteRun Temporary Service",
                NativeApi.SERVICE_ALL_ACCESS,
                NativeApi.SERVICE_WIN32_OWN_PROCESS,
                NativeApi.SERVICE_DEMAND_START,
                NativeApi.SERVICE_ERROR_NORMAL,
                binPath,
                null, IntPtr.Zero, null, null, null);

            if (hSvc == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"CreateService failed: Win32 error {Marshal.GetLastWin32Error()}");

            try
            {
                Console.Error.WriteLine($"[*] Starting service '{svcName}'...");
                if (!NativeApi.StartService(hSvc, 0, null))
                    throw new InvalidOperationException(
                        $"StartService failed: Win32 error {Marshal.GetLastWin32Error()}");

                // Wait for the service to reach RUNNING state (up to 10 s).
                WaitForState(hSvc, NativeApi.SERVICE_RUNNING, timeoutMs: 10_000);

                if (!options.NoWait)
                {
                    Console.Error.WriteLine("[*] Waiting for service to complete...");
                    // Guard against integer overflow: TimeoutSeconds * 1000 must fit in uint.
                    const int maxTimeoutSeconds = (int)(uint.MaxValue / 1000);
                    uint waitMs = options.TimeoutSeconds > 0
                        ? (uint)(Math.Min(options.TimeoutSeconds, maxTimeoutSeconds) * 1000)
                        : uint.MaxValue;
                    WaitForState(hSvc, NativeApi.SERVICE_STOPPED, timeoutMs: waitMs);

                    // Print captured output.
                    string outputFile = Path.Combine(workDir, "output.txt");
                    if (File.Exists(outputFile))
                        Console.Write(File.ReadAllText(outputFile, Encoding.UTF8));
                }
            }
            finally
            {
                // Always try to stop and delete the service.
                try
                {
                    NativeApi.ControlService(hSvc, NativeApi.SERVICE_CONTROL_STOP, out _);
                    WaitForState(hSvc, NativeApi.SERVICE_STOPPED, timeoutMs: 5_000);
                }
                catch { /* best effort */ }

                NativeApi.DeleteService(hSvc);
                NativeApi.CloseServiceHandle(hSvc);
            }
        }
        finally
        {
            NativeApi.CloseServiceHandle(hScm);
        }
    }

    /// <summary>
    /// Poll <see cref="NativeApi.QueryServiceStatus"/> until the service
    /// reaches <paramref name="targetState"/> or the timeout elapses.
    /// </summary>
    private static void WaitForState(IntPtr hSvc, uint targetState, uint timeoutMs)
    {
        uint elapsed = 0;
        const uint pollInterval = 250; // ms

        while (elapsed < timeoutMs)
        {
            if (!NativeApi.QueryServiceStatus(hSvc, out NativeApi.SERVICE_STATUS status))
                break;

            if (status.dwCurrentState == targetState)
                return;

            Thread.Sleep((int)pollInterval);
            elapsed += pollInterval;
        }
    }

    // ── Config / output helpers ────────────────────────────────────────────

    /// <summary>Write a simple INI-style config that the service worker reads.</summary>
    internal static void WriteConfig(string dir, Options options)
    {
        string cmdLine = TokenRunner.BuildCommandLine(options.Program, options.Arguments);
        var sb = new StringBuilder();
        sb.AppendLine($"CMDLINE={cmdLine}");
        sb.AppendLine($"WORKDIR={options.WorkingDirectory ?? string.Empty}");
        File.WriteAllText(Path.Combine(dir, "config.ini"), sb.ToString(), Encoding.UTF8);
    }

    /// <summary>Read the exit code written by the service worker (defaults to -1).</summary>
    private static int ReadExitCode(string dir)
    {
        string f = Path.Combine(dir, "exitcode.txt");
        if (File.Exists(f) && int.TryParse(File.ReadAllText(f).Trim(), out int code))
            return code;
        return -1;
    }

    private static void SafeDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { /* best effort */ }
    }
}
