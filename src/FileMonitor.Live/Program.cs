// Author: Rene Pally
//
// FileMonitor Live — self-contained file system monitor.
// Extracts the minifilter driver, installs it, monitors events,
// and cleans up everything on exit. Like Sysinternals Process Monitor.

using System.Security.Principal;
using FileMonitor.Live;

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

// ── Setup ───────────────────────────────────────────────────────────────
using var manager = new DriverManager();
var cts = new CancellationTokenSource();

// Ensure cleanup on Ctrl+C, console close, process exit
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => manager.Dispose();

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔══════════════════════════════════════════════════╗");
Console.WriteLine("║          FileMonitor Live  —  Rene Pally        ║");
Console.WriteLine("║   Self-contained file system event monitor      ║");
Console.WriteLine("╚══════════════════════════════════════════════════╝");
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
        if (processName.Length > 18) processName = processName[..18] + "…";
        var eventName = GetEventName(n.EventType);
        var color = GetEventColor(n.EventType);

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
