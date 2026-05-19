using Tracer.Core.Domain;
using Tracer.Core.Identity;

namespace Tracer.Core.Records;

/// <summary>
/// A discrete diagnostic event emitted by a node.
/// </summary>
public sealed record EventRecord : DiagnosticRecord
{
    public required EventId EventId { get; init; }
    public required TraceId TraceId { get; init; }
    public EventId? ParentEventId { get; init; }
    public EntityId? EntityId { get; init; }
    public string? OwningPlayerId { get; init; }
    public string? ScenarioPhase { get; init; }
    public Severity? Severity { get; init; }
    public string? NotableLabel { get; init; }
    public required string PayloadJson { get; init; }
}
