// Author: Rene Pally
//
// FileMonitor Live — self-contained file system monitor.
// Extracts the minifilter driver, installs it, monitors events,
// hosts a gRPC server on localhost:50051 for remote clients,
// and cleans up everything on exit. Like Sysinternals Process Monitor.

using System.Security.Principal;
using FileMonitor.Grpc;
using FileMonitor.Live;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

// ── Admin check ─────────────────────────────────────────────────────────
var identity = WindowsIdentity.GetCurrent();
var principal = new WindowsPrincipal(identity);
if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine("ERROR: Must run as Administrator.");
    Console.ResetColor();
    return 1;
}

// ── Build gRPC host ──────────────────────────────────────────────────────
var manager     = new DriverManager();
var broadcaster = new LiveEventBroadcaster();
var cts         = new CancellationTokenSource();

var webBuilder = WebApplication.CreateBuilder(args);
webBuilder.WebHost.ConfigureKestrel(options =>
    options.ListenLocalhost(50051, lo => lo.Protocols = HttpProtocols.Http2));
webBuilder.Services.AddGrpc();
webBuilder.Services.AddSingleton(broadcaster);
webBuilder.Services.AddSingleton(manager);
var app = webBuilder.Build();
app.MapGrpcService<LiveFileMonitorGrpcService>();

// Ensure cleanup on Ctrl+C, console close, process exit
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    broadcaster.Complete();
    manager.Dispose();
};

// ── Banner ───────────────────────────────────────────────────────────────
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔══════════════════════════════════════════════════╗");
Console.WriteLine("║          FileMonitor Live  —  Rene Pally        ║");
Console.WriteLine("║   Self-contained file system event monitor      ║");
Console.WriteLine("╚══════════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();

// ── Start gRPC server ────────────────────────────────────────────────────
await app.StartAsync();
Console.ForegroundColor = ConsoleColor.DarkCyan;
Console.WriteLine("gRPC server listening on  http://localhost:50051");
Console.ResetColor();
Console.WriteLine();

// ── Extract & Install ───────────────────────────────────────────────────
Console.Write("Extracting driver... ");
try
{
    manager.Extract();
    WriteOk();
}
catch (Exception ex)
{
    WriteFail(ex.Message);
    return 1;
}

Console.Write("Installing driver... ");
try
{
    if (!manager.Install())
    {
        WriteFail("fltmc load failed");
        return 1;
    }
    WriteOk();
}
catch (Exception ex)
{
    WriteFail(ex.Message);
    return 1;
}

// ── Connect ─────────────────────────────────────────────────────────────
Console.Write("Connecting to driver... ");

// Retry connection a few times (driver may take a moment to initialize port)
bool connected = false;
for (int i = 0; i < 10 && !connected; i++)
{
    connected = manager.Connect();
    if (!connected) Thread.Sleep(500);
}
if (!connected)
{
    WriteFail("could not connect to communication port");
    return 1;
}
WriteOk();

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("Monitoring file system events. Press Q to quit, S to stop, R to resume.");
Console.ResetColor();
Console.WriteLine(new string('─', 80));
Console.WriteLine($"{"Time",-12} {"PID",6} {"Process",-20} {"Event",-10} Path");
Console.WriteLine(new string('─', 80));

// ── Event listener thread ───────────────────────────────────────────────
var listenerTask = Task.Run(() =>
{
    while (!cts.IsCancellationRequested)
    {
        var notification = manager.GetNextNotification();
        if (notification == null)
        {
            if (cts.IsCancellationRequested) break;
            Thread.Sleep(100);
            continue;
        }

        var n = notification.Value;
        var time = DateTime.Now.ToString("HH:mm:ss.ff");
        var processName = DriverManager.ResolveProcessName(n.ProcessId);
        if (processName.Length > 18) processName = processName[..18] + "\u2026";
        var eventName = GetEventName(n.EventType);
        var color = GetEventColor(n.EventType);

        // ── Console display ──────────────────────────────────────────────
        lock (Console.Out)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"{time,-12} ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"{n.ProcessId,6} ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write($"{processName,-20} ");
            Console.ForegroundColor = color;
            Console.Write($"{eventName,-10} ");
            Console.ResetColor();
            Console.WriteLine(n.FilePath);
        }

        // ── Broadcast to gRPC subscribers ────────────────────────────────
        broadcaster.Publish(new FileEvent
        {
            EventType   = (FileEventType)n.EventType,
            FilePath    = n.FilePath ?? string.Empty,
            ProcessId   = n.ProcessId,
            ThreadId    = n.ThreadId,
            Timestamp   = n.Timestamp,
            ProcessName = processName,
        });
    }
}, cts.Token);

// ── Keyboard input loop ────────────────────────────────────────────────
while (!cts.IsCancellationRequested)
{
    if (Console.KeyAvailable)
    {
        var key = Console.ReadKey(intercept: true).Key;
        switch (key)
        {
            case ConsoleKey.Q:
                cts.Cancel();
                break;
            case ConsoleKey.S:
                manager.SendCommand(DriverManager.CommandType.StopMonitoring);
                WriteStatus("Monitoring paused");
                break;
            case ConsoleKey.R:
                manager.SendCommand(DriverManager.CommandType.StartMonitoring);
                WriteStatus("Monitoring resumed");
                break;
        }
    }
    else
    {
        Thread.Sleep(50);
    }
}

// ── Shutdown ────────────────────────────────────────────────────────────
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Yellow;
Console.Write("Shutting down... ");
Console.ResetColor();

try { await listenerTask.WaitAsync(TimeSpan.FromSeconds(3)); }
catch { /* listener may already be done */ }

// Signal all gRPC subscribers that the stream is ending
broadcaster.Complete();

// Stop the gRPC server
await app.StopAsync();

// Dispose triggers Uninstall → fltmc unload → sc delete → cleanup
manager.Dispose();

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("Driver unloaded, files cleaned up. Goodbye.");
Console.ResetColor();
return 0;

// ── Helpers ─────────────────────────────────────────────────────────────

static string GetEventName(uint eventType) => eventType switch
{
    0x01 => "CREATE",
    0x02 => "CLOSE",
    0x04 => "READ",
    0x08 => "WRITE",
    0x10 => "DELETE",
    0x20 => "RENAME",
    0x40 => "SETINFO",
    0x80 => "CLEANUP",
    _ => $"0x{eventType:X2}",
};

static ConsoleColor GetEventColor(uint eventType) => eventType switch
{
    0x01 => ConsoleColor.Green,      // CREATE
    0x08 => ConsoleColor.Yellow,     // WRITE
    0x10 => ConsoleColor.Red,        // DELETE
    0x20 => ConsoleColor.Cyan,       // RENAME
    0x02 => ConsoleColor.DarkGray,   // CLOSE
    0x80 => ConsoleColor.DarkGray,   // CLEANUP
    _ => ConsoleColor.White,
};

static void WriteOk()
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("OK");
    Console.ResetColor();
}

static void WriteFail(string message)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"FAILED — {message}");
    Console.ResetColor();
}

static void WriteStatus(string message)
{
    lock (Console.Out)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"  [{message}]");
        Console.ResetColor();
    }
}
