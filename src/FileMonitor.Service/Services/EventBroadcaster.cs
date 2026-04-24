// Author: Rene Pally

using System.Threading.Channels;
using FileMonitor.Grpc;
using Microsoft.Extensions.Logging;

namespace FileMonitor.Service.Services;

/// <summary>
/// Broadcasts file events to all subscribed gRPC clients.
/// Each subscriber gets its own bounded channel to prevent slow clients
/// from blocking the driver listener.
/// </summary>
public sealed class EventBroadcaster
{
    private readonly ILogger<EventBroadcaster> _logger;
    private readonly object _lock = new();
    private readonly List<Subscriber> _subscribers = [];
    private long _totalEventsProcessed;

    public long TotalEventsProcessed => Interlocked.Read(ref _totalEventsProcessed);
    public int ActiveSubscriberCount { get { lock (_lock) { return _subscribers.Count; } } }

    public EventBroadcaster(ILogger<EventBroadcaster> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Register a new subscriber. Returns a channel reader the caller can
    /// consume to receive events.
    /// </summary>
    public ChannelReader<FileEvent> AddSubscriber(uint eventFilter, string pathFilter)
    {
        var channel = Channel.CreateBounded<FileEvent>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
            SingleReader = true,
        });

        var subscriber = new Subscriber(channel, eventFilter, pathFilter);

        lock (_lock)
        {
            _subscribers.Add(subscriber);
        }

        _logger.LogInformation("Subscriber added. Total: {Count}", ActiveSubscriberCount);
        return channel.Reader;
    }

    /// <summary>
    /// Remove a subscriber (when the client disconnects).
    /// </summary>
    public void RemoveSubscriber(ChannelReader<FileEvent> reader)
    {
        lock (_lock)
        {
            var idx = _subscribers.FindIndex(s => s.Channel.Reader == reader);
            if (idx >= 0)
            {
                _subscribers[idx].Channel.Writer.TryComplete();
                _subscribers.RemoveAt(idx);
            }
        }

        _logger.LogInformation("Subscriber removed. Total: {Count}", ActiveSubscriberCount);
    }

    /// <summary>
    /// Broadcast a file event to all matching subscribers.
    /// </summary>
    public void Broadcast(FileEvent fileEvent)
    {
        Interlocked.Increment(ref _totalEventsProcessed);

        lock (_lock)
        {
            foreach (var sub in _subscribers)
            {
                if (!sub.Matches(fileEvent)) continue;

                // Non-blocking write; drops oldest if full
                sub.Channel.Writer.TryWrite(fileEvent);
            }
        }
    }

    private sealed class Subscriber
    {
        public Channel<FileEvent> Channel { get; }
        private readonly uint _eventFilter;
        private readonly string _pathFilter;

        public Subscriber(Channel<FileEvent> channel, uint eventFilter, string pathFilter)
        {
            Channel = channel;
            _eventFilter = eventFilter;
            _pathFilter = pathFilter ?? string.Empty;
        }

        public bool Matches(FileEvent evt)
        {
            // Event type filter (0 = accept all)
            if (_eventFilter != 0 && ((uint)evt.EventType & _eventFilter) == 0)
                return false;

            // Path prefix filter
            if (_pathFilter.Length > 0 &&
                !evt.FilePath.StartsWith(_pathFilter, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }
    }
}
