using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Queries;

namespace Tracer.WebApi.Endpoints;

public static class SqlEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/sql/execute",       HandleExecuteAsync).WithOpenApi();
        app.MapGet ("/api/sql/schema",        HandleSchemaAsync).WithOpenApi();
        app.MapPost("/api/sql/explain",       HandleExplainAsync).WithOpenApi();
        app.MapGet ("/api/sql/view-template", HandleViewTemplateAsync).WithOpenApi();
    }

    public static async Task<Results<Ok<SqlExecuteResultDto>, ProblemHttpResult>> HandleExecuteAsync(
        [FromBody] SqlExecuteRequestDto? dto,
        [FromServices] SqlExecutorService service,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (dto is null || string.IsNullOrWhiteSpace(dto.Sql))
            return TypedResults.Problem(new ProblemDetails { Title = "SQL required", Status = 400 });

        var result = await service.ExecuteAsync(new SqlExecutionRequest
        {
            Sql           = dto.Sql,
            Parameters    = dto.Parameters,
            TimeoutSeconds = dto.TimeoutSeconds is { } ts ? Math.Clamp(ts, 1, 300) : null,
            MaxRows       = dto.MaxRows is { } mr ? Math.Clamp(mr, 1, 1_000_000) : null,
        }, ct);

        return TypedResults.Ok(SqlDtoMapper.MapResult(result));
    }

    public static async Task<Ok<SqlSchemaDto>> HandleSchemaAsync(
        [FromServices] SqlSchemaService service,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(service);
        var snap = await service.GetAsync(ct);
        return TypedResults.Ok(SqlDtoMapper.MapSchema(snap));
    }

    public static async Task<Results<Ok<SqlExplainResultDto>, ProblemHttpResult>> HandleExplainAsync(
        [FromBody] SqlExplainRequestDto? dto,
        [FromServices] SqlExecutorService service,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (dto is null || string.IsNullOrWhiteSpace(dto.Sql))
            return TypedResults.Problem(new ProblemDetails { Title = "SQL required", Status = 400 });

        var result = await service.ExplainAsync(dto.Sql, ct);
        if (result.Failed)
            return TypedResults.Problem(new ProblemDetails
            {
                Title  = "Cannot explain",
                Detail = result.ErrorMessage,
                Status = 400,
            });

        return TypedResults.Ok(new SqlExplainResultDto { PlanText = result.PlanText ?? "" });
    }

    public static Results<Ok<ViewSqlTemplateResultDto>, ProblemHttpResult> HandleViewTemplateAsync(
        [FromQuery] string? view,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? topic,
        [FromQuery] string? publisherNode,
        [FromQuery] string? entityId,
        [FromQuery] string? traceId,
        [FromServices] ViewSqlTemplateService service)
    {
        ArgumentNullException.ThrowIfNull(service);

        if (string.IsNullOrWhiteSpace(view) || !service.IsKnownView(view))
            return TypedResults.Problem(new ProblemDetails
            {
                Title  = "Unknown view type",
                Detail = $"Valid values: timeline, entity-history, causal, latency, gaps, topology",
                Status = 400,
            });

        DateTimeOffset? fromDt = null, toDt = null;
        if (!string.IsNullOrEmpty(from) && !DateTimeOffset.TryParse(from, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var parsedFrom))
            return TypedResults.Problem(new ProblemDetails { Title = "Invalid 'from' timestamp", Status = 400 });
        else if (!string.IsNullOrEmpty(from))
            fromDt = DateTimeOffset.Parse(from, null, System.Globalization.DateTimeStyles.RoundtripKind);

        if (!string.IsNullOrEmpty(to) && !DateTimeOffset.TryParse(to, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var parsedTo))
            return TypedResults.Problem(new ProblemDetails { Title = "Invalid 'to' timestamp", Status = 400 });
        else if (!string.IsNullOrEmpty(to))
            toDt = DateTimeOffset.Parse(to, null, System.Globalization.DateTimeStyles.RoundtripKind);

        var p = new ViewTemplateParams
        {
            From          = fromDt,
            To            = toDt,
            Topic         = topic,
            PublisherNode = publisherNode,
            EntityId      = entityId,
            TraceId       = traceId,
        };

        try
        {
            var template = service.Generate(view, p);
            return TypedResults.Ok(new ViewSqlTemplateResultDto
            {
                Sql         = template.Sql,
                Description = template.Description,
            });
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(new ProblemDetails { Title = ex.Message, Status = 400 });
        }
    }
}
