using Tracer.Core.Domain;

namespace Tracer.Core.Domain;

/// <summary>
/// Describes a completed, uploaded interval as seen from the NAS/storage reader.
/// </summary>
public sealed record IntervalDescriptor(
    IntervalTimestamp Timestamp,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc);
