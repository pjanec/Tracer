namespace Tracer.WebApi.Contracts.Dto;

public sealed record AnnotationDto
{
    public required string AnnotationId { get; init; }
    public required string SessionId { get; init; }
    public required string Kind { get; init; }
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

public sealed record CreateAnnotationDto
{
    public required string SessionId { get; init; }
    public required string Kind { get; init; }
    public string? EventId { get; init; }
    public string? EntityId { get; init; }
    public string? TraceId { get; init; }
    public DateTimeOffset? TargetWallclock { get; init; }
    public required string Body { get; init; }
    public string? Title { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public string? Author { get; init; }
}

public sealed record UpdateAnnotationDto
{
    public string? Body { get; init; }
    public string? Title { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public string? Author { get; init; }
}
