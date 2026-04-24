@echo off
REM Install / manage the FileMonitor Windows Service
REM Author: Rene Pally
REM Must be run as Administrator
REM Usage: Manage-Service.bat <install|uninstall|start|stop|status> [ServiceExePath]

setlocal

set "ACTION=%~1"
set "SERVICE_EXE=%~2"
set "SERVICE_NAME=FileMonitorService"

if "%ACTION%"=="" (
    echo Usage: %~nx0 ^<install^|uninstall^|start^|stop^|status^> [ServiceExePath]
    exit /b 1
)

REM Check admin
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: This script must be run as Administrator.
    exit /b 1
)

if /i "%ACTION%"=="install" goto :install
if /i "%ACTION%"=="uninstall" goto :uninstall
if /i "%ACTION%"=="start" goto :start
if /i "%ACTION%"=="stop" goto :stop
if /i "%ACTION%"=="status" goto :status

echo ERROR: Unknown action "%ACTION%". Use install, uninstall, start, stop, or status.
exit /b 1

:install
if "%SERVICE_EXE%"=="" set "SERVICE_EXE=.\FileMonitor.Service.exe"
REM Resolve to full path
for %%F in ("%SERVICE_EXE%") do set "FULL_PATH=%%~fF"
echo Installing service from: %FULL_PATH%
sc create %SERVICE_NAME% binPath= "%FULL_PATH%" DisplayName= "FileMonitor Service" start= auto
if %errorlevel% neq 0 (
    echo ERROR: Failed to create service.
    exit /b 1
)
sc description %SERVICE_NAME% "Monitors file system events via minifilter driver and provides gRPC event streaming."
echo Service installed.
goto :eof

:uninstall
sc stop %SERVICE_NAME% >nul 2>&1
sc delete %SERVICE_NAME%
echo Service uninstalled.
goto :eof

:start
sc start %SERVICE_NAME%
echo Service started.
goto :eof

:stop
sc stop %SERVICE_NAME%
echo Service stopped.
goto :eof

:status
sc query %SERVICE_NAME%
goto :eof
