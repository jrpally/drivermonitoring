/*
 * driver.h - FileMonitor minifilter driver header.
 * Author: Rene Pally
 */

#ifndef _FILE_MONITOR_DRIVER_H_
#define _FILE_MONITOR_DRIVER_H_

#pragma warning(push)
#pragma warning(disable: 4324) /* structure padded due to alignment specifier */
#include <fltKernel.h>
#pragma warning(pop)
#include <dontuse.h>
#include <suppress.h>
#include <ntstrsafe.h>
#include "shared.h"

/* Pool tag for memory allocations */
#define FM_TAG 'noMF'

/* Global driver data */
typedef struct _FILE_MONITOR_DATA {
    PFLT_FILTER   Filter;
    PFLT_PORT     ServerPort;
    PFLT_PORT     ClientPort;
    BOOLEAN       MonitoringActive;
    FAST_MUTEX    StateLock;
} FILE_MONITOR_DATA, *PFILE_MONITOR_DATA;

extern FILE_MONITOR_DATA Globals;

/* --- Minifilter callback declarations --- */

DRIVER_INITIALIZE DriverEntry;
NTSTATUS DriverEntry(
    _In_ PDRIVER_OBJECT  DriverObject,
    _In_ PUNICODE_STRING RegistryPath
);

NTSTATUS FmUnload(
    _In_ FLT_FILTER_UNLOAD_FLAGS Flags
);

NTSTATUS FmInstanceSetup(
    _In_ PCFLT_RELATED_OBJECTS    FltObjects,
    _In_ FLT_INSTANCE_SETUP_FLAGS Flags,
    _In_ DEVICE_TYPE              VolumeDeviceType,
    _In_ FLT_FILESYSTEM_TYPE      VolumeFilesystemType
);

/* Pre/Post operation callbacks */
FLT_PREOP_CALLBACK_STATUS FmPreCreateCallback(
    _Inout_ PFLT_CALLBACK_DATA    Data,
    _In_    PCFLT_RELATED_OBJECTS FltObjects,
    _Out_   PVOID                 *CompletionContext
);

FLT_POSTOP_CALLBACK_STATUS FmPostCreateCallback(
    _Inout_  PFLT_CALLBACK_DATA       Data,
    _In_     PCFLT_RELATED_OBJECTS    FltObjects,
    _In_opt_ PVOID                    CompletionContext,
    _In_     FLT_POST_OPERATION_FLAGS Flags
);

FLT_PREOP_CALLBACK_STATUS FmPreWriteCallback(
    _Inout_ PFLT_CALLBACK_DATA    Data,
    _In_    PCFLT_RELATED_OBJECTS FltObjects,
    _Out_   PVOID                 *CompletionContext
);

FLT_PREOP_CALLBACK_STATUS FmPreSetInfoCallback(
    _Inout_ PFLT_CALLBACK_DATA    Data,
    _In_    PCFLT_RELATED_OBJECTS FltObjects,
    _Out_   PVOID                 *CompletionContext
);

FLT_PREOP_CALLBACK_STATUS FmPreCleanupCallback(
    _Inout_ PFLT_CALLBACK_DATA    Data,
    _In_    PCFLT_RELATED_OBJECTS FltObjects,
    _Out_   PVOID                 *CompletionContext
);

/* Communication port callbacks */
NTSTATUS FmPortConnect(
    _In_  PFLT_PORT         ClientPort,
    _In_  PVOID             ServerPortCookie,
    _In_reads_bytes_(SizeOfContext) PVOID ConnectionContext,
    _In_  ULONG             SizeOfContext,
    _Out_ PVOID             *ConnectionCookie
);

VOID FmPortDisconnect(
    _In_opt_ PVOID ConnectionCookie
);

NTSTATUS FmPortMessageNotify(
    _In_  PVOID                         PortCookie,
    _In_reads_bytes_(InputBufferLength)  PVOID InputBuffer,
    _In_  ULONG                         InputBufferLength,
    _Out_writes_bytes_to_(OutputBufferLength, *ReturnOutputBufferLength) PVOID OutputBuffer,
    _In_  ULONG                         OutputBufferLength,
    _Out_ PULONG                        ReturnOutputBufferLength
);

/* Helper to send notification to user mode */
NTSTATUS FmSendNotification(
    _In_ FILE_MONITOR_EVENT_TYPE EventType,
    _In_ PFLT_CALLBACK_DATA     Data,
    _In_ PCFLT_RELATED_OBJECTS  FltObjects
);

#endif /* _FILE_MONITOR_DRIVER_H_ */
