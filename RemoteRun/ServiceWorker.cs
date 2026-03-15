using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace RemoteRun;

/// <summary>
/// Implements the Windows service worker that executes the target command when
/// RemoteRun.exe is invoked by the Windows Service Control Manager (SCM).
///
/// Flow:
///   1. SCM starts the process: RemoteRun.exe --service --workdir &lt;dir&gt;
///   2. Program.cs detects --service and calls <see cref="Run"/>.
///   3. This class calls <see cref="NativeApi.StartServiceCtrlDispatcher"/>,
///      which blocks until the service ends.
///   4. Inside <see cref="ServiceMain"/> the config is read, the command is
///      executed, stdout+stderr are captured to &lt;dir&gt;\output.txt, and the
///      exit code is written to &lt;dir&gt;\exitcode.txt.
///   5. The service reports SERVICE_STOPPED to the SCM and returns.
/// </summary>
internal static class ServiceWorker
{
    // Kept as a static field so the GC does not collect the delegate while
    // the unmanaged SCM thread holds a pointer to it.
    private static NativeApi.ServiceMainCallback?    s_serviceMain;
    private static NativeApi.ServiceControlHandler?  s_controlHandler;
    private static IntPtr                            s_hStatus;
    private static string?                           s_workDir;
    private static string?                           s_serviceName;
    private static NativeApi.SERVICE_STATUS          s_status;

    /// <summary>
    /// Entry point called by <c>Program.cs</c> when the exe is started by SCM.
    /// Blocks until the service finishes.
    /// </summary>
    public static void Run(string[] args)
    {
        // Extract the work-directory passed in the service binary path.
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--workdir")
            {
                s_workDir = args[i + 1];
                break;
            }
        }

        // Derive the service name from the work-dir GUID suffix so it matches
        // the name used by ServiceRunner.InstallAndRun.
        if (s_workDir != null)
        {
            string dirName = Path.GetFileName(s_workDir.TrimEnd('\\', '/'));
            s_serviceName = dirName.StartsWith("RemoteRun_")
                ? $"RemoteRunSvc_{dirName["RemoteRun_".Length..]}"
                : "RemoteRunSvc";
        }
        else
        {
            s_serviceName = "RemoteRunSvc";
        }

        s_serviceMain = ServiceMain;

        var table = new NativeApi.SERVICE_TABLE_ENTRY[]
        {
            new() { lpServiceName = s_serviceName, lpServiceProc = s_serviceMain },
            new() { lpServiceName = null,           lpServiceProc = null },
        };

        NativeApi.StartServiceCtrlDispatcher(table);
    }

    // ── SCM callbacks ──────────────────────────────────────────────────────

    private static void ServiceMain(uint argc, IntPtr argv)
    {
        s_controlHandler = ServiceCtrlHandler;
        s_hStatus = NativeApi.RegisterServiceCtrlHandler(s_serviceName!, s_controlHandler);

        // Report SERVICE_START_PENDING
        ReportStatus(NativeApi.SERVICE_START_PENDING, waitHint: 3_000);

        // Report SERVICE_RUNNING
        ReportStatus(NativeApi.SERVICE_RUNNING);

        try
        {
            ExecuteCommand();
        }
        catch (Exception ex)
        {
            // Write the exception message so the caller can diagnose failures.
            if (s_workDir != null)
            {
                try
                {
                    File.AppendAllText(
                        Path.Combine(s_workDir, "output.txt"),
                        $"\r\n[ServiceWorker error] {ex.Message}\r\n",
                        Encoding.UTF8);
                }
                catch { /* best effort */ }
            }
        }
        finally
        {
            ReportStatus(NativeApi.SERVICE_STOPPED);
        }
    }

    private static void ServiceCtrlHandler(uint control)
    {
        if (control == NativeApi.SERVICE_CONTROL_STOP)
            ReportStatus(NativeApi.SERVICE_STOPPED);
    }

    // ── Command execution ──────────────────────────────────────────────────

    private static void ExecuteCommand()
    {
        if (string.IsNullOrEmpty(s_workDir))
            return;

        string configPath = Path.Combine(s_workDir, "config.ini");
        if (!File.Exists(configPath))
            return;

        // Parse config.ini
        string cmdLine   = string.Empty;
        string? workDir  = null;

        foreach (string raw in File.ReadAllLines(configPath, Encoding.UTF8))
        {
            int sep = raw.IndexOf('=');
            if (sep < 0) continue;
            string key = raw[..sep].Trim().ToUpperInvariant();
            string val = raw[(sep + 1)..].TrimEnd('\r', '\n');

            if (key == "CMDLINE") cmdLine = val;
            else if (key == "WORKDIR" && !string.IsNullOrWhiteSpace(val)) workDir = val;
        }

        if (string.IsNullOrWhiteSpace(cmdLine))
            return;

        // Create inheritable pipes for stdout/stderr.
        var sa = new NativeApi.SECURITY_ATTRIBUTES
        {
            nLength        = Marshal.SizeOf<NativeApi.SECURITY_ATTRIBUTES>(),
            bInheritHandle = true,
        };

        NativeApi.CreatePipe(out IntPtr hOutRead, out IntPtr hOutWrite, ref sa, 0);
        NativeApi.CreatePipe(out IntPtr hErrRead, out IntPtr hErrWrite, ref sa, 0);

        // The read ends must not be inherited.
        NativeApi.SetHandleInformation(hOutRead, NativeApi.HANDLE_FLAG_INHERIT, 0);
        NativeApi.SetHandleInformation(hErrRead, NativeApi.HANDLE_FLAG_INHERIT, 0);

        var si = new NativeApi.STARTUPINFO
        {
            cb          = Marshal.SizeOf<NativeApi.STARTUPINFO>(),
            dwFlags     = NativeApi.STARTF_USESTDHANDLES | NativeApi.STARTF_USESHOWWINDOW,
            wShowWindow = NativeApi.SW_HIDE,
            hStdInput   = IntPtr.Zero,
            hStdOutput  = hOutWrite,
            hStdError   = hErrWrite,
        };

        bool created = NativeApi.CreateProcess(
            null, cmdLine,
            IntPtr.Zero, IntPtr.Zero,
            true,
            NativeApi.CREATE_NO_WINDOW | NativeApi.NORMAL_PRIORITY_CLASS,
            IntPtr.Zero,
            workDir,
            ref si,
            out NativeApi.PROCESS_INFORMATION pi);

        // Close write ends in this process so ReadFile returns EOF after child exits.
        NativeApi.CloseHandle(hOutWrite);
        NativeApi.CloseHandle(hErrWrite);

        if (!created)
        {
            NativeApi.CloseHandle(hOutRead);
            NativeApi.CloseHandle(hErrRead);
            WriteOutput($"[Error] CreateProcess failed: Win32 error {Marshal.GetLastWin32Error()}\r\n");
            WriteExitCode(-1);
            return;
        }

        NativeApi.CloseHandle(pi.hThread);

        // Drain pipes on separate threads, then wait for the child process.
        var sbOut = new StringBuilder();
        var sbErr = new StringBuilder();

        var tOut = new Thread(() => DrainPipe(hOutRead, sbOut));
        var tErr = new Thread(() => DrainPipe(hErrRead, sbErr));
        tOut.IsBackground = true;
        tErr.IsBackground = true;
        tOut.Start();
        tErr.Start();

        NativeApi.WaitForSingleObject(pi.hProcess, NativeApi.INFINITE);
        NativeApi.GetExitCodeProcess(pi.hProcess, out uint exitCode);
        NativeApi.CloseHandle(pi.hProcess);

        tOut.Join();
        tErr.Join();

        // Merge stdout and stderr into a single output file.
        var combined = new StringBuilder();
        combined.Append(sbOut);
        if (sbErr.Length > 0)
        {
            if (combined.Length > 0) combined.AppendLine();
            combined.Append(sbErr);
        }
        WriteOutput(combined.ToString());
        WriteExitCode((int)exitCode);
    }

    private static void DrainPipe(IntPtr hRead, StringBuilder dest)
    {
        byte[] buf = new byte[4096];
        while (true)
        {
            bool ok = NativeApi.ReadFile(hRead, buf, (uint)buf.Length, out uint read, IntPtr.Zero);
            if (!ok || read == 0) break;
            dest.Append(Encoding.UTF8.GetString(buf, 0, (int)read));
        }
        NativeApi.CloseHandle(hRead);
    }

    private static void WriteOutput(string text)
    {
        if (s_workDir == null) return;
        try { File.WriteAllText(Path.Combine(s_workDir, "output.txt"), text, Encoding.UTF8); }
        catch { /* best effort */ }
    }

    private static void WriteExitCode(int code)
    {
        if (s_workDir == null) return;
        try { File.WriteAllText(Path.Combine(s_workDir, "exitcode.txt"), code.ToString(), Encoding.UTF8); }
        catch { /* best effort */ }
    }

    // ── Status helper ──────────────────────────────────────────────────────

    private static void ReportStatus(uint state, uint waitHint = 0)
    {
        s_status = new NativeApi.SERVICE_STATUS
        {
            dwServiceType             = NativeApi.SERVICE_WIN32_OWN_PROCESS,
            dwCurrentState            = state,
            dwControlsAccepted        = state == NativeApi.SERVICE_RUNNING
                                            ? NativeApi.SERVICE_CONTROL_STOP
                                            : 0,
            dwWin32ExitCode           = 0,
            dwServiceSpecificExitCode = 0,
            dwCheckPoint              = 0,
            dwWaitHint                = waitHint,
        };
        NativeApi.SetServiceStatus(s_hStatus, ref s_status);
    }
}
