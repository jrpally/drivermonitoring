// Author: Rene Pally

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace FileMonitor.Service.Driver;

/// <summary>
/// Manages the communication channel with the FileMonitor minifilter driver.
/// Connects via the filter communication port and provides methods to
/// send commands and receive notifications.
/// </summary>
public sealed class DriverCommunication : IDisposable
{
    private readonly ILogger<DriverCommunication> _logger;
    private SafeFilterHandle? _port;
    private bool _disposed;

    public bool IsConnected => _port is { IsInvalid: false };

    public DriverCommunication(ILogger<DriverCommunication> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Connect to the driver's communication port.
    /// </summary>
    public bool Connect()
    {
        if (IsConnected) return true;

        int hr = FilterApi.FilterConnectCommunicationPort(
            DriverProtocol.PortName,
            0,
            IntPtr.Zero,
            0,
            IntPtr.Zero,
            out var port);

        if (hr != 0)
        {
            _logger.LogError("Failed to connect to driver port. HRESULT: 0x{Hr:X8}", hr);
            return false;
        }

        _port = port;
        _logger.LogInformation("Connected to driver communication port.");
        return true;
    }

    /// <summary>
    /// Send a command to the driver (start/stop monitoring).
    /// </summary>
    public bool SendCommand(DriverProtocol.CommandType command)
    {
        if (!IsConnected)
        {
            _logger.LogWarning("Cannot send command - not connected to driver.");
            return false;
        }

        var cmd = new DriverProtocol.FileMonitorCommand { Command = command };
        int cmdSize = Marshal.SizeOf<DriverProtocol.FileMonitorCommand>();
        int replySize = Marshal.SizeOf<DriverProtocol.FileMonitorReply>();

        IntPtr inBuf = Marshal.AllocHGlobal(cmdSize);
        IntPtr outBuf = Marshal.AllocHGlobal(replySize);

        try
        {
            Marshal.StructureToPtr(cmd, inBuf, false);

            int hr = FilterApi.FilterSendMessage(
                _port!,
                inBuf, (uint)cmdSize,
                outBuf, (uint)replySize,
                out _);

            if (hr != 0)
            {
                _logger.LogError("FilterSendMessage failed. HRESULT: 0x{Hr:X8}", hr);
                return false;
            }

            var reply = Marshal.PtrToStructure<DriverProtocol.FileMonitorReply>(outBuf);
            _logger.LogInformation("Command {Command} sent. Driver replied with status: 0x{Status:X8}",
                command, reply.Status);

            return reply.Status >= 0; // NT_SUCCESS
        }
        finally
        {
            Marshal.FreeHGlobal(inBuf);
            Marshal.FreeHGlobal(outBuf);
        }
    }

    /// <summary>
    /// Blocking call to receive the next notification from the driver.
    /// Call from a dedicated thread/task.
    /// </summary>
    public DriverProtocol.FileMonitorNotification? GetNextNotification()
    {
        if (!IsConnected) return null;

        int msgSize = Marshal.SizeOf<DriverProtocol.FilterMessage>();
        IntPtr msgBuf = Marshal.AllocHGlobal(msgSize);

        try
        {
            int hr = FilterApi.FilterGetMessage(
                _port!,
                msgBuf,
                (uint)msgSize,
                IntPtr.Zero);

            if (hr != 0)
            {
                // ERROR_OPERATION_ABORTED (0x800703E3) is expected during shutdown
                if ((uint)hr != 0x800703E3)
                {
                    _logger.LogError("FilterGetMessage failed. HRESULT: 0x{Hr:X8}", hr);
                }
                return null;
            }

            var msg = Marshal.PtrToStructure<DriverProtocol.FilterMessage>(msgBuf);
            return msg.Notification;
        }
        finally
        {
            Marshal.FreeHGlobal(msgBuf);
        }
    }

    /// <summary>
    /// Resolve process name from PID. Returns empty string on failure.
    /// </summary>
    public static string ResolveProcessName(uint processId)
    {
        try
        {
            using var proc = Process.GetProcessById((int)processId);
            return proc.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Disconnect()
    {
        _port?.Dispose();
        _port = null;
        _logger.LogInformation("Disconnected from driver.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
    }
}
