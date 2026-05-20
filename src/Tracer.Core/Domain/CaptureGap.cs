using Tracer.Core.Time;

namespace Tracer.Core.Domain;

/// <summary>
/// Records a window of time during which capture was interrupted or degraded.
/// </summary>
public sealed record CaptureGap
{
    public required WallclockTime StartUtc { get; init; }
    public required WallclockTime EndUtc { get; init; }
    public required CaptureGapReason Reason { get; init; }
    public required long DroppedRecordCount { get; init; }
    public string? Detail { get; init; }
}

/// <summary>
/// Reason why a capture gap occurred.
/// </summary>
public enum CaptureGapReason
{
    BackpressureFastStateDropped,
    BackpressureSlowStateDropped,
    BackpressureEventsDropped,
    UnrecoveredCrashGap,
    TransportDisconnected
}
