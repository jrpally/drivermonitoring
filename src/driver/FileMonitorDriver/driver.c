/*
 * driver.c - FileMonitor minifilter driver implementation.
 * Author: Rene Pally
 *
 * This minifilter intercepts file system operations and sends
 * notifications to a user-mode service via a communication port.
 */

#include "driver.h"

#pragma prefast(disable:__WARNING_ENCODE_MEMBER_FUNCTION_POINTER, \
    "Not valid for kernel mode drivers")

/* Global data instance */
FILE_MONITOR_DATA Globals = { 0 };

/* Operations we want to intercept */
CONST FLT_OPERATION_REGISTRATION Callbacks[] = {
    {
        IRP_MJ_CREATE,
        0,
        FmPreCreateCallback,
        FmPostCreateCallback
    },
    {
        IRP_MJ_WRITE,
        0,
        FmPreWriteCallback,
        NULL
    },
    {
        IRP_MJ_SET_INFORMATION,
        0,
        FmPreSetInfoCallback,
        NULL
    },
    {
        IRP_MJ_CLEANUP,
        0,
        FmPreCleanupCallback,
        NULL
    },
    { IRP_MJ_OPERATION_END }
};

/* Filter registration structure */
CONST FLT_REGISTRATION FilterRegistration = {
    sizeof(FLT_REGISTRATION),       /* Size */
    FLT_REGISTRATION_VERSION,       /* Version */
    0,                               /* Flags */
    NULL,                            /* Context */
    Callbacks,                       /* Operation callbacks */
    FmUnload,                        /* MiniFilterUnload */
    FmInstanceSetup,                 /* InstanceSetup */
    NULL,                            /* InstanceQueryTeardown */
    NULL,                            /* InstanceTeardownStart */
    NULL,                            /* InstanceTeardownComplete */
    NULL, NULL, NULL                 /* Unused name/generate callbacks */
};

/* ------------------------------------------------------------------ */
/*                           Driver Entry                             */
/* ------------------------------------------------------------------ */

NTSTATUS DriverEntry(
    _In_ PDRIVER_OBJECT  DriverObject,
    _In_ PUNICODE_STRING RegistryPath
)
{
    NTSTATUS status;
    UNICODE_STRING portName;
    OBJECT_ATTRIBUTES oa;
    PSECURITY_DESCRIPTOR sd = NULL;

    UNREFERENCED_PARAMETER(RegistryPath);

    ExInitializeFastMutex(&Globals.StateLock);
    Globals.MonitoringActive = TRUE;

    /* Register with Filter Manager */
    status = FltRegisterFilter(DriverObject, &FilterRegistration, &Globals.Filter);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    /* Create communication port so user-mode service can connect */
    RtlInitUnicodeString(&portName, FILE_MONITOR_PORT_NAME);

    status = FltBuildDefaultSecurityDescriptor(&sd, FLT_PORT_ALL_ACCESS);
    if (!NT_SUCCESS(status)) {
        FltUnregisterFilter(Globals.Filter);
        return status;
    }

    InitializeObjectAttributes(&oa, &portName,
        OBJ_CASE_INSENSITIVE | OBJ_KERNEL_HANDLE, NULL, sd);

    status = FltCreateCommunicationPort(
        Globals.Filter,
        &Globals.ServerPort,
        &oa,
        NULL,
        FmPortConnect,
        FmPortDisconnect,
        FmPortMessageNotify,
        1                /* MaxConnections */
    );

    FltFreeSecurityDescriptor(sd);

    if (!NT_SUCCESS(status)) {
        FltUnregisterFilter(Globals.Filter);
        return status;
    }

    /* Start filtering */
    status = FltStartFiltering(Globals.Filter);
    if (!NT_SUCCESS(status)) {
        FltCloseCommunicationPort(Globals.ServerPort);
        FltUnregisterFilter(Globals.Filter);
        return status;
    }

    KdPrint(("FileMonitor: Driver loaded successfully.\n"));
    return STATUS_SUCCESS;
}

/* ------------------------------------------------------------------ */
/*                          Unload / Instance                         */
/* ------------------------------------------------------------------ */

NTSTATUS FmUnload(_In_ FLT_FILTER_UNLOAD_FLAGS Flags)
{
    UNREFERENCED_PARAMETER(Flags);

    KdPrint(("FileMonitor: Unloading driver.\n"));

    FltCloseCommunicationPort(Globals.ServerPort);

    if (Globals.ClientPort) {
        FltCloseClientPort(Globals.Filter, &Globals.ClientPort);
    }

    FltUnregisterFilter(Globals.Filter);
    return STATUS_SUCCESS;
}

NTSTATUS FmInstanceSetup(
    _In_ PCFLT_RELATED_OBJECTS    FltObjects,
    _In_ FLT_INSTANCE_SETUP_FLAGS Flags,
    _In_ DEVICE_TYPE              VolumeDeviceType,
    _In_ FLT_FILESYSTEM_TYPE      VolumeFilesystemType
)
{
    UNREFERENCED_PARAMETER(FltObjects);
    UNREFERENCED_PARAMETER(Flags);
    UNREFERENCED_PARAMETER(VolumeDeviceType);

    /* Only attach to NTFS volumes */
    if (VolumeFilesystemType != FLT_FSTYPE_NTFS) {
        return STATUS_FLT_DO_NOT_ATTACH;
    }

    return STATUS_SUCCESS;
}

/* ------------------------------------------------------------------ */
/*                     Communication Port Handlers                    */
/* ------------------------------------------------------------------ */

NTSTATUS FmPortConnect(
    _In_  PFLT_PORT ClientPort,
    _In_  PVOID     ServerPortCookie,
    _In_reads_bytes_(SizeOfContext) PVOID ConnectionContext,
    _In_  ULONG     SizeOfContext,
    _Out_ PVOID     *ConnectionCookie
)
{
    UNREFERENCED_PARAMETER(ServerPortCookie);
    UNREFERENCED_PARAMETER(ConnectionContext);
    UNREFERENCED_PARAMETER(SizeOfContext);
    UNREFERENCED_PARAMETER(ConnectionCookie);

    ExAcquireFastMutex(&Globals.StateLock);
    Globals.ClientPort = ClientPort;
    ExReleaseFastMutex(&Globals.StateLock);

    KdPrint(("FileMonitor: User-mode client connected.\n"));
    return STATUS_SUCCESS;
}

VOID FmPortDisconnect(_In_opt_ PVOID ConnectionCookie)
{
    UNREFERENCED_PARAMETER(ConnectionCookie);

    ExAcquireFastMutex(&Globals.StateLock);
    FltCloseClientPort(Globals.Filter, &Globals.ClientPort);
    Globals.ClientPort = NULL;
    ExReleaseFastMutex(&Globals.StateLock);

    KdPrint(("FileMonitor: User-mode client disconnected.\n"));
}

NTSTATUS FmPortMessageNotify(
    _In_  PVOID  PortCookie,
    _In_reads_bytes_(InputBufferLength) PVOID InputBuffer,
    _In_  ULONG  InputBufferLength,
    _Out_writes_bytes_to_(OutputBufferLength, *ReturnOutputBufferLength) PVOID OutputBuffer,
    _In_  ULONG  OutputBufferLength,
    _Out_ PULONG ReturnOutputBufferLength
)
{
    PFILE_MONITOR_COMMAND cmd;
    FILE_MONITOR_REPLY reply;

    UNREFERENCED_PARAMETER(PortCookie);

    *ReturnOutputBufferLength = 0;

    if (InputBuffer == NULL || InputBufferLength < sizeof(FILE_MONITOR_COMMAND)) {
        return STATUS_INVALID_PARAMETER;
    }

    cmd = (PFILE_MONITOR_COMMAND)InputBuffer;

    switch (cmd->Command) {
    case CommandStartMonitoring:
        ExAcquireFastMutex(&Globals.StateLock);
        Globals.MonitoringActive = TRUE;
        ExReleaseFastMutex(&Globals.StateLock);
        KdPrint(("FileMonitor: Monitoring STARTED.\n"));
        reply.Status = STATUS_SUCCESS;
        break;

    case CommandStopMonitoring:
        ExAcquireFastMutex(&Globals.StateLock);
        Globals.MonitoringActive = FALSE;
        ExReleaseFastMutex(&Globals.StateLock);
        KdPrint(("FileMonitor: Monitoring STOPPED.\n"));
        reply.Status = STATUS_SUCCESS;
        break;

    default:
        reply.Status = STATUS_INVALID_PARAMETER;
        break;
    }

    if (OutputBuffer != NULL && OutputBufferLength >= sizeof(FILE_MONITOR_REPLY)) {
        RtlCopyMemory(OutputBuffer, &reply, sizeof(FILE_MONITOR_REPLY));
        *ReturnOutputBufferLength = sizeof(FILE_MONITOR_REPLY);
    }

    return STATUS_SUCCESS;
}

/* ------------------------------------------------------------------ */
/*                     Send notification to user mode                 */
/* ------------------------------------------------------------------ */

NTSTATUS FmSendNotification(
    _In_ FILE_MONITOR_EVENT_TYPE EventType,
    _In_ PFLT_CALLBACK_DATA     Data,
    _In_ PCFLT_RELATED_OBJECTS  FltObjects
)
{
    NTSTATUS status;
    FILE_MONITOR_NOTIFICATION notification;
    PFLT_FILE_NAME_INFORMATION nameInfo = NULL;
    ULONG replyLength = 0;
    LARGE_INTEGER timestamp;

    UNREFERENCED_PARAMETER(FltObjects);

    /* Check if client is connected */
    if (Globals.ClientPort == NULL) {
        return STATUS_PORT_DISCONNECTED;
    }

    RtlZeroMemory(&notification, sizeof(notification));

    notification.EventType = (ULONG)EventType;
    notification.ProcessId = (ULONG)(ULONG_PTR)PsGetCurrentProcessId();
    notification.ThreadId  = (ULONG)(ULONG_PTR)PsGetCurrentThreadId();
    KeQuerySystemTime(&timestamp);
    notification.Timestamp = timestamp;

    /* Get the file name */
    status = FltGetFileNameInformation(Data,
        FLT_FILE_NAME_NORMALIZED | FLT_FILE_NAME_QUERY_DEFAULT,
        &nameInfo);

    if (NT_SUCCESS(status)) {
        status = FltParseFileNameInformation(nameInfo);
        if (NT_SUCCESS(status)) {
            ULONG copyLen = min(nameInfo->Name.Length,
                (FILE_MONITOR_MAX_PATH - 1) * sizeof(WCHAR));
            RtlCopyMemory(notification.FilePath, nameInfo->Name.Buffer, copyLen);
            notification.FilePathLength = copyLen;
        }
        FltReleaseFileNameInformation(nameInfo);
    }

    /* Send to user mode - non-blocking with timeout */
    status = FltSendMessage(
        Globals.Filter,
        &Globals.ClientPort,
        &notification,
        sizeof(notification),
        NULL,
        &replyLength,
        NULL
    );

    return status;
}

/* ------------------------------------------------------------------ */
/*                     Pre/Post Operation Callbacks                   */
/* ------------------------------------------------------------------ */

FLT_PREOP_CALLBACK_STATUS FmPreCreateCallback(
    _Inout_ PFLT_CALLBACK_DATA    Data,
    _In_    PCFLT_RELATED_OBJECTS FltObjects,
    _Out_   PVOID                 *CompletionContext
)
{
    UNREFERENCED_PARAMETER(Data);
    UNREFERENCED_PARAMETER(FltObjects);
    *CompletionContext = NULL;

    if (!Globals.MonitoringActive || Globals.ClientPort == NULL) {
        return FLT_PREOP_SUCCESS_NO_CALLBACK;
    }

    return FLT_PREOP_SUCCESS_WITH_CALLBACK;
}

FLT_POSTOP_CALLBACK_STATUS FmPostCreateCallback(
    _Inout_  PFLT_CALLBACK_DATA       Data,
    _In_     PCFLT_RELATED_OBJECTS    FltObjects,
    _In_opt_ PVOID                    CompletionContext,
    _In_     FLT_POST_OPERATION_FLAGS Flags
)
{
    UNREFERENCED_PARAMETER(CompletionContext);
    UNREFERENCED_PARAMETER(FltObjects);

    if (FlagOn(Flags, FLTFL_POST_OPERATION_DRAINING)) {
        return FLT_POSTOP_FINISHED_PROCESSING;
    }

    /* Only notify on successful creates */
    if (!NT_SUCCESS(Data->IoStatus.Status)) {
        return FLT_POSTOP_FINISHED_PROCESSING;
    }

    /* Determine if this was a create or delete-on-close */
    if (Data->Iopb->Parameters.Create.Options & FILE_DELETE_ON_CLOSE) {
        FmSendNotification(FileEventDelete, Data, FltObjects);
    } else {
        FmSendNotification(FileEventCreate, Data, FltObjects);
    }

    return FLT_POSTOP_FINISHED_PROCESSING;
}

FLT_PREOP_CALLBACK_STATUS FmPreWriteCallback(
    _Inout_ PFLT_CALLBACK_DATA    Data,
    _In_    PCFLT_RELATED_OBJECTS FltObjects,
    _Out_   PVOID                 *CompletionContext
)
{
    *CompletionContext = NULL;

    if (!Globals.MonitoringActive || Globals.ClientPort == NULL) {
        return FLT_PREOP_SUCCESS_NO_CALLBACK;
    }

    FmSendNotification(FileEventWrite, Data, FltObjects);
    return FLT_PREOP_SUCCESS_NO_CALLBACK;
}

FLT_PREOP_CALLBACK_STATUS FmPreSetInfoCallback(
    _Inout_ PFLT_CALLBACK_DATA    Data,
    _In_    PCFLT_RELATED_OBJECTS FltObjects,
    _Out_   PVOID                 *CompletionContext
)
{
    FILE_MONITOR_EVENT_TYPE eventType;

    *CompletionContext = NULL;

    if (!Globals.MonitoringActive || Globals.ClientPort == NULL) {
        return FLT_PREOP_SUCCESS_NO_CALLBACK;
    }

    switch (Data->Iopb->Parameters.SetFileInformation.FileInformationClass) {
    case FileRenameInformation:
    case FileRenameInformationEx:
        eventType = FileEventRename;
        break;
    case FileDispositionInformation:
    case FileDispositionInformationEx:
        eventType = FileEventDelete;
        break;
    default:
        eventType = FileEventSetInfo;
        break;
    }

    FmSendNotification(eventType, Data, FltObjects);
    return FLT_PREOP_SUCCESS_NO_CALLBACK;
}

FLT_PREOP_CALLBACK_STATUS FmPreCleanupCallback(
    _Inout_ PFLT_CALLBACK_DATA    Data,
    _In_    PCFLT_RELATED_OBJECTS FltObjects,
    _Out_   PVOID                 *CompletionContext
)
{
    *CompletionContext = NULL;

    if (!Globals.MonitoringActive || Globals.ClientPort == NULL) {
        return FLT_PREOP_SUCCESS_NO_CALLBACK;
    }

    FmSendNotification(FileEventCleanup, Data, FltObjects);
    return FLT_PREOP_SUCCESS_NO_CALLBACK;
}
