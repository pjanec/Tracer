using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tracer.Core.Time;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Queries;
using Tracer.WebApi.Util;

namespace Tracer.WebApi.Endpoints;

public static class LatencyEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/latency/distribution", HandleDistributionAsync)
            .WithName("GetLatencyDistribution")
            .WithOpenApi();

        app.MapGet("/api/latency/pairs", HandlePairsAsync)
            .WithName("GetLatencyPairs")
            .WithOpenApi();

        app.MapGet("/api/latency/timeseries", HandleTimeSeriesAsync)
            .WithName("GetLatencyTimeSeries")
            .WithOpenApi();

        app.MapGet("/api/latency/outliers", HandleOutliersAsync)
            .WithName("GetLatencyOutliers")
            .WithOpenApi();
    }

    public static async Task<IResult> HandleDistributionAsync(
        [FromServices] IServiceProvider sp,
        [FromServices] LatencyDistributionService svc,
        CancellationToken ct,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? topic = null,
        [FromQuery] string? publisherNode = null,
        [FromQuery] string? subscriberNode = null,
        [FromQuery] bool excludeSelf = true)
    {
        ArgumentNullException.ThrowIfNull(sp);
        ArgumentNullException.ThrowIfNull(svc);
        var gate = BundleModeGate.CheckBundleOrLive(sp);
        if (gate is not null) return gate;

        if (from is null || to is null)
            return Results.Problem(new ProblemDetails
            {
                Title = "Missing parameters",
                Detail = "from and to query parameters are required.",
                Status = StatusCodes.Status400BadRequest
            });

        if (from.Value >= to.Value)
            return Results.Problem(new ProblemDetails
            {
                Title = "Invalid time range",
                Detail = "from must be before to.",
                Status = StatusCodes.Status400BadRequest
            });

        var query = new LatencyQuery
        {
            From = WallclockTime.FromDateTimeOffset(from.Value),
            To = WallclockTime.FromDateTimeOffset(to.Value),
            Topic = topic,
            PublisherNode = publisherNode,
            SubscriberNode = subscriberNode,
            ExcludeSelfSubscribe = excludeSelf,
        };

        var result = await svc.GetAsync(query, ct);
        return Results.Ok(LatencyDtoMapper.Map(result));
    }

    public static async Task<IResult> HandlePairsAsync(
        [FromServices] IServiceProvider sp,
        [FromServices] LatencyDistributionService svc,
        CancellationToken ct,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int minSamples = 10,
        [FromQuery] int limit = 100)
    {
        ArgumentNullException.ThrowIfNull(sp);
        ArgumentNullException.ThrowIfNull(svc);
        var gate = BundleModeGate.CheckBundleOrLive(sp);
        if (gate is not null) return gate;

        if (from is null || to is null)
            return Results.Problem(new ProblemDetails
            {
                Title = "Missing parameters",
                Detail = "from and to query parameters are required.",
                Status = StatusCodes.Status400BadRequest
            });

        var pairs = await svc.ListByPairAsync(
            WallclockTime.FromDateTimeOffset(from.Value),
            WallclockTime.FromDateTimeOffset(to.Value),
            minSamples,
            Math.Clamp(limit, 1, 1000),
            ct);

        return Results.Ok(LatencyDtoMapper.MapPairs(pairs));
    }

    public static async Task<IResult> HandleTimeSeriesAsync(
        [FromServices] IServiceProvider sp,
        [FromServices] LatencyTimeSeriesService svc,
        CancellationToken ct,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? topic = null,
        [FromQuery] string? publisherNode = null,
        [FromQuery] string? subscriberNode = null,
        [FromQuery] bool excludeSelf = true)
    {
        ArgumentNullException.ThrowIfNull(sp);
        ArgumentNullException.ThrowIfNull(svc);
        var gate = BundleModeGate.CheckBundleOrLive(sp);
        if (gate is not null) return gate;

        if (from is null || to is null)
            return Results.Problem(new ProblemDetails
            {
                Title = "Missing parameters",
                Detail = "from and to query parameters are required.",
                Status = StatusCodes.Status400BadRequest
            });

        var query = new LatencyTimeSeriesQuery
        {
            From = WallclockTime.FromDateTimeOffset(from.Value),
            To = WallclockTime.FromDateTimeOffset(to.Value),
            Topic = topic,
            PublisherNode = publisherNode,
            SubscriberNode = subscriberNode,
            ExcludeSelfSubscribe = excludeSelf,
        };

        var result = await svc.GetAsync(query, ct);
        return Results.Ok(LatencyDtoMapper.MapTimeSeries(result));
    }

    public static async Task<IResult> HandleOutliersAsync(
        [FromServices] IServiceProvider sp,
        [FromServices] LatencyOutlierService svc,
        CancellationToken ct,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? topic = null,
        [FromQuery] double? thresholdMs = null,
        [FromQuery] int limit = 100)
    {
        ArgumentNullException.ThrowIfNull(sp);
        ArgumentNullException.ThrowIfNull(svc);
        var gate = BundleModeGate.CheckBundleOrLive(sp);
        if (gate is not null) return gate;

        if (from is null || to is null)
            return Results.Problem(new ProblemDetails
            {
                Title = "Missing parameters",
                Detail = "from and to query parameters are required.",
                Status = StatusCodes.Status400BadRequest
            });

        var query = new LatencyOutlierQuery
        {
            From = WallclockTime.FromDateTimeOffset(from.Value),
            To = WallclockTime.FromDateTimeOffset(to.Value),
            Topic = topic,
            ThresholdMs = thresholdMs,
            Limit = Math.Clamp(limit, 1, 1000),
        };

        var result = await svc.GetOutliersAsync(query, ct);
        return Results.Ok(LatencyDtoMapper.MapOutliers(result));
    }
}
