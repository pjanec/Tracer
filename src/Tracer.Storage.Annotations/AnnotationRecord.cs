namespace Tracer.Storage.Annotations;

public sealed record AnnotationRecord
{
    public required string AnnotationId { get; init; }
    public required string SessionId { get; init; }
    public required AnnotationKind Kind { get; init; }

    public string? EventId { get; init; }
    public string? EntityId { get; init; }
    public string? TraceId { get; init; }
    public DateTimeOffset? TargetWallclock { get; init; }

    public required string Body { get; init; }
    public string? Title { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public string? Author { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? ModifiedAtUtc { get; init; }
}
