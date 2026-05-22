using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tracer.Storage.SavedQueries;
using Tracer.WebApi.Contracts.Dto;

namespace Tracer.WebApi.Endpoints;

public static class SavedQueriesEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet   ("/api/saved-queries",            HandleListAsync).WithOpenApi();
        app.MapGet   ("/api/saved-queries/{id}",       HandleGetAsync).WithOpenApi();
        app.MapPost  ("/api/saved-queries",            HandleCreateAsync).WithOpenApi();
        app.MapPut   ("/api/saved-queries/{id}",       HandleUpdateAsync).WithOpenApi();
        app.MapDelete("/api/saved-queries/{id}",       HandleDeleteAsync).WithOpenApi();
        app.MapPost  ("/api/saved-queries/{id}/favorite", HandleFavoriteAsync).WithOpenApi();
        app.MapPost  ("/api/saved-queries/{id}/clone", HandleCloneAsync).WithOpenApi();
        app.MapPost  ("/api/saved-queries/{id}/run",   HandleRunAsync).WithOpenApi();
    }

    public static async Task<Ok<IReadOnlyList<SavedQueryDto>>> HandleListAsync(
        [FromQuery] string? tag,
        [FromQuery] string? author,
        [FromQuery] bool? favorite,
        [FromQuery] bool? builtIn,
        [FromServices] ISavedQueryStore store,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        var filter = new SavedQueryFilter
        {
            Tag       = tag,
            Author    = author,
            IsFavorite = favorite,
            IsBuiltIn  = builtIn,
        };
        var records = await store.ListAsync(filter, ct);
        return TypedResults.Ok((IReadOnlyList<SavedQueryDto>)records.Select(SavedQueryDtoMapper.Map).ToList());
    }

    public static async Task<Results<Ok<SavedQueryDto>, NotFound>> HandleGetAsync(
        string id,
        [FromServices] ISavedQueryStore store,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        var record = await store.GetAsync(id, ct);
        return record is null ? TypedResults.NotFound() : TypedResults.Ok(SavedQueryDtoMapper.Map(record));
    }

    public static async Task<Results<Created<SavedQueryDto>, BadRequest<ProblemDetails>>> HandleCreateAsync(
        [FromBody] CreateSavedQueryDto? dto,
        [FromServices] ISavedQueryStore store,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (dto is null || string.IsNullOrWhiteSpace(dto.Label))
            return TypedResults.BadRequest(new ProblemDetails { Title = "Label is required", Status = 400 });

        var record = SavedQueryDtoMapper.FromCreate(dto);
        var created = await store.CreateAsync(record, ct);
        return TypedResults.Created($"/api/saved-queries/{created.SavedQueryId}", SavedQueryDtoMapper.Map(created));
    }

    public static async Task<Results<Ok<SavedQueryDto>, NotFound, ProblemHttpResult>> HandleUpdateAsync(
        string id,
        [FromBody] UpdateSavedQueryDto? dto,
        [FromServices] ISavedQueryStore store,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (dto is null)
            return TypedResults.Problem(new ProblemDetails { Title = "Request body required", Status = 400 });

        if (dto.Label is not null && string.IsNullOrWhiteSpace(dto.Label))
            return TypedResults.Problem(new ProblemDetails { Title = "Label must not be empty", Status = 400 });

        var existing = await store.GetAsync(id, ct);
        if (existing is null) return TypedResults.NotFound();

        try
        {
            var updated = existing with
            {
                Label      = dto.Label       ?? existing.Label,
                Description= dto.Description ?? existing.Description,
                Sql        = dto.Sql         ?? existing.Sql,
                Parameters = dto.Parameters?.Select(SavedQueryDtoMapper.FromParamDto).ToList()
                             ?? existing.Parameters,
                Tags       = dto.Tags?.ToList() ?? existing.Tags,
                IsFavorite = dto.IsFavorite  ?? existing.IsFavorite,
            };
            var result = await store.UpdateAsync(updated, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(SavedQueryDtoMapper.Map(result));
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
        [FromServices] ISavedQueryStore store,
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

    public static async Task<Results<Ok<SavedQueryDto>, NotFound>> HandleFavoriteAsync(
        string id,
        [FromServices] ISavedQueryStore store,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        var updated = await store.ToggleFavoriteAsync(id, ct);
        return updated is null ? TypedResults.NotFound() : TypedResults.Ok(SavedQueryDtoMapper.Map(updated));
    }

    public static async Task<Results<Created<SavedQueryDto>, NotFound>> HandleCloneAsync(
        string id,
        [FromBody] CloneSavedQueryDto? dto,
        [FromServices] ISavedQueryStore store,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        var existing = await store.GetAsync(id, ct);
        if (existing is null) return TypedResults.NotFound();

        var clone = existing with
        {
            SavedQueryId = "",
            IsBuiltIn    = false,
            IsFavorite   = false,
            Author       = dto?.Author ?? existing.Author,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            RunCount     = 0,
            LastRunAtUtc = null,
        };
        var created = await store.CreateAsync(clone, ct);
        return TypedResults.Created($"/api/saved-queries/{created.SavedQueryId}", SavedQueryDtoMapper.Map(created));
    }

    public static async Task<Results<NoContent, NotFound>> HandleRunAsync(
        string id,
        [FromServices] ISavedQueryStore store,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        var existing = await store.GetAsync(id, ct);
        if (existing is null) return TypedResults.NotFound();
        await store.IncrementRunCountAsync(id, ct);
        return TypedResults.NoContent();
    }
}
