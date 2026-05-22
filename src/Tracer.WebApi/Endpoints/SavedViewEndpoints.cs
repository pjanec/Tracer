using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tracer.Storage.SavedViews;
using Tracer.WebApi.Contracts.Dto;

namespace Tracer.WebApi.Endpoints;

public static class SavedViewEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/saved-views", HandleListAsync).WithOpenApi();
        app.MapPost("/api/saved-views", HandleCreateAsync).WithOpenApi();
        app.MapGet("/api/saved-views/{id}", HandleGetAsync).WithOpenApi();
        app.MapPut("/api/saved-views/{id}", HandleUpdateAsync).WithOpenApi();
        app.MapDelete("/api/saved-views/{id}", HandleDeleteAsync).WithOpenApi();
        app.MapPost("/api/saved-views/{id}/opened", HandleRecordOpenedAsync).WithOpenApi();
    }

    public static async Task<Results<Ok<IReadOnlyList<SavedViewDto>>, ProblemHttpResult>> HandleListAsync(
        [FromQuery] string? sessionId,
        [FromQuery] string? persona,
        [FromQuery] string? kind,
        [FromQuery] string? orderBy,
        [FromServices] ISavedViewStore store,
        CancellationToken ct,
        [FromQuery] int limit = 100)
    {
        ArgumentNullException.ThrowIfNull(store);
        try
        {
            var clampedLimit = Math.Clamp(limit, 1, 500);
            SavedViewKind? parsedKind = null;
            if (kind is not null && Enum.TryParse<SavedViewKind>(kind, ignoreCase: true, out var k))
                parsedKind = k;

            var filter = new SavedViewFilter
            {
                SessionId = sessionId,
                Persona   = persona,
                Kind      = parsedKind,
                OrderBy   = orderBy ?? "created",
                Limit     = clampedLimit,
            };
            var results = await store.ListAsync(filter, ct);
            return TypedResults.Ok((IReadOnlyList<SavedViewDto>)results.Select(SavedViewDtoMapper.Map).ToList());
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

    public static async Task<Results<Created<SavedViewDto>, BadRequest<ProblemDetails>, ProblemHttpResult>> HandleCreateAsync(
        [FromBody] CreateSavedViewDto? dto,
        [FromServices] ISavedViewStore store,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (dto is null)
            return TypedResults.BadRequest(new ProblemDetails { Title = "Request body is required", Status = 400 });

        if (string.IsNullOrWhiteSpace(dto.SessionId))
            return TypedResults.BadRequest(new ProblemDetails { Title = "SessionId is required", Status = 400 });

        try
        {
            var record = SavedViewDtoMapper.FromCreate(dto);
            var created = await store.CreateAsync(record, ct);
            var result = SavedViewDtoMapper.Map(created);
            return TypedResults.Created($"/api/saved-views/{created.SavedViewId}", result);
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

    public static async Task<Results<Ok<SavedViewDto>, NotFound, ProblemHttpResult>> HandleGetAsync(
        string id,
        [FromServices] ISavedViewStore store,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        try
        {
            var record = await store.GetAsync(id, ct);
            return record is null
                ? TypedResults.NotFound()
                : TypedResults.Ok(SavedViewDtoMapper.Map(record));
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

    public static async Task<Results<Ok<SavedViewDto>, NotFound, ProblemHttpResult>> HandleUpdateAsync(
        string id,
        [FromBody] UpdateSavedViewDto? dto,
        [FromServices] ISavedViewStore store,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        try
        {
            var existing = await store.GetAsync(id, ct);
            if (existing is null)
                return TypedResults.NotFound();

            var updated = existing with
            {
                Label       = dto?.Label       ?? existing.Label,
                Description = dto?.Description ?? existing.Description,
            };
            var result = await store.UpdateAsync(updated, ct);
            return result is null
                ? TypedResults.NotFound()
                : TypedResults.Ok(SavedViewDtoMapper.Map(result));
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
        string id,
        [FromServices] ISavedViewStore store,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        try
        {
            var deleted = await store.DeleteAsync(id, ct);
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

    public static async Task<NoContent> HandleRecordOpenedAsync(
        string id,
        [FromServices] ISavedViewStore store,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        try
        {
            await store.RecordOpenedAsync(id, ct);
        }
        catch
        {
            // fire-and-forget; always 204
        }
        return TypedResults.NoContent();
    }
}
