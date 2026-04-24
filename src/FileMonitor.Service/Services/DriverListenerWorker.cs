// Author: Rene Pally

using FileMonitor.Grpc;
using FileMonitor.Service.Driver;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FileMonitor.Service.Services;

/// <summary>
/// Background worker that listens for notifications from the minifilter driver
/// and broadcasts them to all subscribed gRPC clients.
/// </summary>
public sealed class DriverListenerWorker : BackgroundService
{
    private readonly DriverCommunication _driver;
    private readonly EventBroadcaster _broadcaster;
    private readonly ILogger<DriverListenerWorker> _logger;

    public DriverListenerWorker(
        DriverCommunication driver,
        EventBroadcaster broadcaster,
        ILogger<DriverListenerWorker> logger)
    {
        _driver = driver;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Driver listener worker starting.");

        // Retry connection loop
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_driver.IsConnected)
            {
                _logger.LogInformation("Attempting to connect to driver...");
                if (!_driver.Connect())
                {
                    _logger.LogWarning("Driver not available. Retrying in 5 seconds...");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }
            }

            // Listen for notifications on a thread pool thread
            // (FilterGetMessage is a blocking call)
            await Task.Run(() => ListenLoop(stoppingToken), stoppingToken);
        }

        _driver.Disconnect();
        _logger.LogInformation("Driver listener worker stopped.");
    }

    private void ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var notification = _driver.GetNextNotification();
            if (notification == null)
            {
                // Connection lost or shutting down
                if (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning("Lost connection to driver.");
                    _driver.Disconnect();
                }
                break;
            }

            var n = notification.Value;

            var fileEvent = new FileEvent
            {
                EventType = (FileEventType)n.EventType,
                FilePath = n.FilePath ?? string.Empty,
                ProcessId = n.ProcessId,
                ThreadId = n.ThreadId,
                Timestamp = n.Timestamp,
                ProcessName = DriverCommunication.ResolveProcessName(n.ProcessId),
            };

            _broadcaster.Broadcast(fileEvent);
        }
    }
}
