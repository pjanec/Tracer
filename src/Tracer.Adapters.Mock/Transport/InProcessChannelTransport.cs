using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Tracer.Core.Abstractions;
using Tracer.Core.Records;
using Tracer.Core.Time;

namespace Tracer.Adapters.Mock.Transport;

/// <summary>
/// In-process bounded channel transport for development and testing.
/// Drops the OLDEST record when the channel is full.
/// </summary>
public sealed class InProcessChannelTransport : IAgentTransport
{
    private readonly Channel<DiagnosticRecord> _channel;
    private readonly int _capacity;

    private long _totalReceived;
    private long _totalDropped;
    private WallclockTime _lastReceivedAt;

    public InProcessChannelTransport(int capacity)
    {
        _capacity = capacity;
        _channel = Channel.CreateBounded<DiagnosticRecord>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = false,
                SingleWriter = false,
            });
    }

    /// <summary>Enqueues a record. If the channel is full the oldest record is dropped.</summary>
    public Task WriteAsync(DiagnosticRecord record, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _totalReceived);
        _lastReceivedAt = WallclockTime.FromDateTimeOffset(DateTimeOffset.UtcNow);

        // Reader.Count reflects items currently in the buffer.
        // If it's at capacity, DropOldest will evict one entry.
        if (_channel.Reader.Count >= _capacity)
            Interlocked.Increment(ref _totalDropped);

        _channel.Writer.TryWrite(record);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<DiagnosticRecord> ReadAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var record in _channel.Reader.ReadAllAsync(ct))
            yield return record;
    }

    /// <inheritdoc />
    public TransportHealth GetHealth() => new()
    {
        PendingCount = _channel.Reader.Count,
        Capacity = _capacity,
        TotalReceived = Interlocked.Read(ref _totalReceived),
        TotalDropped = Interlocked.Read(ref _totalDropped),
        LastReceivedAt = _lastReceivedAt,
    };

    /// <summary>Marks the writer as complete, causing ReadAsync to finish.</summary>
    public void Complete() => _channel.Writer.TryComplete();

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Complete();
        return ValueTask.CompletedTask;
    }
}
