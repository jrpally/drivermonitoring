// Author: Rene Pally
//
// LiveEventBroadcaster — in-process fan-out of driver events to gRPC subscribers.
// Each subscriber gets its own bounded channel so a slow client cannot block others.

using System.Threading.Channels;
using FileMonitor.Grpc;

namespace FileMonitor.Live;

internal sealed class LiveEventBroadcaster
{
    private readonly object _lock = new();
    private readonly List<Channel<FileEvent>> _subscribers = [];
    private long _totalPublished;

    public long TotalPublished => Interlocked.Read(ref _totalPublished);
    public int ActiveSubscriberCount { get { lock (_lock) return _subscribers.Count; } }

    /// <summary>
    /// Register a new subscriber. Returns a ChannelReader the gRPC service reads from.
    /// </summary>
    public ChannelReader<FileEvent> Subscribe()
    {
        var ch = Channel.CreateBounded<FileEvent>(new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
            SingleReader = true,
        });

        lock (_lock)
            _subscribers.Add(ch);

        return ch.Reader;
    }

    /// <summary>
    /// Remove a subscriber (called when the gRPC stream ends).
    /// </summary>
    public void Unsubscribe(ChannelReader<FileEvent> reader)
    {
        lock (_lock)
            _subscribers.RemoveAll(ch => ch.Reader == reader);
    }

    /// <summary>
    /// Publish an event to all current subscribers.
    /// </summary>
    public void Publish(FileEvent evt)
    {
        Interlocked.Increment(ref _totalPublished);
        lock (_lock)
        {
            foreach (var ch in _subscribers)
                ch.Writer.TryWrite(evt);
        }
    }

    /// <summary>
    /// Signal all subscribers that no more events will be sent (called on shutdown).
    /// </summary>
    public void Complete()
    {
        lock (_lock)
        {
            foreach (var ch in _subscribers)
                ch.Writer.TryComplete();
        }
    }
}
