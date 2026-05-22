using Tracer.Storage.Annotations;

namespace Tracer.WebApi.Contracts.Dto;

public static class AnnotationDtoMapper
{
    public static AnnotationDto Map(AnnotationRecord r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new()
        {
            AnnotationId    = r.AnnotationId,
            SessionId       = r.SessionId,
            Kind            = r.Kind.ToString(),
            EventId         = r.EventId,
            EntityId        = r.EntityId,
            TraceId         = r.TraceId,
            TargetWallclock = r.TargetWallclock,
            Body            = r.Body,
            Title           = r.Title,
            Tags            = r.Tags,
            Author          = r.Author,
            CreatedAtUtc    = r.CreatedAtUtc,
            ModifiedAtUtc   = r.ModifiedAtUtc,
        };
    }

    public static AnnotationRecord FromCreate(CreateAnnotationDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new()
        {
            AnnotationId    = "",
            SessionId       = dto.SessionId,
            Kind            = Enum.Parse<AnnotationKind>(dto.Kind, ignoreCase: true),
            EventId         = dto.EventId,
            EntityId        = dto.EntityId,
            TraceId         = dto.TraceId,
            TargetWallclock = dto.TargetWallclock,
            Body            = dto.Body,
            Title           = dto.Title,
            Tags            = dto.Tags,
            Author          = dto.Author,
            CreatedAtUtc    = default,
        };
    }
}

