// Author: Rene Pally
//
// DriverManager - Extracts the embedded minifilter driver, installs/loads it,
// provides direct communication, and cleans up on dispose.
// Self-contained like Sysinternals Process Monitor.

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FileMonitor.Live;

internal sealed partial class DriverManager : IDisposable
{
    private const string DriverName = "FileMonitorDriver";
    private const string PortName = "\\FileMonitorPort";
    private const int MaxPath = 1024;

    private string? _tempDir;
    private string? _sysPath;
    private string? _infPath;
    private SafeFilterHandle? _port;
    private bool _driverInstalled;
    private bool _disposed;

    // ── Embedded resource extraction ────────────────────────────────────

    public void Extract()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FileMonitorLive_" + Environment.ProcessId);
        Directory.CreateDirectory(_tempDir);

        _sysPath = Path.Combine(_tempDir, "FileMonitorDriver.sys");
        _infPath = Path.Combine(_tempDir, "FileMonitorDriver.inf");

        ExtractResource("FileMonitorDriver.sys", _sysPath);
        ExtractResource("FileMonitorDriver.inf", _infPath);
    }

    private static void ExtractResource(string name, string destPath)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource '{name}' not found.");
        using var fs = File.Create(destPath);
        stream.CopyTo(fs);
    }

    // ── Driver install / uninstall ──────────────────────────────────────

    public bool Install()
    {
        if (_sysPath == null || _infPath == null)
            throw new InvalidOperationException("Call Extract() first.");

        // Copy driver to system32\drivers
        var destDriver = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "drivers", "FileMonitorDriver.sys");
        File.Copy(_sysPath, destDriver, overwrite: true);

        // Register via INF
        RunProcess("rundll32.exe",
            $"setupapi.dll,InstallHinfSection DefaultInstall 132 {_infPath}");

        // Load minifilter
        var (exitCode, _) = RunProcess("fltmc.exe", $"load {DriverName}");
        _driverInstalled = exitCode == 0;

        return _driverInstalled;
    }

    public void Uninstall()
    {
        // Disconnect first
        Disconnect();

        // Unload minifilter
        RunProcess("fltmc.exe", $"unload {DriverName}");
        _driverInstalled = false;

        // Remove driver file
        var destDriver = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "drivers", "FileMonitorDriver.sys");
        try { File.Delete(destDriver); } catch { /* best effort */ }

        // Remove registry entries
        RunProcess("reg.exe",
            $@"delete HKLM\SYSTEM\CurrentControlSet\Services\{DriverName} /f");

        // Clean temp directory
        if (_tempDir != null)
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    // ── Driver communication ────────────────────────────────────────────

    public bool Connect()
    {
        int hr = FilterConnectCommunicationPort(
            PortName, 0, IntPtr.Zero, 0, IntPtr.Zero, out var port);

        if (hr != 0)
        {
            Console.Error.WriteLine($"Failed to connect to driver port. HRESULT: 0x{hr:X8}");
            return false;
        }

        _port = port;
        return true;
    }

    public void Disconnect()
    {
        _port?.Dispose();
        _port = null;
    }

    public bool SendCommand(CommandType command)
    {
        if (_port == null || _port.IsInvalid) return false;

        var cmd = new FileMonitorCommand { Command = command };
        int cmdSize = Marshal.SizeOf<FileMonitorCommand>();
        int replySize = Marshal.SizeOf<FileMonitorReply>();
        IntPtr inBuf = Marshal.AllocHGlobal(cmdSize);
        IntPtr outBuf = Marshal.AllocHGlobal(replySize);

        try
        {
            Marshal.StructureToPtr(cmd, inBuf, false);
            int hr = FilterSendMessage(_port, inBuf, (uint)cmdSize,
                outBuf, (uint)replySize, out _);
            if (hr != 0) return false;

            var reply = Marshal.PtrToStructure<FileMonitorReply>(outBuf);
            return reply.Status >= 0;
        }
        finally
        {
            Marshal.FreeHGlobal(inBuf);
            Marshal.FreeHGlobal(outBuf);
        }
    }

    public FileMonitorNotification? GetNextNotification()
    {
        if (_port == null || _port.IsInvalid) return null;

        int msgSize = Marshal.SizeOf<FilterMessage>();
        IntPtr msgBuf = Marshal.AllocHGlobal(msgSize);

        try
        {
            int hr = FilterGetMessage(_port, msgBuf, (uint)msgSize, IntPtr.Zero);

            if (hr != 0)
            {
                // ERROR_OPERATION_ABORTED expected during shutdown
                if ((uint)hr != 0x800703E3)
                    Console.Error.WriteLine($"FilterGetMessage failed: 0x{hr:X8}");
                return null;
            }

            var msg = Marshal.PtrToStructure<FilterMessage>(msgBuf);
            return msg.Notification;
        }
        finally
        {
            Marshal.FreeHGlobal(msgBuf);
        }
    }

    public static string ResolveProcessName(uint processId)
    {
        try
        {
            using var proc = Process.GetProcessById((int)processId);
            return proc.ProcessName;
        }
        catch { return string.Empty; }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static (int ExitCode, string Output) RunProcess(string fileName, string arguments)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        proc.Start();
        string output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, output);
    }

    // ── Dispose ─────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_driverInstalled)
            Uninstall();
        else
            Disconnect();
    }

    // ── P/Invoke ────────────────────────────────────────────────────────

    [LibraryImport("fltlib.dll", EntryPoint = "FilterConnectCommunicationPort",
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial int FilterConnectCommunicationPort(
        string portName, uint options, nint context,
        uint sizeOfContext, nint securityAttributes,
        out SafeFilterHandle port);

    [LibraryImport("fltlib.dll", EntryPoint = "FilterSendMessage")]
    private static partial int FilterSendMessage(
        SafeFilterHandle port, nint inBuffer, uint inBufferSize,
        nint outBuffer, uint outBufferSize, out uint bytesReturned);

    [LibraryImport("fltlib.dll", EntryPoint = "FilterGetMessage")]
    private static partial int FilterGetMessage(
        SafeFilterHandle port, nint msgBuffer, uint msgBufferSize,
        nint overlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    // ── Interop structs ─────────────────────────────────────────────────

    internal sealed class SafeFilterHandle : SafeHandle
    {
        public SafeFilterHandle() : base(IntPtr.Zero, true) { }
        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);
        protected override bool ReleaseHandle() => CloseHandle((nint)handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FilterMessageHeader
    {
        public uint ReplyLength;
        public ulong MessageId;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct FileMonitorNotification
    {
        public uint EventType;
        public uint ProcessId;
        public uint ThreadId;
        public long Timestamp;
        public uint FilePathLength;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)]
        public string FilePath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct FilterMessage
    {
        public FilterMessageHeader Header;
        public FileMonitorNotification Notification;
    }

    internal enum CommandType : uint
    {
        StartMonitoring = 1,
        StopMonitoring = 2,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileMonitorCommand
    {
        public CommandType Command;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileMonitorReply
    {
        public int Status;
    }
}
