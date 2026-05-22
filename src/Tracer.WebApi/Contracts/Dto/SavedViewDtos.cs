using Tracer.Storage.SavedViews;

namespace Tracer.WebApi.Contracts.Dto;

public sealed record SavedViewDto
{
    public required string SavedViewId { get; init; }
    public required string SessionId { get; init; }
    public required string Kind { get; init; }
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

public sealed record CreateSavedViewDto
{
    public required string SessionId { get; init; }
    public required string Kind { get; init; }
    public required string ViewType { get; init; }
    public required string Url { get; init; }
    public required string Label { get; init; }
    public required string Persona { get; init; }
    public string? Description { get; init; }
    public string? Author { get; init; }
}

public sealed record UpdateSavedViewDto
{
    public string? Label { get; init; }
    public string? Description { get; init; }
}

public static class SavedViewDtoMapper
{
    public static SavedViewDto Map(SavedViewRecord r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new()
        {
            SavedViewId    = r.SavedViewId,
            SessionId      = r.SessionId,
            Kind           = r.Kind.ToString(),
            ViewType       = r.ViewType,
            Url            = r.Url,
            Label          = r.Label,
            Description    = r.Description,
            Persona        = r.Persona,
            Author         = r.Author,
            CreatedAtUtc   = r.CreatedAtUtc,
            LastOpenedAtUtc = r.LastOpenedAtUtc,
            OpenCount      = r.OpenCount,
        };
    }

    public static SavedViewRecord FromCreate(CreateSavedViewDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new()
        {
            SavedViewId  = "",
            SessionId    = dto.SessionId,
            Kind         = Enum.Parse<SavedViewKind>(dto.Kind, ignoreCase: true),
            ViewType     = dto.ViewType,
            Url          = dto.Url,
            Label        = dto.Label,
            Description  = dto.Description,
            Persona      = dto.Persona,
            Author       = dto.Author,
            CreatedAtUtc = default,
            OpenCount    = 0,
        };
    }
}
