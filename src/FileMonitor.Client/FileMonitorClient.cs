// Author: Rene Pally

using FileMonitor.Grpc;
using Grpc.Core;
using Grpc.Net.Client;

namespace FileMonitor.Client;

/// <summary>
/// Client library for connecting to the FileMonitor Windows service.
/// Provides methods to subscribe to file events and control monitoring.
/// </summary>
public sealed class FileMonitorClient : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly FileMonitorService.FileMonitorServiceClient _client;
    private CancellationTokenSource? _subscriptionCts;

    /// <summary>
    /// Fired for each file event received from the service.
    /// </summary>
    public event Action<FileEvent>? OnFileEvent;

    /// <summary>
    /// Fired when the subscription stream ends or errors.
    /// </summary>
    public event Action<Exception?>? OnDisconnected;

    /// <summary>
    /// Create a new client connected to the FileMonitor service.
    /// </summary>
    /// <param name="serviceAddress">gRPC endpoint, e.g. "http://localhost:50051"</param>
    public FileMonitorClient(string serviceAddress = "http://localhost:50051")
    {
        _channel = GrpcChannel.ForAddress(serviceAddress);
        _client = new FileMonitorService.FileMonitorServiceClient(_channel);
    }

    /// <summary>
    /// Start receiving file events. Events are delivered via the OnFileEvent event.
    /// </summary>
    /// <param name="eventFilter">Bitmask of FileEventType values to receive (0 = all).</param>
    /// <param name="pathFilter">Optional path prefix filter.</param>
    public void StartSubscription(uint eventFilter = 0, string pathFilter = "")
    {
        StopSubscription();

        _subscriptionCts = new CancellationTokenSource();
        var ct = _subscriptionCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var request = new SubscribeRequest
                {
                    EventFilter = eventFilter,
                    PathFilter = pathFilter,
                };

                using var call = _client.Subscribe(request, cancellationToken: ct);

                await foreach (var evt in call.ResponseStream.ReadAllAsync(ct))
                {
                    OnFileEvent?.Invoke(evt);
                }

                OnDisconnected?.Invoke(null);
            }
            catch (OperationCanceledException)
            {
                OnDisconnected?.Invoke(null);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                OnDisconnected?.Invoke(null);
            }
            catch (Exception ex)
            {
                OnDisconnected?.Invoke(ex);
            }
        }, ct);
    }

    /// <summary>
    /// Stop the current event subscription.
    /// </summary>
    public void StopSubscription()
    {
        _subscriptionCts?.Cancel();
        _subscriptionCts?.Dispose();
        _subscriptionCts = null;
    }

    /// <summary>
    /// Tell the service to start monitoring (resumes the driver).
    /// </summary>
    public async Task<MonitoringResponse> StartMonitoringAsync(
        CancellationToken ct = default)
    {
        return await _client.StartMonitoringAsync(new MonitoringRequest(), cancellationToken: ct);
    }

    /// <summary>
    /// Tell the service to stop monitoring (pauses the driver).
    /// </summary>
    public async Task<MonitoringResponse> StopMonitoringAsync(
        CancellationToken ct = default)
    {
        return await _client.StopMonitoringAsync(new MonitoringRequest(), cancellationToken: ct);
    }

    /// <summary>
    /// Get the current status of the monitoring service.
    /// </summary>
    public async Task<StatusResponse> GetStatusAsync(CancellationToken ct = default)
    {
        return await _client.GetStatusAsync(new StatusRequest(), cancellationToken: ct);
    }

    public void Dispose()
    {
        StopSubscription();
        _channel.Dispose();
    }
}
