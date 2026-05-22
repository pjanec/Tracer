namespace Tracer.Storage.SavedViews;

public sealed record SavedViewRecord
{
    public required string SavedViewId { get; init; }
    public required string SessionId { get; init; }
    public required SavedViewKind Kind { get; init; }
    public required string ViewType { get; init; }
    public required string Url { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
    public required string Persona { get; init; }
    public string? Author { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? LastOpenedAtUtc { get; init; }
    public required int OpenCount { get; init; }
}
