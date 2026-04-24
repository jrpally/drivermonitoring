/*
 * shared.h - Shared definitions between kernel driver and user-mode service.
 * Author: Rene Pally
 *
 * This header defines the communication protocol between the minifilter
 * driver and the user-mode Windows service.
 */

#ifndef _FILE_MONITOR_SHARED_H_
#define _FILE_MONITOR_SHARED_H_

/* Communication port name used by FilterConnectCommunicationPort */
#define FILE_MONITOR_PORT_NAME L"\\FileMonitorPort"

/* Maximum file path length in notification messages */
#define FILE_MONITOR_MAX_PATH 1024

/* File event types reported by the driver */
typedef enum _FILE_MONITOR_EVENT_TYPE {
    FileEventCreate      = 0x01,
    FileEventClose        = 0x02,
    FileEventRead         = 0x04,
    FileEventWrite        = 0x08,
    FileEventDelete       = 0x10,
    FileEventRename       = 0x20,
    FileEventSetInfo      = 0x40,
    FileEventCleanup      = 0x80,
} FILE_MONITOR_EVENT_TYPE;

/* Notification message sent from driver to user mode */
typedef struct _FILE_MONITOR_NOTIFICATION {
    ULONG  EventType;               /* FILE_MONITOR_EVENT_TYPE */
    ULONG  ProcessId;               /* Process that triggered the event */
    ULONG  ThreadId;                /* Thread that triggered the event */
    LARGE_INTEGER Timestamp;        /* System time of the event */
    ULONG  FilePathLength;          /* Length of FilePath in bytes */
    WCHAR  FilePath[FILE_MONITOR_MAX_PATH]; /* Full file path */
} FILE_MONITOR_NOTIFICATION, *PFILE_MONITOR_NOTIFICATION;

/* Command sent from user mode to driver */
typedef enum _FILE_MONITOR_COMMAND_TYPE {
    CommandStartMonitoring = 1,
    CommandStopMonitoring  = 2,
} FILE_MONITOR_COMMAND_TYPE;

typedef struct _FILE_MONITOR_COMMAND {
    FILE_MONITOR_COMMAND_TYPE Command;
} FILE_MONITOR_COMMAND, *PFILE_MONITOR_COMMAND;

/* Reply from driver to command */
typedef struct _FILE_MONITOR_REPLY {
    NTSTATUS Status;
} FILE_MONITOR_REPLY, *PFILE_MONITOR_REPLY;

#endif /* _FILE_MONITOR_SHARED_H_ */
