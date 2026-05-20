using Tracer.Core.Time;

namespace Tracer.Core.Time;

/// <summary>
/// A half-open time interval [StartUtc, EndUtc) on the cluster's synchronized wall-clock.
/// </summary>
public sealed record TimeRange(WallclockTime StartUtc, WallclockTime EndUtc);
