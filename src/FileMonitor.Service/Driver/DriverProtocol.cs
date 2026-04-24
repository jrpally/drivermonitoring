// Author: Rene Pally

using System.Runtime.InteropServices;

namespace FileMonitor.Service.Driver;

/// <summary>
/// Matches the shared.h structures used by the kernel driver.
/// </summary>
public static class DriverProtocol
{
    public const string PortName = "\\FileMonitorPort";
    public const int MaxPath = 1024;

    /// <summary>
    /// Header prepended by Filter Manager on every message from the driver.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FilterMessageHeader
    {
        public uint ReplyLength;
        public ulong MessageId;
    }

    /// <summary>
    /// Notification message from the driver (matches FILE_MONITOR_NOTIFICATION).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct FileMonitorNotification
    {
        public uint EventType;
        public uint ProcessId;
        public uint ThreadId;
        public long Timestamp;
        public uint FilePathLength;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)]
        public string FilePath;
    }

    /// <summary>
    /// Full message buffer = header + notification body.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct FilterMessage
    {
        public FilterMessageHeader Header;
        public FileMonitorNotification Notification;
    }

    public enum CommandType : uint
    {
        StartMonitoring = 1,
        StopMonitoring = 2,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FileMonitorCommand
    {
        public CommandType Command;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FileMonitorReply
    {
        public int Status; // NTSTATUS
    }
}
