cmake_minimum_required(VERSION 3.16)
project(FileMonitorDriver C)

# This CMakeLists is provided as a reference.
# Building WDK drivers typically uses MSBuild.
# For CMake-based WDK builds, ensure FindWDK.cmake is available.

find_package(WDK REQUIRED)

wdk_add_driver(FileMonitorDriver
    driver.c
)

target_include_directories(FileMonitorDriver PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
target_link_libraries(FileMonitorDriver WDK::FLTMGR WDK::NTSTRSAFE)
