// Author: Rene Pally

using System.Runtime.InteropServices;

namespace FileMonitor.Service.Driver;

/// <summary>
/// P/Invoke declarations for Filter Manager user-mode API (fltlib.dll).
/// </summary>
internal static partial class FilterApi
{
    private const string FltLib = "fltlib.dll";
    private const string Kernel32 = "kernel32.dll";

    [LibraryImport(FltLib, EntryPoint = "FilterConnectCommunicationPort",
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int FilterConnectCommunicationPort(
        string portName,
        uint options,
        IntPtr context,
        uint sizeOfContext,
        IntPtr securityAttributes,
        out SafeFilterHandle port);

    [LibraryImport(FltLib, EntryPoint = "FilterSendMessage")]
    internal static partial int FilterSendMessage(
        SafeFilterHandle port,
        IntPtr inBuffer,
        uint inBufferSize,
        IntPtr outBuffer,
        uint outBufferSize,
        out uint bytesReturned);

    [LibraryImport(FltLib, EntryPoint = "FilterGetMessage")]
    internal static partial int FilterGetMessage(
        SafeFilterHandle port,
        IntPtr msgBuffer,
        uint msgBufferSize,
        IntPtr overlapped);

    [LibraryImport(Kernel32, EntryPoint = "CloseHandle")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr handle);
}

/// <summary>
/// Safe handle wrapper for the filter communication port.
/// </summary>
internal sealed class SafeFilterHandle : SafeHandle
{
    public SafeFilterHandle() : base(IntPtr.Zero, true) { }

    public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);

    protected override bool ReleaseHandle()
    {
        return FilterApi.CloseHandle(handle);
    }
}
