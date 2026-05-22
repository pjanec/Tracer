using Tracer.Storage.SavedQueries;

namespace Tracer.WebApi.Contracts.Dto;

public sealed record SavedQueryDto
{
    public required string SavedQueryId { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
    public required string Sql { get; init; }
    public required IReadOnlyList<SavedQueryParameterDto> Parameters { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required bool IsBuiltIn { get; init; }
    public required bool IsFavorite { get; init; }
    public string? Author { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? LastRunAtUtc { get; init; }
    public required int RunCount { get; init; }
}

public sealed record SavedQueryParameterDto
{
    public required string Name { get; init; }
    public required string DuckType { get; init; }
    public required string DefaultValueText { get; init; }
    public string? Description { get; init; }
}

public sealed record CreateSavedQueryDto
{
    public required string Label { get; init; }
    public string? Description { get; init; }
    public required string Sql { get; init; }
    public IReadOnlyList<SavedQueryParameterDto>? Parameters { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public string? Author { get; init; }
}

public sealed record UpdateSavedQueryDto
{
    public string? Label { get; init; }
    public string? Description { get; init; }
    public string? Sql { get; init; }
    public IReadOnlyList<SavedQueryParameterDto>? Parameters { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public bool? IsFavorite { get; init; }
}

public sealed record CloneSavedQueryDto
{
    public string? Author { get; init; }
}

public static class SavedQueryDtoMapper
{
    public static SavedQueryDto Map(SavedQueryRecord r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new()
    {
        SavedQueryId = r.SavedQueryId,
        Label        = r.Label,
        Description  = r.Description,
        Sql          = r.Sql,
        Parameters   = r.Parameters.Select(MapParam).ToList(),
        Tags         = r.Tags.ToList(),
        IsBuiltIn    = r.IsBuiltIn,
        IsFavorite   = r.IsFavorite,
        Author       = r.Author,
        CreatedAtUtc = r.CreatedAtUtc,
        LastRunAtUtc = r.LastRunAtUtc,
        RunCount     = r.RunCount,
        };
    }

    public static SavedQueryParameterDto MapParam(SavedQueryParameter p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new()
    {
        Name             = p.Name,
        DuckType         = p.DuckType,
        DefaultValueText = p.DefaultValueText,
        Description      = p.Description,
        };
    }

    public static SavedQueryParameter FromParamDto(SavedQueryParameterDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new()
    {
        Name             = dto.Name,
        DuckType         = dto.DuckType,
        DefaultValueText = dto.DefaultValueText,
        Description      = dto.Description,
        };
    }

    public static SavedQueryRecord FromCreate(CreateSavedQueryDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new()
    {
        SavedQueryId = "",
        Label        = dto.Label,
        Description  = dto.Description,
        Sql          = dto.Sql,
        Parameters   = (IReadOnlyList<SavedQueryParameter>?)dto.Parameters?.Select(FromParamDto).ToList()
                       ?? Array.Empty<SavedQueryParameter>(),
        Tags         = (IReadOnlyList<string>?)dto.Tags?.ToList() ?? Array.Empty<string>(),
        IsBuiltIn    = false,
        IsFavorite   = false,
        Author       = dto.Author,
        CreatedAtUtc = default,
        RunCount     = 0,
        };
    }
}
