using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tracer.OfflineViewer.Lifecycle;
using Tracer.WebApi.Errors;

namespace Tracer.OfflineViewer.WebApi;

public static class BundleOpenEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/bundle/open", HandleOpenAsync).WithOpenApi();
        app.MapPost("/api/bundle/close", HandleCloseAsync).WithOpenApi();
        app.MapGet("/api/bundle/current", HandleCurrentAsync).WithOpenApi();
    }

    public static async Task<Results<Ok<OpenBundleResponseDto>, ProblemHttpResult>> HandleOpenAsync(
        [FromBody] OpenBundleRequestDto request,
        [FromServices] BundleOpenManager mgr,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(mgr);
        try
        {
            await mgr.OpenAsync(request.Path, ct);
            return TypedResults.Ok(new OpenBundleResponseDto
            {
                BundleId = mgr.Current!.Manifest.BundleId
            });
        }
        catch (Exception ex)
        {
            return TypedResults.Problem(ProblemDetailsFactory.From(ex));
        }
    }

    public static async Task<NoContent> HandleCloseAsync(
        [FromServices] BundleOpenManager mgr, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mgr);
        await mgr.CloseAsync(ct);
        return TypedResults.NoContent();
    }

    public static Task<Ok<CurrentBundleDto?>> HandleCurrentAsync(
        [FromServices] BundleOpenManager mgr)
    {
        ArgumentNullException.ThrowIfNull(mgr);
        var current = mgr.Current;
        if (current is null) return Task.FromResult(TypedResults.Ok<CurrentBundleDto?>(null));
        return Task.FromResult(TypedResults.Ok<CurrentBundleDto?>(new CurrentBundleDto
        {
            BundleId = current.Manifest.BundleId,
            Label = current.Manifest.SessionContext.Label,
            TimeRange = new CurrentBundleTimeRange
            {
                StartUtc = current.Manifest.TimeRange.StartUtc,
                EndUtc = current.Manifest.TimeRange.EndUtc
            }
        }));
    }
}
