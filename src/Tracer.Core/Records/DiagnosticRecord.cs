using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Time;

namespace Tracer.Core.Records;

/// <summary>
/// Abstract base for all diagnostic records produced by Tracer.
/// </summary>
public abstract record DiagnosticRecord
{
    public required ulong SequenceNumber { get; init; }
    public required WallclockTime PublishWallclock { get; init; }
    public required WallclockTime ReceiveWallclock { get; init; }
    public required AgentId PublisherNode { get; init; }
    public required AgentId SubscriberNode { get; init; }
    public required TopicName Topic { get; init; }
}
