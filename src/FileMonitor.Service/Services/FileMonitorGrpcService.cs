// Author: Rene Pally

using FileMonitor.Grpc;
using FileMonitor.Service.Driver;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace FileMonitor.Service.Services;

/// <summary>
/// gRPC service implementation. Clients call these methods to subscribe
/// to events and control monitoring.
/// </summary>
public sealed class FileMonitorGrpcService : FileMonitorService.FileMonitorServiceBase
{
    private readonly DriverCommunication _driver;
    private readonly EventBroadcaster _broadcaster;
    private readonly ILogger<FileMonitorGrpcService> _logger;

    public FileMonitorGrpcService(
        DriverCommunication driver,
        EventBroadcaster broadcaster,
        ILogger<FileMonitorGrpcService> logger)
    {
        _driver = driver;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    /// <summary>
    /// Server-streaming RPC: client subscribes and receives a stream of events.
    /// </summary>
    public override async Task Subscribe(
        SubscribeRequest request,
        IServerStreamWriter<FileEvent> responseStream,
        ServerCallContext context)
    {
        _logger.LogInformation(
            "Client subscribed. EventFilter={Filter}, PathFilter={Path}",
            request.EventFilter, request.PathFilter);

        var reader = _broadcaster.AddSubscriber(request.EventFilter, request.PathFilter);

        try
        {
            await foreach (var evt in reader.ReadAllAsync(context.CancellationToken))
            {
                await responseStream.WriteAsync(evt, context.CancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
        finally
        {
            _broadcaster.RemoveSubscriber(reader);
            _logger.LogInformation("Client unsubscribed.");
        }
    }

    public override Task<MonitoringResponse> StartMonitoring(
        MonitoringRequest request, ServerCallContext context)
    {
        _logger.LogInformation("StartMonitoring requested.");

        bool ok = _driver.SendCommand(DriverProtocol.CommandType.StartMonitoring);

        return Task.FromResult(new MonitoringResponse
        {
            Success = ok,
            Message = ok ? "Monitoring started." : "Failed to start monitoring."
        });
    }

    public override Task<MonitoringResponse> StopMonitoring(
        MonitoringRequest request, ServerCallContext context)
    {
        _logger.LogInformation("StopMonitoring requested.");

        bool ok = _driver.SendCommand(DriverProtocol.CommandType.StopMonitoring);

        return Task.FromResult(new MonitoringResponse
        {
            Success = ok,
            Message = ok ? "Monitoring stopped." : "Failed to stop monitoring."
        });
    }

    public override Task<StatusResponse> GetStatus(
        StatusRequest request, ServerCallContext context)
    {
        return Task.FromResult(new StatusResponse
        {
            IsMonitoring = true, // Could track this in DriverCommunication
            IsDriverConnected = _driver.IsConnected,
            EventsProcessed = (ulong)_broadcaster.TotalEventsProcessed,
            ActiveSubscribers = (uint)_broadcaster.ActiveSubscriberCount,
        });
    }
}
