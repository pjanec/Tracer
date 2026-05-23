using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Tracer.Adapters.SharedMemory.Configuration;
using Tracer.Core.Abstractions;
using Tracer.Core.Records;
using Tracer.Core.Time;

namespace Tracer.Adapters.SharedMemory;

/// <summary>
/// Production <see cref="IAgentTransport"/> implemented with a named shared memory ring buffer.
/// Consumer side: the TracerAgent calls <see cref="ReadAsync"/> to receive records from the
/// simulation process (which writes via <see cref="SharedMemoryWriter"/>).
/// </summary>
public sealed class SharedMemoryTransport : IAgentTransport
{
    private readonly SharedMemoryConfig _config;
    private readonly ILogger<SharedMemoryTransport> _logger;

    private long _totalReceived;
    private long _totalDropped;
    private WallclockTime _lastReceivedAt;

    public SharedMemoryTransport(SharedMemoryConfig config, ILogger<SharedMemoryTransport> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<DiagnosticRecord> ReadAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new SharedMemoryReader(_config.SharedMemoryName, _config.SemaphoreName);

        while (!ct.IsCancellationRequested)
        {
            // WaitAndRead blocks (up to 100 ms) on a background thread, then drains.
            List<DiagnosticRecord> batch;
            try
            {
                batch = await Task.Run(
                    () => reader.WaitAndRead(TimeSpan.FromMilliseconds(100)).ToList(),
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            foreach (var record in batch)
            {
                Interlocked.Increment(ref _totalReceived);
                _lastReceivedAt = WallclockTime.FromDateTimeOffset(DateTimeOffset.UtcNow);
                yield return record;
            }
            // Update drop count after processing each batch
            Interlocked.Exchange(ref _totalDropped, reader.GetDroppedCount());
        }
    }

    /// <inheritdoc/>
    public TransportHealth GetHealth() => new()
    {
        PendingCount = 0,
        Capacity = (int)Math.Min(_config.CapacityBytes, int.MaxValue),
        TotalReceived = Interlocked.Read(ref _totalReceived),
        TotalDropped = Interlocked.Read(ref _totalDropped),
        LastReceivedAt = _lastReceivedAt,
    };

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
