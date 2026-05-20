using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Tracer.Core.Records;

namespace Tracer.WebApi.Streaming;

/// <summary>
/// A single SSE client connection. Holds a bounded channel for event delivery.
/// Events that don't match the filter are discarded in <see cref="Enqueue"/>.
/// When the channel is full, the oldest event is dropped and <see cref="DropCount"/> is incremented.
/// </summary>
public sealed class SseConnection : IAsyncDisposable
{
    private readonly Channel<EventRecord> _channel;
    private readonly int _bufferSize;
    private int _dropCount;

    public Guid Id { get; } = Guid.NewGuid();
    public SseFilter Filter { get; }
    public int DropCount => _dropCount;

    public SseConnection(SseFilter filter, int bufferSize)
    {
        Filter = filter;
        _bufferSize = bufferSize;
        _channel = Channel.CreateBounded<EventRecord>(new BoundedChannelOptions(bufferSize)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true,
        });
    }

    /// <summary>Applies the filter and enqueues the event if it matches.</summary>
    public void Enqueue(EventRecord ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        if (!Filter.Matches(ev)) return;

        if (_channel.Reader.Count >= _bufferSize)
            Interlocked.Increment(ref _dropCount);
        _channel.Writer.TryWrite(ev);
    }

    /// <summary>Returns an async stream of events until cancellation or the channel is completed.</summary>
    public async IAsyncEnumerable<EventRecord> ReadAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var ev in _channel.Reader.ReadAllAsync(ct))
            yield return ev;
    }

    /// <summary>Signals no more events will be written.</summary>
    public void Complete() => _channel.Writer.TryComplete();

    public ValueTask DisposeAsync()
    {
        Complete();
        return ValueTask.CompletedTask;
    }
}
