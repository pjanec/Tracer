using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tracer.Core.Identity;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Contracts.Mapping;
using Tracer.WebApi.Queries;

namespace Tracer.WebApi.Endpoints;

public static class TraceEndpoints
{
    private static readonly Regex HexPattern =
        new Regex("^[0-9a-fA-F]{16}$", RegexOptions.Compiled);

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/traces/{traceId}",             HandleGetTraceSummaryAsync)
           .WithName("GetTraceSummary").WithOpenApi();

        app.MapGet("/api/traces/{traceId}/tree",        HandleGetTraceTreeAsync)
           .WithName("GetTraceTree").WithOpenApi();

        app.MapGet("/api/events/{eventId}/trace",       HandleGetTraceByEventAsync)
           .WithName("GetTraceByEvent").WithOpenApi();

        app.MapGet("/api/events/{eventId}/ancestors",   HandleAncestorsAsync)
           .WithName("GetEventAncestors").WithOpenApi();

        app.MapGet("/api/events/{eventId}/descendants", HandleDescendantsAsync)
           .WithName("GetEventDescendants").WithOpenApi();
    }

    internal static async Task<Results<Ok<TraceSummaryDto>, NotFound, ProblemHttpResult>>
        HandleGetTraceSummaryAsync(
            string traceId,
            [FromServices] TraceQueryService traces,
            CancellationToken ct)
    {
        if (!HexPattern.IsMatch(traceId))
            return TypedResults.Problem(BadHexDetail("traceId"), statusCode: 400);

        var id = ulong.Parse(traceId, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var tree = await traces.GetTraceTreeAsync(id, maxEvents: 1, ct);
        if (tree is null) return TypedResults.NotFound();

        return TypedResults.Ok(TraceDtoMapper.Map(tree.Summary));
    }

    internal static async Task<Results<Ok<TraceTreeDto>, NotFound, ProblemHttpResult>>
        HandleGetTraceTreeAsync(
            string traceId,
            [FromQuery] int? maxEvents,
            [FromServices] TraceQueryService traces,
            CancellationToken ct)
    {
        if (!HexPattern.IsMatch(traceId))
            return TypedResults.Problem(BadHexDetail("traceId"), statusCode: 400);

        var id  = ulong.Parse(traceId, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var cap = Math.Clamp(maxEvents ?? 1000, 1, 5000);
        var tree = await traces.GetTraceTreeAsync(id, cap, ct);
        if (tree is null) return TypedResults.NotFound();

        return TypedResults.Ok(TraceDtoMapper.Map(tree));
    }

    internal static async Task<Results<Ok<TraceTreeDto>, NotFound, ProblemHttpResult>>
        HandleGetTraceByEventAsync(
            string eventId,
            [FromQuery] int? maxEvents,
            [FromServices] TraceQueryService traces,
            CancellationToken ct)
    {
        if (!HexPattern.IsMatch(eventId))
            return TypedResults.Problem(BadHexDetail("eventId"), statusCode: 400);

        var id  = ulong.Parse(eventId, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var cap = Math.Clamp(maxEvents ?? 1000, 1, 5000);
        var tree = await traces.GetTraceTreeForEventAsync(new EventId(id), cap, ct);
        if (tree is null) return TypedResults.NotFound();

        return TypedResults.Ok(TraceDtoMapper.Map(tree));
    }

    internal static async Task<Results<Ok<TraceTreeDto>, NotFound, ProblemHttpResult>>
        HandleAncestorsAsync(
            string eventId,
            [FromQuery] int? maxDepth,
            [FromServices] TraceQueryService traces,
            CancellationToken ct)
    {
        if (!HexPattern.IsMatch(eventId))
            return TypedResults.Problem(BadHexDetail("eventId"), statusCode: 400);

        var id    = ulong.Parse(eventId, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var depth = Math.Clamp(maxDepth ?? 50, 1, 100);
        var tree  = await traces.GetAncestorTreeAsync(new EventId(id), depth, ct);
        if (tree is null) return TypedResults.NotFound();

        return TypedResults.Ok(TraceDtoMapper.Map(tree));
    }

    internal static async Task<Results<Ok<TraceTreeDto>, NotFound, ProblemHttpResult>>
        HandleDescendantsAsync(
            string eventId,
            [FromQuery] int? maxDepth,
            [FromQuery] int? maxNodes,
            [FromServices] TraceQueryService traces,
            CancellationToken ct)
    {
        if (!HexPattern.IsMatch(eventId))
            return TypedResults.Problem(BadHexDetail("eventId"), statusCode: 400);

        var id    = ulong.Parse(eventId, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var depth = Math.Clamp(maxDepth ?? 30, 1, 100);
        var nodes = Math.Clamp(maxNodes ?? 1000, 1, 5000);
        var tree  = await traces.GetDescendantTreeAsync(new EventId(id), depth, nodes, ct);
        if (tree is null) return TypedResults.NotFound();

        return TypedResults.Ok(TraceDtoMapper.Map(tree));
    }

    private static string BadHexDetail(string field) =>
        $"{field} must be a 16-character hexadecimal string";
}
