using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RemoteRun;

/// <summary>
/// Runs a process as NT AUTHORITY\SYSTEM on the <em>local</em> machine by
/// duplicating the primary token of an existing SYSTEM process (e.g. winlogon.exe).
///
/// Requirements: the caller must be running as a member of the local
/// Administrators group.  SeDebugPrivilege and SeImpersonatePrivilege are
/// enabled automatically if available.
/// </summary>
internal static class TokenRunner
{
    /// <summary>
    /// Attempt to launch <paramref name="options"/> as SYSTEM using token
    /// duplication.  Stdout / stderr are captured and written to the console.
    /// </summary>
    /// <returns>
    /// <c>true</c> + <paramref name="exitCode"/> on success;
    /// <c>false</c> when the method cannot proceed (caller should fall back to
    /// the service approach).
    /// </returns>
    public static bool TryRunAsSystem(Options options, out int exitCode)
    {
        exitCode = -1;

        // 1. Enable required privileges on the current process token.
        if (!EnablePrivilege(NativeApi.SE_DEBUG_NAME))
        {
            Console.Error.WriteLine("[!] Cannot enable SeDebugPrivilege. Are you running as Administrator?");
            return false;
        }
        EnablePrivilege(NativeApi.SE_IMPERSONATE_NAME);
        EnablePrivilege(NativeApi.SE_ASSIGN_PRIMARY_TOKEN_NAME);

        // 2. Find a SYSTEM process whose token we can borrow.
        IntPtr hSystemToken = FindSystemToken();
        if (hSystemToken == IntPtr.Zero)
        {
            Console.Error.WriteLine("[!] Could not obtain a SYSTEM token.");
            return false;
        }

        try
        {
            // 3. Duplicate it as a primary token.
            if (!NativeApi.DuplicateTokenEx(
                    hSystemToken,
                    NativeApi.TOKEN_ALL_ACCESS,
                    IntPtr.Zero,
                    NativeApi.SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                    NativeApi.TOKEN_TYPE.TokenPrimary,
                    out IntPtr hPrimaryToken))
            {
                Console.Error.WriteLine($"[!] DuplicateTokenEx failed: {LastError()}");
                return false;
            }

            try
            {
                exitCode = LaunchWithToken(hPrimaryToken, options);
                return true;
            }
            finally
            {
                NativeApi.CloseHandle(hPrimaryToken);
            }
        }
        finally
        {
            NativeApi.CloseHandle(hSystemToken);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Iterate running processes until we find one whose token belongs to
    /// NT AUTHORITY\SYSTEM, then return a duplicate of that token.
    /// </summary>
    private static IntPtr FindSystemToken()
    {
        // Preferred candidates in order (more stable, always present)
        string[] preferred = { "winlogon", "services", "lsass", "wininit" };

        // First, try the preferred list in order.
        foreach (string name in preferred)
        {
            foreach (Process proc in Process.GetProcessesByName(name))
            {
                IntPtr tok = TryGetSystemToken(proc.Id);
                if (tok != IntPtr.Zero)
                    return tok;
            }
        }

        // Fallback: scan all processes.
        foreach (Process proc in Process.GetProcesses())
        {
            IntPtr tok = TryGetSystemToken(proc.Id);
            if (tok != IntPtr.Zero)
                return tok;
        }

        return IntPtr.Zero;
    }

    private static IntPtr TryGetSystemToken(int pid)
    {
        IntPtr hProcess = NativeApi.OpenProcess(NativeApi.PROCESS_QUERY_INFORMATION, false, pid);
        if (hProcess == IntPtr.Zero)
            return IntPtr.Zero;

        try
        {
            if (!NativeApi.OpenProcessToken(hProcess,
                    NativeApi.TOKEN_QUERY | NativeApi.TOKEN_DUPLICATE,
                    out IntPtr hToken))
                return IntPtr.Zero;

            try
            {
                if (IsSystemToken(hToken))
                    return hToken;

                // Not a SYSTEM token – close the handle before returning.
                NativeApi.CloseHandle(hToken);
                return IntPtr.Zero;
            }
            catch
            {
                NativeApi.CloseHandle(hToken);
                return IntPtr.Zero;
            }
        }
        finally
        {
            NativeApi.CloseHandle(hProcess);
        }
    }

    /// <summary>
    /// Returns true when <paramref name="hToken"/> belongs to NT AUTHORITY\SYSTEM.
    /// </summary>
    private static bool IsSystemToken(IntPtr hToken)
    {
        // Query TOKEN_USER to get the user SID.
        NativeApi.GetTokenInformation(
            hToken, NativeApi.TOKEN_INFORMATION_CLASS.TokenUser,
            IntPtr.Zero, 0, out uint needed);

        if (needed == 0) return false;

        IntPtr buf = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!NativeApi.GetTokenInformation(
                    hToken, NativeApi.TOKEN_INFORMATION_CLASS.TokenUser,
                    buf, needed, out _))
                return false;

            var tu  = Marshal.PtrToStructure<NativeApi.TOKEN_USER>(buf);
            return NativeApi.IsWellKnownSid(tu.User.Sid, NativeApi.WELL_KNOWN_SID_TYPE.WinLocalSystemSid);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    /// <summary>
    /// Create the target process using <paramref name="hToken"/> and optionally
    /// capture its output.
    /// </summary>
    private static int LaunchWithToken(IntPtr hToken, Options options)
    {
        // Build pipes for stdout / stderr so we can relay them to the console.
        var sa = new NativeApi.SECURITY_ATTRIBUTES
        {
            nLength        = Marshal.SizeOf<NativeApi.SECURITY_ATTRIBUTES>(),
            bInheritHandle = true,
        };

        if (!NativeApi.CreatePipe(out IntPtr hOutRead, out IntPtr hOutWrite, ref sa, 0))
            throw new InvalidOperationException($"CreatePipe (stdout) failed: {LastError()}");
        if (!NativeApi.CreatePipe(out IntPtr hErrRead, out IntPtr hErrWrite, ref sa, 0))
        {
            NativeApi.CloseHandle(hOutRead);
            NativeApi.CloseHandle(hOutWrite);
            throw new InvalidOperationException($"CreatePipe (stderr) failed: {LastError()}");
        }

        // The read ends must NOT be inherited by the child process.
        NativeApi.SetHandleInformation(hOutRead, NativeApi.HANDLE_FLAG_INHERIT, 0);
        NativeApi.SetHandleInformation(hErrRead, NativeApi.HANDLE_FLAG_INHERIT, 0);

        string cmdLine = BuildCommandLine(options.Program, options.Arguments);

        var si = new NativeApi.STARTUPINFO
        {
            cb         = Marshal.SizeOf<NativeApi.STARTUPINFO>(),
            dwFlags    = NativeApi.STARTF_USESTDHANDLES | NativeApi.STARTF_USESHOWWINDOW,
            wShowWindow = NativeApi.SW_HIDE,
            hStdInput  = IntPtr.Zero,
            hStdOutput = hOutWrite,
            hStdError  = hErrWrite,
        };

        bool ok = NativeApi.CreateProcessWithTokenW(
            hToken,
            0,                                  // dwLogonFlags
            null,                               // lpApplicationName
            cmdLine,
            NativeApi.CREATE_NO_WINDOW | NativeApi.NORMAL_PRIORITY_CLASS,
            IntPtr.Zero,                        // environment (inherit)
            options.WorkingDirectory,
            ref si,
            out NativeApi.PROCESS_INFORMATION pi);

        // Close write ends in the parent so ReadFile sees EOF when child exits.
        NativeApi.CloseHandle(hOutWrite);
        NativeApi.CloseHandle(hErrWrite);

        if (!ok)
        {
            NativeApi.CloseHandle(hOutRead);
            NativeApi.CloseHandle(hErrRead);
            throw new InvalidOperationException($"CreateProcessWithTokenW failed: {LastError()}");
        }

        NativeApi.CloseHandle(pi.hThread);

        // Relay output on background threads while we wait.
        var outThread = new System.Threading.Thread(() => RelayPipe(hOutRead, Console.Out));
        var errThread = new System.Threading.Thread(() => RelayPipe(hErrRead, Console.Error));
        outThread.IsBackground = true;
        errThread.IsBackground = true;
        outThread.Start();
        errThread.Start();

        int exitCode = 0;
        if (!options.NoWait)
        {
            uint waitMs = options.TimeoutSeconds > 0
                ? (uint)(options.TimeoutSeconds * 1000)
                : NativeApi.INFINITE;

            uint waitResult = NativeApi.WaitForSingleObject(pi.hProcess, waitMs);
            if (waitResult == NativeApi.WAIT_TIMEOUT)
                Console.Error.WriteLine("[!] Process timed out.");

            NativeApi.GetExitCodeProcess(pi.hProcess, out uint code);
            exitCode = (int)code;

            outThread.Join(3_000);
            errThread.Join(3_000);
        }

        NativeApi.CloseHandle(pi.hProcess);
        return exitCode;
    }

    private static void RelayPipe(IntPtr hRead, System.IO.TextWriter dest)
    {
        byte[] buf = new byte[4096];
        while (true)
        {
            bool ok = NativeApi.ReadFile(hRead, buf, (uint)buf.Length, out uint read, IntPtr.Zero);
            if (!ok || read == 0) break;
            dest.Write(Encoding.UTF8.GetString(buf, 0, (int)read));
            dest.Flush();
        }
        NativeApi.CloseHandle(hRead);
    }

    // ── Privilege helpers ──────────────────────────────────────────────────

    internal static bool EnablePrivilege(string privilegeName)
    {
        if (!NativeApi.OpenProcessToken(
                Process.GetCurrentProcess().Handle,
                TOKEN_ADJUST_PRIVILEGES | NativeApi.TOKEN_QUERY,
                out IntPtr hToken))
            return false;

        try
        {
            if (!NativeApi.LookupPrivilegeValue(null, privilegeName, out NativeApi.LUID luid))
                return false;

            var tp = new NativeApi.TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privileges = new[]
                {
                    new NativeApi.LUID_AND_ATTRIBUTES
                    {
                        Luid       = luid,
                        Attributes = NativeApi.SE_PRIVILEGE_ENABLED,
                    }
                },
            };

            NativeApi.AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            return Marshal.GetLastWin32Error() == 0;
        }
        finally
        {
            NativeApi.CloseHandle(hToken);
        }
    }

    // TOKEN_ADJUST_PRIVILEGES is 0x0020, not declared in NativeApi to keep it tidy.
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;

    // ── Formatting helpers ─────────────────────────────────────────────────

    internal static string BuildCommandLine(string program, string? arguments)
    {
        // Quote the executable path if it contains spaces.
        string quoted = program.Contains(' ') ? $"\"{program}\"" : program;
        return string.IsNullOrEmpty(arguments) ? quoted : $"{quoted} {arguments}";
    }

    private static string LastError() =>
        $"Win32 error {Marshal.GetLastWin32Error()}";
}
