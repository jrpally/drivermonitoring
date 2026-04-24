// Author: Rene Pally
//
// FileMonitor — self-contained installer and launcher.
//
// What it does (as Administrator):
//   1. Extracts and loads the FileMonitorDriver.sys minifilter kernel driver.
//   2. Extracts FileMonitor.Service.exe to %ProgramData%\FileMonitor\ and
//      installs it as a Windows Service.
//   3. Starts the service — it connects to the driver and exposes file events
//      via gRPC on http://localhost:50051.
//   4. Waits. Press Ctrl+C to stop the service and uninstall everything.
//
// Clients connect to http://localhost:50051 using:
//   • FileMonitor.Client (C# SDK)
//   • Any gRPC client generated from proto/file_monitor.proto

using System.Security.Principal;
using FileMonitor.Live;

// ── Admin check ───────────────────────────────────────────────────────────
var identity  = WindowsIdentity.GetCurrent();
var principal = new WindowsPrincipal(identity);
if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine("ERROR: Must run as Administrator.");
    Console.ResetColor();
    return 1;
}

// ── Setup ─────────────────────────────────────────────────────────────────
var driverMgr  = new DriverManager();
var serviceMgr = new ServiceManager();
var cts        = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    serviceMgr.Dispose();
    driverMgr.Dispose();
};

// ── Banner ────────────────────────────────────────────────────────────────
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔═══════════════════════════════════════════════════╗");
Console.WriteLine("║          FileMonitor  —  Rene Pally              ║");
Console.WriteLine("║  Kernel-level file system monitor  |  gRPC/HTTP2 ║");
Console.WriteLine("╚═══════════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();

// ── Install directory ─────────────────────────────────────────────────────
var installDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "FileMonitor");
Directory.CreateDirectory(installDir);

// ── 1. Driver ─────────────────────────────────────────────────────────────
Console.Write("Extracting driver...      ");
try   { driverMgr.Extract();                      WriteOk(); }
catch (Exception ex) { WriteFail(ex.Message);     return 1; }

Console.Write("Installing driver...      ");
try
{
    if (!driverMgr.Install()) { WriteFail("fltmc load failed"); return 1; }
    WriteOk();
}
catch (Exception ex) { WriteFail(ex.Message); return 1; }

// ── 2. Service ────────────────────────────────────────────────────────────
var serviceExePath = Path.Combine(installDir, "FileMonitor.Service.exe");

Console.Write("Extracting service...     ");
try   { serviceMgr.Extract(serviceExePath);        WriteOk(); }
catch (Exception ex) { WriteFail(ex.Message);      return 1; }

Console.Write("Installing service...     ");
try
{
    if (!serviceMgr.Install()) { WriteFail("sc create failed"); return 1; }
    WriteOk();
}
catch (Exception ex) { WriteFail(ex.Message); return 1; }

Console.Write("Starting service...       ");
try
{
    if (!serviceMgr.Start()) { WriteFail("sc start failed"); return 1; }
    WriteOk();
}
catch (Exception ex) { WriteFail(ex.Message); return 1; }

// ── 3. Ready ──────────────────────────────────────────────────────────────
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("  FileMonitor is running.");
Console.ResetColor();
Console.ForegroundColor = ConsoleColor.DarkCyan;
Console.WriteLine("  gRPC endpoint:  http://localhost:50051");
Console.WriteLine($"  Install path:   {installDir}");
Console.ResetColor();
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine("  Connect with FileMonitor.Client (C#) or any gRPC client.");
Console.WriteLine("  See live/examples/ for Python and C# samples.");
Console.WriteLine();
Console.WriteLine("  Press Ctrl+C to stop the service and uninstall everything.");
Console.ResetColor();
Console.WriteLine();

// ── 4. Wait ───────────────────────────────────────────────────────────────
await Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => { });

// ── 5. Shutdown ───────────────────────────────────────────────────────────
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("Stopping service and uninstalling...");
Console.ResetColor();

serviceMgr.Uninstall();   // stop + sc delete
driverMgr.Dispose();      // fltmc unload + registry cleanup

// Clean up the install directory (best effort — service exe may briefly still be open)
try { Directory.Delete(installDir, recursive: true); }
catch { /* best effort */ }

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("Uninstalled. Goodbye.");
Console.ResetColor();
return 0;

// ── Helpers ───────────────────────────────────────────────────────────────

static void WriteOk()
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("OK");
    Console.ResetColor();
}

static void WriteFail(string message)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"FAILED: {message}");
    Console.ResetColor();
}

