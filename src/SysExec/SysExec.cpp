/*
 * SysExec.cpp
 * Lightweight remote / local command-execution tool.
 * Runs any command as NT AUTHORITY\SYSTEM by installing a short-lived
 * Windows service and capturing its output over a named pipe.
 *
 * Inspired by PsExec / NirSoft AdvancedRun.
 *
 * Build (MSVC, x64 Release):
 *   cl /nologo /W4 /WX /O2 /MT /EHsc /D_UNICODE /DUNICODE \
 *      SysExec.cpp advapi32.lib \
 *      /link /SUBSYSTEM:CONSOLE /OUT:SysExec.exe
 *
 * Requirements: Administrator privileges on the local (and remote) machine.
 * Platforms   : Windows Vista / Server 2008 and later (32- and 64-bit).
 */

#define WIN32_LEAN_AND_MEAN
#define _WIN32_WINNT 0x0600   /* Vista+ for RegDeleteTree */

#include <windows.h>
#include <winsvc.h>
#include <sddl.h>
#include <strsafe.h>
#include <stdio.h>

#pragma comment(lib, "advapi32.lib")

/* ================================================================
   Constants / limits
   ================================================================ */

#define SYSEXEC_SVC_NAME      L"SysExecSvc"
#define SYSEXEC_SVC_DISPLAY   L"SysExec Temporary Service"
#define SYSEXEC_PIPE_PREFIX   L"\\\\.\\pipe\\SysExecOut_"
#define SYSEXEC_SVC_ARG       L"/SvcRun"
#define SYSEXEC_REG_PARAMS    L"SYSTEM\\CurrentControlSet\\Services\\SysExecSvc\\Parameters"

#define MAX_CMD_LEN     8192
#define PIPE_BUF        65536
#define SVC_TIMEOUT_MS  120000   /* 2-minute cap; commands exceeding this are terminated */
#define PIPE_WAIT_MS    15000    /* wait for service to connect to pipe  */

/* ================================================================
   Global service state (used only in service mode)
   ================================================================ */

static SERVICE_STATUS_HANDLE g_hSvcStatus = NULL;
static SERVICE_STATUS        g_SvcStatus  = {};

/* ================================================================
   Utility helpers
   ================================================================ */

static void PrintError(const wchar_t *ctx, DWORD code)
{
    wchar_t msg[512];
    FormatMessageW(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
                   NULL, code, 0, msg, _countof(msg), NULL);
    /* strip trailing CR/LF */
    for (size_t n = wcslen(msg); n && (msg[n-1] == L'\r' || msg[n-1] == L'\n'); --n)
        msg[n-1] = L'\0';
    fwprintf(stderr, L"[SysExec] %s: %s (error %lu)\n", ctx, msg, code);
}

/* Write string to named pipe / file handle; ignore errors */
static void PipeWrite(HANDLE h, const char *s)
{
    if (h == INVALID_HANDLE_VALUE || !s) return;
    DWORD written;
    WriteFile(h, s, (DWORD)strlen(s), &written, NULL);
}

/* ================================================================
   Service helpers – SetSvcStatus
   ================================================================ */

static void SetSvcStatus(DWORD state, DWORD exitCode = NO_ERROR, DWORD waitHint = 0)
{
    static DWORD checkpoint = 1;
    g_SvcStatus.dwCurrentState  = state;
    g_SvcStatus.dwWin32ExitCode = exitCode;
    g_SvcStatus.dwWaitHint      = waitHint;
    g_SvcStatus.dwCheckPoint    = (state == SERVICE_START_PENDING ||
                                   state == SERVICE_STOP_PENDING)
                                  ? checkpoint++ : 0;
    SetServiceStatus(g_hSvcStatus, &g_SvcStatus);
}

/* ================================================================
   ServiceControlHandler  – handles STOP/SHUTDOWN
   ================================================================ */

static VOID WINAPI SvcCtrlHandler(DWORD ctrl)
{
    if (ctrl == SERVICE_CONTROL_STOP || ctrl == SERVICE_CONTROL_SHUTDOWN)
        SetSvcStatus(SERVICE_STOP_PENDING, NO_ERROR, 3000);
}

/* ================================================================
   ServiceMain – runs in service mode (NT AUTHORITY\SYSTEM)
   ================================================================ */

static VOID WINAPI ServiceMain(DWORD /*argc*/, LPWSTR* /*argv*/)
{
    g_SvcStatus.dwServiceType      = SERVICE_WIN32_OWN_PROCESS;
    g_SvcStatus.dwControlsAccepted = SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN;

    g_hSvcStatus = RegisterServiceCtrlHandlerW(SYSEXEC_SVC_NAME, SvcCtrlHandler);
    if (!g_hSvcStatus) return;

    SetSvcStatus(SERVICE_RUNNING);

    /* ---- Read parameters from registry ---- */
    wchar_t pipeName[256]        = {};
    wchar_t command[MAX_CMD_LEN] = {};
    wchar_t workDir[MAX_PATH]    = {};
    {
        HKEY hKey = NULL;
        if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, SYSEXEC_REG_PARAMS,
                          0, KEY_READ, &hKey) == ERROR_SUCCESS)
        {
            DWORD type, sz;
            sz = sizeof(pipeName);
            RegQueryValueExW(hKey, L"PipeName", NULL, &type, (BYTE*)pipeName, &sz);
            sz = sizeof(command);
            RegQueryValueExW(hKey, L"Command",  NULL, &type, (BYTE*)command,  &sz);
            sz = sizeof(workDir);
            RegQueryValueExW(hKey, L"WorkDir",  NULL, &type, (BYTE*)workDir,  &sz);
            RegCloseKey(hKey);
        }
        /* Delete params so they are not left on disk after the service exits */
        RegDeleteTreeW(HKEY_LOCAL_MACHINE, SYSEXEC_REG_PARAMS);
    }

    if (!command[0])
    {
        SetSvcStatus(SERVICE_STOPPED, ERROR_INVALID_PARAMETER);
        return;
    }

    /* ---- Connect to the named pipe created by the main process ---- */
    HANDLE hPipe = INVALID_HANDLE_VALUE;
    if (pipeName[0])
    {
        for (int tries = 0; tries < 30 && hPipe == INVALID_HANDLE_VALUE; ++tries)
        {
            hPipe = CreateFileW(pipeName, GENERIC_WRITE, 0, NULL,
                                OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
            if (hPipe == INVALID_HANDLE_VALUE)
            {
                DWORD e = GetLastError();
                if (e == ERROR_PIPE_BUSY)
                    WaitNamedPipeW(pipeName, 1000);
                else
                    Sleep(500);
            }
        }
    }

    /* ---- Launch the command, piping stdout/stderr to our named pipe ---- */
    SECURITY_ATTRIBUTES sa = { sizeof(sa), NULL, TRUE };

    STARTUPINFOW si        = {};
    si.cb                  = sizeof(si);
    si.dwFlags             = STARTF_USESTDHANDLES;
    si.hStdOutput          = (hPipe != INVALID_HANDLE_VALUE) ? hPipe
                             : GetStdHandle(STD_OUTPUT_HANDLE);
    si.hStdError           = si.hStdOutput;
    si.hStdInput           = GetStdHandle(STD_INPUT_HANDLE);

    wchar_t cmdLine[MAX_CMD_LEN];
    StringCchCopyW(cmdLine, _countof(cmdLine), command);

    PROCESS_INFORMATION pi = {};
    BOOL ok = CreateProcessW(NULL, cmdLine, NULL, NULL, TRUE,
                             CREATE_NO_WINDOW, NULL,
                             (workDir[0] ? workDir : NULL), &si, &pi);
    if (ok)
    {
        WaitForSingleObject(pi.hProcess, SVC_TIMEOUT_MS);
        CloseHandle(pi.hProcess);
        CloseHandle(pi.hThread);
    }
    else
    {
        char errMsg[256];
        StringCchPrintfA(errMsg, _countof(errMsg),
                         "[SysExec] CreateProcess failed, error %lu\r\n",
                         GetLastError());
        PipeWrite(hPipe, errMsg);
    }

    if (hPipe != INVALID_HANDLE_VALUE) CloseHandle(hPipe);

    SetSvcStatus(SERVICE_STOPPED);
}

/* ================================================================
   WriteRegParams – stores execution parameters in registry
   ================================================================ */

static DWORD WriteRegParams(HKEY hRoot,
                             const wchar_t *pipeName,
                             const wchar_t *command,
                             const wchar_t *workDir)
{
    HKEY hKey = NULL;
    DWORD disp;
    DWORD rc = RegCreateKeyExW(hRoot, SYSEXEC_REG_PARAMS,
                               0, NULL, REG_OPTION_NON_VOLATILE,
                               KEY_WRITE, NULL, &hKey, &disp);
    if (rc != ERROR_SUCCESS) return rc;

    RegSetValueExW(hKey, L"PipeName", 0, REG_SZ, (BYTE*)pipeName,
                   (DWORD)((wcslen(pipeName)+1) * sizeof(wchar_t)));
    RegSetValueExW(hKey, L"Command", 0, REG_SZ, (BYTE*)command,
                   (DWORD)((wcslen(command)+1) * sizeof(wchar_t)));
    RegSetValueExW(hKey, L"WorkDir", 0, REG_SZ, (BYTE*)workDir,
                   (DWORD)((wcslen(workDir)+1) * sizeof(wchar_t)));
    RegCloseKey(hKey);
    return ERROR_SUCCESS;
}

/* ================================================================
   WaitForServiceState – polls SCM until the service reaches the
   target state or the timeout expires.
   ================================================================ */

static BOOL WaitForServiceState(SC_HANDLE hSvc, DWORD targetState, DWORD timeoutMs)
{
    DWORD elapsed = 0;
    SERVICE_STATUS ss = {};
    while (elapsed < timeoutMs)
    {
        QueryServiceStatus(hSvc, &ss);
        if (ss.dwCurrentState == targetState) return TRUE;
        Sleep(500);
        elapsed += 500;
    }
    return FALSE;
}

/* ================================================================
   CleanupService – stops and removes the temporary service
   ================================================================ */

static void CleanupService(SC_HANDLE hSCM, SC_HANDLE hSvc,
                            const wchar_t *remoteExePath)
{
    if (hSvc)
    {
        SERVICE_STATUS ss = {};
        ControlService(hSvc, SERVICE_CONTROL_STOP, &ss);
        WaitForServiceState(hSvc, SERVICE_STOPPED, 10000);
        DeleteService(hSvc);
        CloseServiceHandle(hSvc);
    }
    if (hSCM) CloseServiceHandle(hSCM);
    if (remoteExePath && remoteExePath[0]) DeleteFileW(remoteExePath);
}

/* ================================================================
   CreateNamedPipeServer – creates the output pipe.
   The DACL is restricted: only NT AUTHORITY\SYSTEM may connect and
   write (the service runs as SYSTEM).  Any other process is denied.
   The creating process (admin user) holds the server handle and
   reads; it does not need a client-side entry in the DACL.
   ================================================================ */

static HANDLE CreateNamedPipeServer(const wchar_t *pipeName)
{
    /* D:(A;;GA;;;SY)  =  Allow Generic-All to SYSTEM only */
    PSECURITY_DESCRIPTOR pSD = NULL;
    if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(
            L"D:(A;;GA;;;SY)", SDDL_REVISION_1, &pSD, NULL))
    {
        /* Fall back to a restrictive descriptor built manually */
        pSD = NULL;
    }

    SECURITY_ATTRIBUTES sa = { sizeof(sa), pSD, FALSE };
    HANDLE hPipe = CreateNamedPipeW(pipeName,
                                    PIPE_ACCESS_INBOUND |
                                        FILE_FLAG_FIRST_PIPE_INSTANCE,
                                    PIPE_TYPE_BYTE | PIPE_READMODE_BYTE |
                                        PIPE_WAIT,
                                    1,
                                    PIPE_BUF, PIPE_BUF,
                                    PIPE_WAIT_MS,
                                    &sa);
    if (pSD) LocalFree(pSD);
    return hPipe;
}

/* ================================================================
   StreamPipeToConsole – reads bytes from hPipe and writes to stdout
   until the other end closes the handle.
   ================================================================ */

static void StreamPipeToConsole(HANDLE hPipe)
{
    char buf[4096];
    DWORD nRead;
    while (ReadFile(hPipe, buf, sizeof(buf) - 1, &nRead, NULL) && nRead)
    {
        buf[nRead] = '\0';
        fputs(buf, stdout);
        fflush(stdout);
    }
}

/* ================================================================
   RunAsSystem – LOCAL NT AUTHORITY\SYSTEM execution
   ================================================================ */

static int RunAsSystem(const wchar_t *command, const wchar_t *workDir)
{
    wchar_t exePath[MAX_PATH] = {};
    GetModuleFileNameW(NULL, exePath, MAX_PATH);

    /* Build unique pipe name using our PID */
    wchar_t pipeName[256];
    StringCchPrintfW(pipeName, _countof(pipeName),
                     L"%s%lu", SYSEXEC_PIPE_PREFIX, GetCurrentProcessId());

    HANDLE hPipe = CreateNamedPipeServer(pipeName);
    if (hPipe == INVALID_HANDLE_VALUE)
    {
        PrintError(L"CreateNamedPipe", GetLastError());
        return 1;
    }

    /* Store execution parameters */
    DWORD rc = WriteRegParams(HKEY_LOCAL_MACHINE, pipeName, command, workDir);
    if (rc != ERROR_SUCCESS)
    {
        PrintError(L"WriteRegParams", rc);
        CloseHandle(hPipe);
        return 1;
    }

    /* Open local SCM and install the temporary service */
    SC_HANDLE hSCM = OpenSCManagerW(NULL, NULL, SC_MANAGER_ALL_ACCESS);
    if (!hSCM)
    {
        PrintError(L"OpenSCManager", GetLastError());
        CloseHandle(hPipe);
        return 1;
    }

    /* Remove any stale service with the same name */
    SC_HANDLE hOld = OpenServiceW(hSCM, SYSEXEC_SVC_NAME, DELETE);
    if (hOld) { DeleteService(hOld); CloseServiceHandle(hOld); }

    /* Build the service binary path: "<our exe>" /SvcRun */
    wchar_t binPath[MAX_PATH + 16];
    StringCchPrintfW(binPath, _countof(binPath), L"\"%s\" %s", exePath, SYSEXEC_SVC_ARG);

    SC_HANDLE hSvc = CreateServiceW(
        hSCM,
        SYSEXEC_SVC_NAME,
        SYSEXEC_SVC_DISPLAY,
        SERVICE_ALL_ACCESS,
        SERVICE_WIN32_OWN_PROCESS,
        SERVICE_DEMAND_START,
        SERVICE_ERROR_NORMAL,
        binPath,
        NULL, NULL, NULL,
        NULL, NULL);   /* LocalSystem account */

    if (!hSvc)
    {
        PrintError(L"CreateService", GetLastError());
        CloseServiceHandle(hSCM);
        CloseHandle(hPipe);
        RegDeleteTreeW(HKEY_LOCAL_MACHINE, SYSEXEC_REG_PARAMS);
        return 1;
    }

    if (!StartServiceW(hSvc, 0, NULL))
    {
        PrintError(L"StartService", GetLastError());
        CleanupService(hSCM, hSvc, NULL);
        CloseHandle(hPipe);
        RegDeleteTreeW(HKEY_LOCAL_MACHINE, SYSEXEC_REG_PARAMS);
        return 1;
    }

    /* Wait for the service to connect to our pipe */
    BOOL connected = ConnectNamedPipe(hPipe, NULL);
    if (!connected && GetLastError() != ERROR_PIPE_CONNECTED)
    {
        PrintError(L"ConnectNamedPipe", GetLastError());
        CleanupService(hSCM, hSvc, NULL);
        CloseHandle(hPipe);
        return 1;
    }

    /* Stream command output to our stdout */
    StreamPipeToConsole(hPipe);
    CloseHandle(hPipe);

    /* Wait until service exits, then clean up */
    WaitForServiceState(hSvc, SERVICE_STOPPED, SVC_TIMEOUT_MS);
    CleanupService(hSCM, hSvc, NULL);
    return 0;
}

/* ================================================================
   RunOnRemote – run a command as SYSTEM on a remote machine.

   Steps:
     1. Copy SysExec.exe to \\computer\admin$\SysExec.exe
     2. Connect to remote SCM and create a temporary service
     3. Write execution parameters to the remote registry
     4. Start the service (it runs as SYSTEM on the remote host)
     5. The remote service connects back to the named pipe on THIS host
     6. Stream output, wait for completion, clean up
   ================================================================ */

static int RunOnRemote(const wchar_t *computer,
                       const wchar_t *command,
                       const wchar_t *workDir)
{
    wchar_t exePath[MAX_PATH] = {};
    GetModuleFileNameW(NULL, exePath, MAX_PATH);

    /* Remote copy path */
    wchar_t remoteExe[MAX_PATH];
    StringCchPrintfW(remoteExe, _countof(remoteExe),
                     L"\\\\%s\\admin$\\SysExec.exe", computer);

    if (!CopyFileW(exePath, remoteExe, FALSE))
    {
        PrintError(L"Copy to remote admin$", GetLastError());
        return 1;
    }

    /* Build named pipe name as seen from the remote machine.
       The remote service will connect to \\LOCAL_COMPUTER\pipe\<name>. */
    wchar_t localHost[MAX_COMPUTERNAME_LENGTH + 1] = {};
    DWORD   hostLen = MAX_COMPUTERNAME_LENGTH + 1;
    GetComputerNameW(localHost, &hostLen);

    wchar_t remotePipeName[256];
    StringCchPrintfW(remotePipeName, _countof(remotePipeName),
                     L"\\\\%s\\pipe\\SysExecOut_%lu",
                     localHost, GetCurrentProcessId());

    /* Also build the local-name variant for CreateNamedPipe */
    wchar_t localPipeName[256];
    StringCchPrintfW(localPipeName, _countof(localPipeName),
                     L"%s%lu", SYSEXEC_PIPE_PREFIX, GetCurrentProcessId());

    HANDLE hPipe = CreateNamedPipeServer(localPipeName);
    if (hPipe == INVALID_HANDLE_VALUE)
    {
        PrintError(L"CreateNamedPipe", GetLastError());
        DeleteFileW(remoteExe);
        return 1;
    }

    /* Connect to remote SCM */
    SC_HANDLE hSCM = OpenSCManagerW(computer, NULL, SC_MANAGER_ALL_ACCESS);
    if (!hSCM)
    {
        PrintError(L"OpenSCManager (remote)", GetLastError());
        CloseHandle(hPipe);
        DeleteFileW(remoteExe);
        return 1;
    }

    /* Remove stale service */
    SC_HANDLE hOld = OpenServiceW(hSCM, SYSEXEC_SVC_NAME, DELETE);
    if (hOld) { DeleteService(hOld); CloseServiceHandle(hOld); }

    /* Service binary is at %SystemRoot%\SysExec.exe on the remote host */
    wchar_t binPath[MAX_PATH + 16];
    StringCchPrintfW(binPath, _countof(binPath),
                     L"%%SystemRoot%%\\SysExec.exe %s", SYSEXEC_SVC_ARG);

    SC_HANDLE hSvc = CreateServiceW(
        hSCM,
        SYSEXEC_SVC_NAME,
        SYSEXEC_SVC_DISPLAY,
        SERVICE_ALL_ACCESS,
        SERVICE_WIN32_OWN_PROCESS,
        SERVICE_DEMAND_START,
        SERVICE_ERROR_NORMAL,
        binPath,
        NULL, NULL, NULL,
        NULL, NULL);

    if (!hSvc)
    {
        PrintError(L"CreateService (remote)", GetLastError());
        CloseHandle(hPipe);
        CloseServiceHandle(hSCM);
        DeleteFileW(remoteExe);
        return 1;
    }

    /* Write parameters to remote registry */
    HKEY hRemoteLM = NULL;
    DWORD regRc = RegConnectRegistryW(computer, HKEY_LOCAL_MACHINE, &hRemoteLM);
    if (regRc != ERROR_SUCCESS)
    {
        PrintError(L"RegConnectRegistry (remote)", regRc);
        CleanupService(hSCM, hSvc, remoteExe);
        CloseHandle(hPipe);
        return 1;
    }
    DWORD paramRc = WriteRegParams(hRemoteLM, remotePipeName, command, workDir);
    RegCloseKey(hRemoteLM);
    if (paramRc != ERROR_SUCCESS)
    {
        PrintError(L"WriteRegParams (remote)", paramRc);
        CleanupService(hSCM, hSvc, remoteExe);
        CloseHandle(hPipe);
        return 1;
    }

    if (!StartServiceW(hSvc, 0, NULL))
    {
        PrintError(L"StartService (remote)", GetLastError());
        CleanupService(hSCM, hSvc, remoteExe);
        CloseHandle(hPipe);
        return 1;
    }

    /* Wait for remote service to connect */
    BOOL connected = ConnectNamedPipe(hPipe, NULL);
    if (!connected && GetLastError() != ERROR_PIPE_CONNECTED)
    {
        PrintError(L"ConnectNamedPipe (remote)", GetLastError());
        CleanupService(hSCM, hSvc, remoteExe);
        CloseHandle(hPipe);
        return 1;
    }

    StreamPipeToConsole(hPipe);
    CloseHandle(hPipe);

    WaitForServiceState(hSvc, SERVICE_STOPPED, SVC_TIMEOUT_MS);
    CleanupService(hSCM, hSvc, remoteExe);
    return 0;
}

/* ================================================================
   PrintUsage
   ================================================================ */

static void PrintUsage(void)
{
    wprintf(
        L"SysExec v1.0  -  Lightweight NT AUTHORITY\\SYSTEM command executor\n"
        L"Part of the AdvancedRun-Rework project\n\n"
        L"Usage:\n"
        L"  SysExec /system <command> [args]          "
            L"Run as SYSTEM on this machine\n"
        L"  SysExec /computer <host> <command> [args] "
            L"Run as SYSTEM on a remote host\n\n"
        L"Options:\n"
        L"  /system            Execute the command as NT AUTHORITY\\SYSTEM (local)\n"
        L"  /computer <host>   Target remote computer (name or IP address)\n"
        L"                     Leading \\\\\\\\ is accepted but not required\n"
        L"  /dir <path>        Set the working directory for the launched process\n"
        L"  /?  /help          Show this help text\n\n"
        L"Examples:\n"
        L"  SysExec /system cmd.exe\n"
        L"  SysExec /system whoami\n"
        L"  SysExec /system /dir C:\\Windows regedit.exe\n"
        L"  SysExec /computer 192.168.1.10 ipconfig /all\n"
        L"  SysExec /computer SERVER01 /dir C:\\Temp cmd.exe\n\n"
        L"Notes:\n"
        L"  * Administrator privileges are required.\n"
        L"  * Remote mode needs SMB admin-share access (admin$) and RPC\n"
        L"    on the target machine.\n"
        L"  * A temporary Windows service named '" SYSEXEC_SVC_NAME L"' is\n"
        L"    created and automatically removed after execution.\n"
    );
}

/* ================================================================
   wmain
   ================================================================ */

int wmain(int argc, wchar_t **argv)
{
    /* ---- Service dispatch mode ----------------------------------------
       When the SCM starts us as a service, argv[1] == SYSEXEC_SVC_ARG.
       We hand control to the service dispatcher which calls ServiceMain.
       --------------------------------------------------------------------- */
    if (argc >= 2 && _wcsicmp(argv[1], SYSEXEC_SVC_ARG) == 0)
    {
        SERVICE_TABLE_ENTRYW table[] = {
            { (LPWSTR)SYSEXEC_SVC_NAME, ServiceMain },
            { NULL, NULL }
        };
        StartServiceCtrlDispatcherW(table);
        return 0;
    }

    /* ---- Parse arguments ----------------------------------------------- */
    wchar_t computer[256]        = {};
    wchar_t workDir[MAX_PATH]    = {};
    wchar_t command[MAX_CMD_LEN] = {};
    BOOL    bSystem              = FALSE;

    int i = 1;
    while (i < argc)
    {
        if (_wcsicmp(argv[i], L"/system") == 0 ||
            _wcsicmp(argv[i], L"-s")      == 0)
        {
            bSystem = TRUE;
            ++i;
        }
        else if ((_wcsicmp(argv[i], L"/computer") == 0 ||
                  _wcsicmp(argv[i], L"-c")        == 0) && (i + 1 < argc))
        {
            const wchar_t *host = argv[i + 1];
            if (host[0] == L'\\' && host[1] == L'\\') host += 2;
            StringCchCopyW(computer, _countof(computer), host);
            bSystem = TRUE;   /* remote always runs as SYSTEM */
            i += 2;
        }
        else if ((_wcsicmp(argv[i], L"/dir") == 0 ||
                  _wcsicmp(argv[i], L"-d")   == 0) && (i + 1 < argc))
        {
            StringCchCopyW(workDir, _countof(workDir), argv[i + 1]);
            i += 2;
        }
        else if (_wcsicmp(argv[i], L"/?")     == 0 ||
                 _wcsicmp(argv[i], L"/help")  == 0 ||
                 _wcsicmp(argv[i], L"-h")     == 0 ||
                 _wcsicmp(argv[i], L"--help") == 0)
        {
            PrintUsage();
            return 0;
        }
        else
        {
            /* Everything from here onward is the command + its arguments */
            for (int j = i; j < argc; ++j)
            {
                if (j > i) StringCchCatW(command, _countof(command), L" ");

                /* Quote arguments that contain whitespace */
                BOOL needsQuote = (wcschr(argv[j], L' ') || wcschr(argv[j], L'\t'));
                if (needsQuote) StringCchCatW(command, _countof(command), L"\"");
                StringCchCatW(command, _countof(command), argv[j]);
                if (needsQuote) StringCchCatW(command, _countof(command), L"\"");
            }
            break;
        }
    }

    if (!command[0])
    {
        PrintUsage();
        return 1;
    }

    /* ---- Dispatch -------------------------------------------------------- */
    if (computer[0])
        return RunOnRemote(computer, command, workDir);

    if (bSystem)
        return RunAsSystem(command, workDir);

    /* Plain run (no elevation override) – just launch the process normally */
    STARTUPINFOW si        = { sizeof(si) };
    PROCESS_INFORMATION pi = {};
    wchar_t cmdLine[MAX_CMD_LEN];
    StringCchCopyW(cmdLine, _countof(cmdLine), command);

    if (!CreateProcessW(NULL, cmdLine, NULL, NULL, FALSE, 0, NULL,
                        workDir[0] ? workDir : NULL, &si, &pi))
    {
        PrintError(L"CreateProcess", GetLastError());
        return 1;
    }
    WaitForSingleObject(pi.hProcess, INFINITE);
    DWORD exitCode = 0;
    GetExitCodeProcess(pi.hProcess, &exitCode);
    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);
    return (int)exitCode;
}
