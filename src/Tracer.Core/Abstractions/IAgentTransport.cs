using Tracer.Core.Records;
using Tracer.Core.Time;

namespace Tracer.Core.Abstractions;

/// <summary>
/// Transport carrying records from data producer to TracerAgent.
/// Production: shared memory ring (Phase 11). Development: in-process channel.
/// </summary>
public interface IAgentTransport : IAsyncDisposable
{
    /// <summary>
    /// Read records as they arrive. Completes when the transport is closed.
    /// </summary>
    IAsyncEnumerable<DiagnosticRecord> ReadAsync(CancellationToken ct);

    /// <summary>
    /// Snapshot of current transport health for diagnostics.
    /// </summary>
    TransportHealth GetHealth();
}

/// <summary>
/// Snapshot of current transport health.
/// </summary>
public sealed record TransportHealth
{
    public required int PendingCount { get; init; }
    public required int Capacity { get; init; }
    public required long TotalReceived { get; init; }
    public required long TotalDropped { get; init; }
    public required WallclockTime LastReceivedAt { get; init; }
}
