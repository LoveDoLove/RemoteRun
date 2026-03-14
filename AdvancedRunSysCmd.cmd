@echo off
title SYSTEM CMD Launcher
cd /d "%~dp0"

:: -----------------------------------------------------------------------
:: AdvancedRunSysCmd.cmd
:: Launches an interactive CMD terminal running as the NT AUTHORITY\SYSTEM
:: user, similar to "PsExec -s cmd.exe".
::
:: Requirements:
::   - Must be run as Administrator (UAC elevation required).
::   - AdvancedRun.exe and SYSTEM.cfg must be in the same folder as this script.
:: -----------------------------------------------------------------------

:: Check for Administrator privileges
fsutil dirty query %systemdrive% >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] This script requires Administrator privileges.
    echo Please right-click this script and choose "Run as administrator".
    pause
    exit /b 1
)

:: Verify AdvancedRun.exe is present
if not exist "%~dp0AdvancedRun.exe" (
    echo [ERROR] AdvancedRun.exe not found in "%~dp0".
    echo Please ensure AdvancedRun.exe is in the same folder as this script.
    pause
    exit /b 1
)

:: Verify SYSTEM.cfg is present
if not exist "%~dp0SYSTEM.cfg" (
    echo [ERROR] SYSTEM.cfg not found in "%~dp0".
    echo Please ensure SYSTEM.cfg is in the same folder as this script.
    pause
    exit /b 1
)

echo [INFO] Launching CMD as NT AUTHORITY\SYSTEM via AdvancedRun...
start "" "%~dp0AdvancedRun.exe" /Run "%~dp0SYSTEM.cfg"