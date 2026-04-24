// Author: Rene Pally
//
// LiveFileMonitorGrpcService — gRPC service implementation hosted inside FileMonitor.exe.
// Bridges the in-process LiveEventBroadcaster to remote gRPC subscribers.

using FileMonitor.Grpc;
using Grpc.Core;

namespace FileMonitor.Live;

internal sealed class LiveFileMonitorGrpcService : FileMonitorService.FileMonitorServiceBase
{
    private readonly LiveEventBroadcaster _broadcaster;
    private readonly DriverManager _driver;

    public LiveFileMonitorGrpcService(LiveEventBroadcaster broadcaster, DriverManager driver)
    {
        _broadcaster = broadcaster;
        _driver = driver;
    }

    /// <summary>
    /// Subscribe to a stream of file events. The stream runs until the client cancels.
    /// </summary>
    public override async Task Subscribe(
        SubscribeRequest request,
        IServerStreamWriter<FileEvent> responseStream,
        ServerCallContext context)
    {
        var reader = _broadcaster.Subscribe();
        try
        {
            await foreach (var evt in reader.ReadAllAsync(context.CancellationToken))
            {
                // Apply event-type bitmask filter
                if (request.EventFilter != 0 &&
                    ((uint)evt.EventType & request.EventFilter) == 0)
                    continue;

                // Apply path prefix filter
                if (!string.IsNullOrEmpty(request.PathFilter) &&
                    !evt.FilePath.StartsWith(request.PathFilter,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                await responseStream.WriteAsync(evt, context.CancellationToken);
            }
        }
        finally
        {
            _broadcaster.Unsubscribe(reader);
        }
    }

    public override Task<MonitoringResponse> StartMonitoring(
        MonitoringRequest request, ServerCallContext context)
    {
        var ok = _driver.SendCommand(DriverManager.CommandType.StartMonitoring);
        return Task.FromResult(new MonitoringResponse
        {
            Success = ok,
            Message = ok ? "Monitoring started." : "Failed to send command to driver.",
        });
    }

    public override Task<MonitoringResponse> StopMonitoring(
        MonitoringRequest request, ServerCallContext context)
    {
        var ok = _driver.SendCommand(DriverManager.CommandType.StopMonitoring);
        return Task.FromResult(new MonitoringResponse
        {
            Success = ok,
            Message = ok ? "Monitoring stopped." : "Failed to send command to driver.",
        });
    }

    public override Task<StatusResponse> GetStatus(
        StatusRequest request, ServerCallContext context)
    {
        return Task.FromResult(new StatusResponse
        {
            IsMonitoring    = true,
            IsDriverConnected = _driver.IsConnected,
            EventsProcessed  = (ulong)_broadcaster.TotalPublished,
            ActiveSubscribers = (uint)_broadcaster.ActiveSubscriberCount,
        });
    }
}
