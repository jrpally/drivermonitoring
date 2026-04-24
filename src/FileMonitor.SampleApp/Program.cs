// Author: Rene Pally

using FileMonitor.Client;
using FileMonitor.Grpc;

Console.WriteLine("=== FileMonitor Sample Client ===");
Console.WriteLine("Connecting to FileMonitor service...");
Console.WriteLine();

using var client = new FileMonitorClient("http://localhost:50051");

// Print status
try
{
    var status = await client.GetStatusAsync();
    Console.WriteLine($"  Driver connected : {status.IsDriverConnected}");
    Console.WriteLine($"  Monitoring active: {status.IsMonitoring}");
    Console.WriteLine($"  Events processed : {status.EventsProcessed}");
    Console.WriteLine($"  Active subscribers: {status.ActiveSubscribers}");
    Console.WriteLine();
}
catch (Exception ex)
{
    Console.WriteLine($"  Could not get status: {ex.Message}");
    Console.WriteLine("  Make sure the FileMonitor service is running.");
    Console.WriteLine();
}

// Subscribe to all events
client.OnFileEvent += evt =>
{
    var color = evt.EventType switch
    {
        FileEventType.FileEventCreate => ConsoleColor.Green,
        FileEventType.FileEventWrite => ConsoleColor.Yellow,
        FileEventType.FileEventDelete => ConsoleColor.Red,
        FileEventType.FileEventRename => ConsoleColor.Cyan,
        _ => ConsoleColor.Gray,
    };

    Console.ForegroundColor = color;
    Console.WriteLine(
        $"[{DateTimeOffset.FromFileTime(evt.Timestamp):HH:mm:ss.fff}] " +
        $"{evt.EventType,-16} PID={evt.ProcessId,-6} " +
        $"{evt.ProcessName,-20} {evt.FilePath}");
    Console.ResetColor();
};

client.OnDisconnected += ex =>
{
    if (ex != null)
        Console.WriteLine($"Disconnected with error: {ex.Message}");
    else
        Console.WriteLine("Subscription ended.");
};

client.StartSubscription();
Console.WriteLine("Listening for file events. Press a key to select an action:");
Console.WriteLine("  [S] Stop monitoring");
Console.WriteLine("  [R] Resume monitoring");
Console.WriteLine("  [Q] Quit");
Console.WriteLine();

while (true)
{
    var key = Console.ReadKey(intercept: true);

    switch (char.ToUpperInvariant(key.KeyChar))
    {
        case 'S':
            Console.WriteLine("Stopping monitoring...");
            var stopResp = await client.StopMonitoringAsync();
            Console.WriteLine($"  -> {stopResp.Message}");
            break;

        case 'R':
            Console.WriteLine("Resuming monitoring...");
            var startResp = await client.StartMonitoringAsync();
            Console.WriteLine($"  -> {startResp.Message}");
            break;

        case 'Q':
            Console.WriteLine("Shutting down...");
            client.StopSubscription();
            return;
    }
}
