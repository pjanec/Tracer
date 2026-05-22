namespace Tracer.Storage.Annotations;

public sealed record AnnotationFilter
{
    public string? SessionId { get; init; }
    public AnnotationKind? Kind { get; init; }
    public string? EventId { get; init; }
    public string? EntityId { get; init; }
    public string? TraceId { get; init; }
    public DateTimeOffset? FromUtc { get; init; }
    public DateTimeOffset? ToUtc { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public int Limit { get; init; } = 500;
}
