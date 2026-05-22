using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tracer.Storage.Annotations;
using Tracer.WebApi.Contracts.Dto;

namespace Tracer.WebApi.Endpoints;

public static class AnnotationEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/annotations", HandleListAsync).WithOpenApi();
        app.MapPost("/api/annotations", HandleCreateAsync).WithOpenApi();
        app.MapGet("/api/annotations/{annotationId}", HandleGetAsync).WithOpenApi();
        app.MapPut("/api/annotations/{annotationId}", HandleUpdateAsync).WithOpenApi();
        app.MapDelete("/api/annotations/{annotationId}", HandleDeleteAsync).WithOpenApi();
    }

    public static async Task<Results<Ok<IReadOnlyList<AnnotationDto>>, ProblemHttpResult>> HandleListAsync(
        [FromQuery] string? sessionId,
        [FromQuery] string? kind,
        [FromQuery] string? eventId,
        [FromQuery] string? entityId,
        [FromQuery] string? traceId,
        [FromServices] IAnnotationStore store,
        CancellationToken ct,
        [FromQuery] int limit = 500)
    {
        ArgumentNullException.ThrowIfNull(store);
        try
        {
            var clampedLimit = Math.Clamp(limit, 1, 5000);
            AnnotationKind? parsedKind = null;
            if (kind is not null && Enum.TryParse<AnnotationKind>(kind, ignoreCase: true, out var k))
                parsedKind = k;

            var filter = new AnnotationFilter
            {
                SessionId = sessionId,
                Kind      = parsedKind,
                EventId   = eventId,
                EntityId  = entityId,
                TraceId   = traceId,
                Limit     = clampedLimit,
            };

            var results = await store.ListAsync(filter, ct);
            return TypedResults.Ok((IReadOnlyList<AnnotationDto>)results.Select(AnnotationDtoMapper.Map).ToList());
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title  = "Operation not allowed",
                Detail = ex.Message,
                Status = 405,
            });
        }
    }

    public static async Task<Results<Created<AnnotationDto>, BadRequest<ProblemDetails>, ProblemHttpResult>> HandleCreateAsync(
        [FromBody] CreateAnnotationDto? dto,
        [FromServices] IAnnotationStore store,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (dto is null)
            return TypedResults.BadRequest(new ProblemDetails { Title = "Request body is required", Status = 400 });

        var validation = ValidateCreate(dto);
        if (validation is not null)
            return TypedResults.BadRequest(validation);

        try
        {
            var record = AnnotationDtoMapper.FromCreate(dto);
            var created = await store.CreateAsync(record, ct);
            var result = AnnotationDtoMapper.Map(created);
            return TypedResults.Created($"/api/annotations/{created.AnnotationId}", result);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title  = "Operation not allowed",
                Detail = ex.Message,
                Status = 405,
            });
        }
    }

    public static async Task<Results<Ok<AnnotationDto>, NotFound, ProblemHttpResult>> HandleGetAsync(
        string annotationId,
        [FromServices] IAnnotationStore store,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        try
        {
            var record = await store.GetAsync(annotationId, ct);
            return record is null
                ? TypedResults.NotFound()
                : TypedResults.Ok(AnnotationDtoMapper.Map(record));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title  = "Operation not allowed",
                Detail = ex.Message,
                Status = 405,
            });
        }
    }

    public static async Task<Results<Ok<AnnotationDto>, NotFound, ProblemHttpResult>> HandleUpdateAsync(
        string annotationId,
        [FromBody] UpdateAnnotationDto? dto,
        [FromServices] IAnnotationStore store,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        try
        {
            var existing = await store.GetAsync(annotationId, ct);
            if (existing is null)
                return TypedResults.NotFound();

            var updated = existing with
            {
                Body   = dto?.Body   ?? existing.Body,
                Title  = dto?.Title  ?? existing.Title,
                Tags   = dto?.Tags   ?? existing.Tags,
                Author = dto?.Author ?? existing.Author,
            };
            var result = await store.UpdateAsync(updated, ct);
            return result is null
                ? TypedResults.NotFound()
                : TypedResults.Ok(AnnotationDtoMapper.Map(result));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title  = "Operation not allowed",
                Detail = ex.Message,
                Status = 405,
            });
        }
    }

    public static async Task<Results<NoContent, NotFound, ProblemHttpResult>> HandleDeleteAsync(
        string annotationId,
        [FromServices] IAnnotationStore store,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        try
        {
            var deleted = await store.DeleteAsync(annotationId, ct);
            return deleted ? TypedResults.NoContent() : TypedResults.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title  = "Operation not allowed",
                Detail = ex.Message,
                Status = 405,
            });
        }
    }

    internal static ProblemDetails? ValidateCreate(CreateAnnotationDto dto)
    {
        if (dto.SessionId is null)
            return new ProblemDetails { Title = "SessionId is required", Status = 400 };

        var targetCount = new[] { dto.EventId, dto.EntityId, dto.TraceId }
            .Count(x => x is not null);

        if (targetCount != 1)
            return new ProblemDetails
            {
                Title  = "Exactly one target identifier (eventId, entityId, or traceId) is required",
                Status = 400,
            };

        return null;
    }
}
