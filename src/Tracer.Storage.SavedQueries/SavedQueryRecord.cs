namespace Tracer.Storage.SavedQueries;

public sealed record SavedQueryRecord
{
    public required string SavedQueryId { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
    public required string Sql { get; init; }
    public IReadOnlyList<SavedQueryParameter> Parameters { get; init; } = Array.Empty<SavedQueryParameter>();
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public required bool IsBuiltIn { get; init; }
    public required bool IsFavorite { get; init; }
    public string? Author { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? LastRunAtUtc { get; init; }
    public required int RunCount { get; init; }
}

public sealed record SavedQueryParameter
{
    public required string Name { get; init; }
    public required string DuckType { get; init; }
    public required string DefaultValueText { get; init; }
    public string? Description { get; init; }
}

public sealed record SavedQueryFilter
{
    public bool? IsBuiltIn { get; init; }
    public bool? IsFavorite { get; init; }
    public string? Tag { get; init; }
    public string? Author { get; init; }
}
