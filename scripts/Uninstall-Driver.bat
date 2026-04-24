@echo off
REM Uninstall the FileMonitor minifilter driver
REM Author: Rene Pally
REM Must be run as Administrator

setlocal

echo === FileMonitor Driver Uninstall ===

REM Check admin
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: This script must be run as Administrator.
    exit /b 1
)

REM Unload the filter
echo Unloading minifilter...
fltmc unload FileMonitorDriver 2>nul

REM Remove driver file
set "DRIVER_FILE=%SystemRoot%\system32\drivers\FileMonitorDriver.sys"
if exist "%DRIVER_FILE%" (
    del /f "%DRIVER_FILE%"
    echo Removed %DRIVER_FILE%
)

REM Remove registry entries
reg query "HKLM\SYSTEM\CurrentControlSet\Services\FileMonitorDriver" >nul 2>&1
if %errorlevel% equ 0 (
    reg delete "HKLM\SYSTEM\CurrentControlSet\Services\FileMonitorDriver" /f >nul
    echo Removed registry entries.
)

echo Driver uninstalled.
