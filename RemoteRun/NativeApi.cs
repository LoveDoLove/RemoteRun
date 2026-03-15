using System;
using System.Runtime.InteropServices;

namespace RemoteRun;

/// <summary>
/// All Win32 / native-API declarations used by RemoteRun.
/// </summary>
internal static class NativeApi
{
    // ── Process access rights ──────────────────────────────────────────────
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;

    // ── Token access rights ───────────────────────────────────────────────
    public const uint TOKEN_DUPLICATE            = 0x0002;
    public const uint TOKEN_QUERY               = 0x0008;
    public const uint TOKEN_ASSIGN_PRIMARY      = 0x0001;
    public const uint TOKEN_IMPERSONATE         = 0x0004;
    public const uint TOKEN_ALL_ACCESS          = 0xF01FF;

    // ── Service-manager access rights ─────────────────────────────────────
    public const uint SC_MANAGER_ALL_ACCESS     = 0xF003F;
    public const uint SC_MANAGER_CREATE_SERVICE = 0x0002;
    public const uint SC_MANAGER_CONNECT        = 0x0001;

    // ── Service access rights ─────────────────────────────────────────────
    public const uint SERVICE_ALL_ACCESS        = 0xF01FF;
    public const uint SERVICE_QUERY_STATUS      = 0x0004;
    public const uint SERVICE_START             = 0x0010;
    public const uint SERVICE_STOP              = 0x0020;

    // ── Service configuration ─────────────────────────────────────────────
    public const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;
    public const uint SERVICE_DEMAND_START      = 0x00000003;
    public const uint SERVICE_ERROR_NORMAL      = 0x00000001;

    // ── Service current-state values ──────────────────────────────────────
    public const uint SERVICE_STOPPED           = 0x00000001;
    public const uint SERVICE_START_PENDING     = 0x00000002;
    public const uint SERVICE_STOP_PENDING      = 0x00000003;
    public const uint SERVICE_RUNNING           = 0x00000004;

    // ── Service control codes ─────────────────────────────────────────────
    public const uint SERVICE_CONTROL_STOP      = 0x00000001;

    // ── Process-creation flags ────────────────────────────────────────────
    public const uint CREATE_NO_WINDOW          = 0x08000000;
    public const uint NORMAL_PRIORITY_CLASS     = 0x00000020;

    // ── STARTUPINFO flags ─────────────────────────────────────────────────
    public const uint STARTF_USESTDHANDLES      = 0x00000100;
    public const uint STARTF_USESHOWWINDOW      = 0x00000001;
    public const short SW_HIDE                  = 0;

    // ── Handle flags ──────────────────────────────────────────────────────
    public const uint HANDLE_FLAG_INHERIT       = 0x00000001;

    // ── WaitForSingleObject ───────────────────────────────────────────────
    public const uint INFINITE                  = 0xFFFFFFFF;
    public const uint WAIT_OBJECT_0             = 0x00000000;
    public const uint WAIT_TIMEOUT              = 0x00000102;
    public const uint WAIT_FAILED               = 0xFFFFFFFF;

    // ── Privilege names ───────────────────────────────────────────────────
    public const string SE_DEBUG_NAME                 = "SeDebugPrivilege";
    public const string SE_IMPERSONATE_NAME           = "SeImpersonatePrivilege";
    public const string SE_ASSIGN_PRIMARY_TOKEN_NAME  = "SeAssignPrimaryTokenPrivilege";
    public const uint   SE_PRIVILEGE_ENABLED          = 0x00000002;

    // ── Well-known SID types ──────────────────────────────────────────────
    public enum WELL_KNOWN_SID_TYPE
    {
        WinLocalSystemSid    = 22,
        WinLocalServiceSid   = 23,
        WinNetworkServiceSid = 24,
    }

    // ── Security impersonation level ──────────────────────────────────────
    public enum SECURITY_IMPERSONATION_LEVEL
    {
        SecurityAnonymous      = 0,
        SecurityIdentification = 1,
        SecurityImpersonation  = 2,
        SecurityDelegation     = 3,
    }

    // ── Token type ────────────────────────────────────────────────────────
    public enum TOKEN_TYPE
    {
        TokenPrimary       = 1,
        TokenImpersonation = 2,
    }

    // ── Token information class ───────────────────────────────────────────
    public enum TOKEN_INFORMATION_CLASS
    {
        TokenUser      = 1,
        TokenGroups    = 2,
        TokenPrivileges = 3,
    }

    // ────────────────────────────────── Structures ────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SECURITY_ATTRIBUTES
    {
        public int    nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool   bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int    cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public uint   dwX;
        public uint   dwY;
        public uint   dwXSize;
        public uint   dwYSize;
        public uint   dwXCountChars;
        public uint   dwYCountChars;
        public uint   dwFillAttribute;
        public uint   dwFlags;
        public short  wShowWindow;
        public short  cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int    dwProcessId;
        public int    dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public LUID_AND_ATTRIBUTES[] Privileges;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int  HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_USER
    {
        public SID_AND_ATTRIBUTES User;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SID_AND_ATTRIBUTES
    {
        public IntPtr Sid;
        public uint   Attributes;
    }

    // Service dispatch table (null-terminated array required by SCM)
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SERVICE_TABLE_ENTRY
    {
        public string? lpServiceName;
        public ServiceMainCallback? lpServiceProc;
    }

    public delegate void ServiceMainCallback(uint dwNumServicesArgs, IntPtr lpServiceArgVectors);
    public delegate void ServiceControlHandler(uint dwControl);

    // ────────────────────────────────── P/Invoke ──────────────────────────

    // Service Control Manager
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr OpenSCManager(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateService(
        IntPtr hSCManager, string lpServiceName, string lpDisplayName,
        uint dwDesiredAccess, uint dwServiceType, uint dwStartType,
        uint dwErrorControl, string lpBinaryPathName,
        string? lpLoadOrderGroup, IntPtr lpdwTagId,
        string? lpDependencies, string? lpServiceStartName, string? lpPassword);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool StartService(IntPtr hService, uint dwNumServiceArgs, string[]? lpServiceArgVectors);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool QueryServiceStatus(IntPtr hService, out SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool ControlService(IntPtr hService, uint dwControl, out SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool DeleteService(IntPtr hService);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool CloseServiceHandle(IntPtr hSCObject);

    // Service worker (used when running as SCM service)
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool StartServiceCtrlDispatcher(SERVICE_TABLE_ENTRY[] lpServiceTable);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr RegisterServiceCtrlHandler(string lpServiceName, ServiceControlHandler lpHandlerProc);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool SetServiceStatus(IntPtr hServiceStatus, ref SERVICE_STATUS lpServiceStatus);

    // Process & token
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool DuplicateTokenEx(
        IntPtr hExistingToken, uint dwDesiredAccess, IntPtr lpTokenAttributes,
        SECURITY_IMPERSONATION_LEVEL ImpersonationLevel, TOKEN_TYPE TokenType,
        out IntPtr phNewToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcessWithTokenW(
        IntPtr hToken, uint dwLogonFlags,
        string? lpApplicationName, string? lpCommandLine,
        uint dwCreationFlags, IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcess(
        string? lpApplicationName, string? lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        bool bInheritHandles, uint dwCreationFlags,
        IntPtr lpEnvironment, string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    // Token information
    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetTokenInformation(
        IntPtr TokenHandle, TOKEN_INFORMATION_CLASS TokenInformationClass,
        IntPtr TokenInformation, uint TokenInformationLength, out uint ReturnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool IsWellKnownSid(IntPtr pSid, WELL_KNOWN_SID_TYPE WellKnownSidType);

    // Privileges
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool AdjustTokenPrivileges(
        IntPtr TokenHandle, bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState, uint BufferLength,
        IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool LookupPrivilegeValue(
        string? lpSystemName, string lpName, out LUID lpLuid);

    // Pipes
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CreatePipe(
        out IntPtr hReadPipe, out IntPtr hWritePipe,
        ref SECURITY_ATTRIBUTES lpPipeAttributes, uint nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(
        IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead, IntPtr lpOverlapped);
}
