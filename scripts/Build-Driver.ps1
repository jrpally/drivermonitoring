# Build-Driver.ps1 — Compile the FileMonitor minifilter driver
# Author: Rene Pally
# Uses MSVC cl.exe + link.exe with WDK kernel-mode headers/libs.
# Must be run from the driverproject root or specify -SourceDir.

param(
    [ValidateSet("Debug","Release")]
    [string]$Configuration = "Debug",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

# --- Locate tools ---
$vsBase = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -property installationPath 2>$null
if (-not $vsBase) { throw "Visual Studio not found." }

$msvcVersions = Get-ChildItem "$vsBase\VC\Tools\MSVC" -Directory | Sort-Object Name -Descending
$msvcDir = $msvcVersions[0].FullName
$clExe   = "$msvcDir\bin\Hostx64\x64\cl.exe"
$linkExe = "$msvcDir\bin\Hostx64\x64\link.exe"

if (-not (Test-Path $clExe)) { throw "cl.exe not found at $clExe" }

# --- Locate WDK ---
$wdkRoot = "C:\Program Files (x86)\Windows Kits\10"

# Find the newest version that has kernel-mode (km) headers
$wdkVer = (Get-ChildItem "$wdkRoot\Include" -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' -and
                   (Test-Path "$wdkRoot\Include\$($_.Name)\km") } |
    Sort-Object Name -Descending |
    Select-Object -First 1).Name
if (-not $wdkVer) { throw "WDK km headers not found under $wdkRoot\Include" }

# Find the newest version that has shared\specstrings.h (comes from the Windows SDK,
# may differ from the WDK version on CI runners where SDK and WDK versions diverge)
$sdkVer = (Get-ChildItem "$wdkRoot\Include" -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' -and
                   (Test-Path "$wdkRoot\Include\$($_.Name)\shared\specstrings.h") } |
    Sort-Object Name -Descending |
    Select-Object -First 1).Name
if (-not $sdkVer) {
    # Fallback: use any version that has a shared directory
    $sdkVer = (Get-ChildItem "$wdkRoot\Include" -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' -and
                       (Test-Path "$wdkRoot\Include\$($_.Name)\shared") } |
        Sort-Object Name -Descending |
        Select-Object -First 1).Name
}
if (-not $sdkVer) { throw "Windows SDK shared headers not found under $wdkRoot\Include" }

Write-Host "MSVC       : $msvcDir" -ForegroundColor Cyan
Write-Host "WDK (km)   : $wdkVer" -ForegroundColor Cyan
Write-Host "SDK (shared): $sdkVer" -ForegroundColor Cyan
Write-Host "Config      : $Configuration | $Platform" -ForegroundColor Cyan
Write-Host ""

# --- Paths ---
$srcDir = "$PSScriptRoot\..\src\driver\FileMonitorDriver"
$outDir = "$PSScriptRoot\..\bin\$Configuration\$Platform"
$objDir = "$PSScriptRoot\..\build\obj\driver\$Configuration\$Platform"

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
New-Item -ItemType Directory -Force -Path $objDir | Out-Null

$kmInclude = "$wdkRoot\Include\$wdkVer\km"
$ucrtKmInc = "$wdkRoot\Include\$wdkVer\km\crt"
$sharedInc = "$wdkRoot\Include\$sdkVer\shared"   # SDK version — has specstrings.h
$kmLib     = "$wdkRoot\lib\$wdkVer\km\$Platform"

# --- Compiler flags ---
$defines = @(
    "/DNTDDI_VERSION=0x0A000000"  # Windows 10
    "/D_WIN64"
    "/D_AMD64_"
    "/DAMD64"
)

$clFlags = @(
    "/kernel"         # Kernel-mode compilation
    "/Zi"             # Debug info
    "/W4"             # Warning level 4
    "/WX"             # Warnings as errors
    "/Zp8"            # 8-byte struct packing
    "/GF"             # String pooling
    "/Gm-"            # Disable minimal rebuild
    "/GR-"            # No RTTI
    "/GS-"            # No buffer security check (kernel)
    "/Gy"             # Function-level linking
    "/Gz"             # __stdcall
    "/Oi"             # Intrinsics
    "/EHs-c-"         # No exceptions
    "/std:clatest"
    "/c"              # Compile only
) + $defines

if ($Configuration -eq "Debug") {
    $clFlags += "/Od"   # No optimization
} else {
    $clFlags += "/O2"   # Optimize for speed
}

$includes = @(
    "/I`"$srcDir`""
    "/I`"$ucrtKmInc`""
    "/I`"$msvcDir\include`""  # specstrings.h / sal.h live here in VS 2022+
    "/I`"$kmInclude`""
    "/I`"$sharedInc`""
)

# --- Compile ---
Write-Host "Compiling driver.c ..." -ForegroundColor Yellow
$compileArgs = $clFlags + $includes + @(
    "/Fo`"$objDir\driver.obj`""
    "/Fd`"$objDir\driver.pdb`""
    "`"$srcDir\driver.c`""
)

$compileCmd = "& `"$clExe`" $($compileArgs -join ' ')"
Write-Host $compileCmd -ForegroundColor DarkGray
Invoke-Expression $compileCmd
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }

# --- Link ---
Write-Host ""
Write-Host "Linking FileMonitorDriver.sys ..." -ForegroundColor Yellow

$linkFlags = @(
    "/DRIVER"
    "/KERNEL"
    "/SUBSYSTEM:NATIVE"
    "/ENTRY:DriverEntry"
    "/NODEFAULTLIB"
    "/DEBUG"
    "/OUT:`"$outDir\FileMonitorDriver.sys`""
    "/PDB:`"$outDir\FileMonitorDriver.pdb`""
    "/MAP:`"$outDir\FileMonitorDriver.map`""
    "/LIBPATH:`"$kmLib`""
    "/LIBPATH:`"$msvcDir\lib\x64`""
    "ntoskrnl.lib"
    "hal.lib"
    "fltMgr.lib"
    "ntstrsafe.lib"
    "BufferOverflowK.lib"
    "`"$objDir\driver.obj`""
)

$linkCmd = "& `"$linkExe`" $($linkFlags -join ' ')"
Write-Host $linkCmd -ForegroundColor DarkGray
Invoke-Expression $linkCmd
if ($LASTEXITCODE -ne 0) { throw "Linking failed with exit code $LASTEXITCODE" }

# --- Post-build: copy deployment files ---
Write-Host ""
Write-Host "Copying deployment files ..." -ForegroundColor Yellow

# INF file alongside the .sys
Copy-Item "$srcDir\FileMonitorDriver.inf" "$outDir\FileMonitorDriver.inf" -Force
Write-Host "  -> FileMonitorDriver.inf"

# Deployment scripts
$scriptsOutDir = "$PSScriptRoot\..\bin\$Configuration\scripts"
New-Item -ItemType Directory -Force -Path $scriptsOutDir | Out-Null
Copy-Item "$PSScriptRoot\Install-Driver.bat"   "$scriptsOutDir\" -Force
Copy-Item "$PSScriptRoot\Uninstall-Driver.bat" "$scriptsOutDir\" -Force
Copy-Item "$PSScriptRoot\Manage-Service.bat"   "$scriptsOutDir\" -Force
Write-Host "  -> scripts\Install-Driver.bat"
Write-Host "  -> scripts\Uninstall-Driver.bat"
Write-Host "  -> scripts\Manage-Service.bat"

Write-Host ""
Write-Host "Build succeeded: $outDir\FileMonitorDriver.sys" -ForegroundColor Green
Get-ChildItem "$outDir" | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
