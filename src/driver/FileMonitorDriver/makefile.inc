#
# Makefile for FileMonitorDriver minifilter
# Build with WDK: msbuild or build.exe
#

TARGETNAME=FileMonitorDriver
TARGETTYPE=DRIVER

# Minifilter is loaded by the filter manager
DRIVERTYPE=FS

TARGETLIBS=$(DDK_LIB_PATH)\fltMgr.lib \
           $(DDK_LIB_PATH)\ntstrsafe.lib

INCLUDES=$(INCLUDES);.

SOURCES=driver.c

MSC_WARNING_LEVEL=/W4 /WX
