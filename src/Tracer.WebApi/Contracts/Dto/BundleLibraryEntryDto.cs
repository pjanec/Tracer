using Tracer.WebApi.Queries;

namespace Tracer.WebApi.Contracts.Dto;

public sealed record BundleLibraryEntryDto
{
    public required string BundleId { get; init; }
    public required string SessionId { get; init; }
    public string? Label { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required bool IsArchived { get; init; }
    public required DateTimeOffset BuiltAtUtc { get; init; }
    public required long SizeBytes { get; init; }
    public DateTimeOffset? LastOpenedAtUtc { get; init; }
    public DateTimeOffset? SessionStartUtc { get; init; }
    public DateTimeOffset? SessionEndUtc { get; init; }
}

public sealed record UpdateBundleMetadataDto
{
    public string? Label { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public bool? Archived { get; init; }
}

public sealed record BundleLibraryListDto
{
    public required IReadOnlyList<BundleLibraryEntryDto> Entries { get; init; }
}

public static class BundleLibraryDtoMapper
{
    public static BundleLibraryEntryDto Map(BundleLibraryEntry e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return new()
    {
        BundleId        = e.BundleId,
        SessionId       = e.SessionId,
        Label           = e.Label,
        Description     = e.Description,
        Tags            = e.Tags,
        IsArchived      = e.IsArchived,
        BuiltAtUtc      = e.BuiltAtUtc,
        SizeBytes       = e.SizeBytes,
        LastOpenedAtUtc = e.LastOpenedAtUtc,
        SessionStartUtc = e.SessionStartUtc,
        SessionEndUtc   = e.SessionEndUtc,
        };
    }
}
