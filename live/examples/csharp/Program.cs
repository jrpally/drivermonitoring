// FileMonitor C# client example
// ================================
// Connects to a running FileMonitor.exe instance and prints all file system events.
//
// Prerequisites:
//   1. Run FileMonitor.exe as Administrator.
//   2. dotnet run   (from this directory)
//
// Controls:
//   Ctrl+C  — disconnect and exit
//   S       — stop monitoring (pause the driver)
//   R       — resume monitoring

using FileMonitor.Client;
using FileMonitor.Grpc;

const string ServiceAddress = "http://localhost:50051";

Console.WriteLine($"Connecting to FileMonitor at {ServiceAddress} ...");

using var client = new FileMonitorClient(ServiceAddress);

// ── Print current status ────────────────────────────────────────────────
try
{
    var status = await client.GetStatusAsync();
    Console.WriteLine($"Driver connected : {status.IsDriverConnected}");
    Console.WriteLine($"Monitoring active: {status.IsMonitoring}");
    Console.WriteLine($"Events processed : {status.EventsProcessed}");
    Console.WriteLine();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Cannot reach server: {ex.Message}");
    Console.ResetColor();
    return;
}

// ── Subscribe to events ─────────────────────────────────────────────────
Console.WriteLine($"{"Time",-12} {"PID",6}  {"Process",-22} {"Event",-10}  Path");
Console.WriteLine(new string('-', 90));

client.OnFileEvent += evt =>
{
    var ts       = DateTime.FromFileTimeUtc(evt.Timestamp).ToLocalTime();
    var time     = ts.ToString("HH:mm:ss.ff");
    var process  = string.IsNullOrEmpty(evt.ProcessName)
                   ? evt.ProcessId.ToString()
                   : evt.ProcessName;
    if (process.Length > 20) process = process[..19] + "…";

    var eventName = evt.EventType.ToString().Replace("FileEvent", "");

    var color = evt.EventType switch
    {
        FileEventType.FileEventCreate  => ConsoleColor.Green,
        FileEventType.FileEventWrite   => ConsoleColor.Yellow,
        FileEventType.FileEventDelete  => ConsoleColor.Red,
        FileEventType.FileEventRename  => ConsoleColor.Cyan,
        _                              => ConsoleColor.White,
    };

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write($"{time,-12} ");
    Console.ForegroundColor = ConsoleColor.White;
    Console.Write($"{evt.ProcessId,6}  ");
    Console.ForegroundColor = ConsoleColor.Gray;
    Console.Write($"{process,-22} ");
    Console.ForegroundColor = color;
    Console.Write($"{eventName,-10}  ");
    Console.ResetColor();
    Console.WriteLine(evt.FilePath);
};

client.OnDisconnected += ex =>
{
    Console.ForegroundColor = ex is null ? ConsoleColor.Yellow : ConsoleColor.Red;
    Console.WriteLine(ex is null ? "\nStream ended." : $"\nDisconnected: {ex.Message}");
    Console.ResetColor();
};

// Subscribe to all events (0 = all types, "" = all paths)
client.StartSubscription(eventFilter: 0, pathFilter: "");

// ── Keyboard controls ───────────────────────────────────────────────────
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine("Press S=stop  R=resume  Ctrl+C=quit");

while (!cts.IsCancellationRequested)
{
    if (Console.KeyAvailable)
    {
        switch (Console.ReadKey(intercept: true).Key)
        {
            case ConsoleKey.S:
                var stop = await client.StopMonitoringAsync();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n[Monitoring paused: {stop.Message}]");
                Console.ResetColor();
                break;
            case ConsoleKey.R:
                var start = await client.StartMonitoringAsync();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n[Monitoring resumed: {start.Message}]");
                Console.ResetColor();
                break;
        }
    }
    else
    {
        await Task.Delay(50, cts.Token).ContinueWith(_ => { });
    }
}

client.StopSubscription();
Console.WriteLine("Goodbye.");
