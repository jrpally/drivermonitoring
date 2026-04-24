@echo off
REM Install the FileMonitor minifilter driver
REM Author: Rene Pally
REM Must be run as Administrator

setlocal

set "DRIVER_PATH=%~1"
set "INF_PATH=%~2"
if "%DRIVER_PATH%"=="" set "DRIVER_PATH=.\FileMonitorDriver.sys"
if "%INF_PATH%"=="" set "INF_PATH=.\FileMonitorDriver.inf"

echo === FileMonitor Driver Installation ===

REM Check admin
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: This script must be run as Administrator.
    exit /b 1
)

REM Copy driver to system32\drivers
set "DEST_DIR=%SystemRoot%\system32\drivers"
echo Copying driver to %DEST_DIR%...
copy /y "%DRIVER_PATH%" "%DEST_DIR%\FileMonitorDriver.sys"
if %errorlevel% neq 0 (
    echo ERROR: Failed to copy driver.
    exit /b 1
)

REM Install using INF
echo Installing driver via INF...
rundll32.exe setupapi.dll,InstallHinfSection DefaultInstall 132 "%INF_PATH%"

REM Load the driver
echo Loading minifilter...
fltmc load FileMonitorDriver

echo.
fltmc filters | findstr /i "FileMonitor"
echo.
echo Driver installed and loaded.
