// Author: Rene Pally
//
// ServiceManager — extracts the embedded FileMonitor.Service.exe, installs it
// as a Windows Service, starts it, and removes it on dispose.

using System.Diagnostics;
using System.Reflection;

namespace FileMonitor.Live;

internal sealed class ServiceManager : IDisposable
{
    private const string ServiceName        = "FileMonitorService";
    private const string ServiceDisplayName = "FileMonitor Service";
    private const string ServiceDescription =
        "Connects to the FileMonitorDriver minifilter and exposes file events via gRPC on localhost:50051.";

    private string? _serviceExePath;
    private bool    _serviceInstalled;
    private bool    _disposed;

    // ── Extract ─────────────────────────────────────────────────────────

    /// <summary>
    /// Extract the embedded FileMonitor.Service.exe to <paramref name="destPath"/>.
    /// </summary>
    public void Extract(string destPath)
    {
        _serviceExePath = destPath;

        using var stream = Assembly.GetExecutingAssembly()
                               .GetManifestResourceStream("FileMonitor.Service.exe")
                           ?? throw new InvalidOperationException(
                               "Embedded resource 'FileMonitor.Service.exe' not found. " +
                               "Publish FileMonitor.Service as single-file first.");

        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        using var fs = File.Create(destPath);
        stream.CopyTo(fs);
    }

    // ── Lifecycle ────────────────────────────────────────────────────────

    /// <summary>
    /// Register the extracted exe as a Windows Service (demand-start, own process).
    /// Removes any stale registration with the same name first.
    /// </summary>
    public bool Install()
    {
        if (_serviceExePath == null)
            throw new InvalidOperationException("Call Extract() first.");

        // Remove any leftover registration (best effort, ignore errors)
        RunProcess("sc.exe", $"delete \"{ServiceName}\"");
        Thread.Sleep(500);

        var (exitCode, output) = RunProcess("sc.exe",
            $"create \"{ServiceName}\" " +
            $"binPath= \"{_serviceExePath}\" " +
            $"DisplayName= \"{ServiceDisplayName}\" " +
            $"start= demand type= own");

        if (exitCode != 0)
        {
            Console.Error.WriteLine($"  sc create: {output}");
            return false;
        }

        // Set description (best effort)
        RunProcess("sc.exe", $"description \"{ServiceName}\" \"{ServiceDescription}\"");

        _serviceInstalled = true;
        return true;
    }

    /// <summary>Start the installed service.</summary>
    public bool Start()
    {
        var (exitCode, output) = RunProcess("sc.exe", $"start \"{ServiceName}\"");
        if (exitCode != 0)
            Console.Error.WriteLine($"  sc start: {output}");
        return exitCode == 0;
    }

    /// <summary>Stop the service (tolerates "not started" gracefully).</summary>
    public bool Stop()
    {
        var (exitCode, _) = RunProcess("sc.exe", $"stop \"{ServiceName}\"");
        // exit code 1062 = ERROR_SERVICE_NOT_ACTIVE — already stopped, that's fine
        return exitCode == 0 || exitCode == 1062;
    }

    /// <summary>Stop then delete the service registration.</summary>
    public void Uninstall()
    {
        Stop();
        Thread.Sleep(2000); // give SCM time to terminate the process
        RunProcess("sc.exe", $"delete \"{ServiceName}\"");
        _serviceInstalled = false;
    }

    // ── Dispose ─────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_serviceInstalled) Uninstall();
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static (int ExitCode, string Output) RunProcess(string fileName, string arguments)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName               = fileName,
            Arguments              = arguments,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };
        proc.Start();
        string output = proc.StandardOutput.ReadToEnd()
                      + proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, output.Trim());
    }
}
