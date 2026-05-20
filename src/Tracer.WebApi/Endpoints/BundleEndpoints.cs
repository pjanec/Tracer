using System.IO.Compression;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tracer.WebApi.Bundles;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Errors;

namespace Tracer.WebApi.Endpoints;

public static class BundleEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/bundles/build", HandleBuildAsync).WithOpenApi();
        app.MapGet("/api/bundles", HandleListAsync).WithOpenApi();
        app.MapGet("/api/bundles/{bundleId}", HandleGetAsync).WithOpenApi();
        app.MapGet("/api/bundles/{bundleId}/status", HandleStatusAsync).WithOpenApi();
        app.MapGet("/api/bundles/{bundleId}/download", HandleDownloadAsync).WithOpenApi();
        app.MapDelete("/api/bundles/{bundleId}", HandleDeleteAsync).WithOpenApi();
    }

    public static async Task<Results<Accepted<BundleBuildAcceptedDto>, ProblemHttpResult>> HandleBuildAsync(
        [FromBody] BundleBuildRequestDto request,
        [FromServices] BundleBuildService builds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(builds);
        try
        {
            var bundleId = await builds.QueueBuildAsync(request, ct);
            return TypedResults.Accepted(
                $"/api/bundles/{bundleId}/status",
                new BundleBuildAcceptedDto { BundleId = bundleId });
        }
        catch (Exception ex)
        {
            return TypedResults.Problem(ProblemDetailsFactory.From(ex));
        }
    }

    public static Task<Ok<BundleBuildStatusDto>> HandleStatusAsync(
        string bundleId,
        [FromServices] BundleBuildService builds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(builds);
        var status = builds.GetStatus(bundleId);
        return Task.FromResult(TypedResults.Ok(status));
    }

    public static async Task<Results<Ok<BundleListDto>, ProblemHttpResult>> HandleListAsync(
        [FromServices] BundleCatalog catalog,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var entries = await catalog.ListAsync(ct);
        return TypedResults.Ok(new BundleListDto { Bundles = entries });
    }

    public static async Task<Results<Ok<BundleManifestDto>, NotFound>> HandleGetAsync(
        string bundleId,
        [FromServices] BundleCatalog catalog,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var manifest = await catalog.GetManifestAsync(bundleId, ct);
        return manifest is null ? TypedResults.NotFound() : TypedResults.Ok(manifest);
    }

    public static async Task<Results<FileStreamHttpResult, NotFound>> HandleDownloadAsync(
        string bundleId,
        [FromServices] BundleCatalog catalog,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var bundle = await catalog.GetAsync(bundleId, ct);
        if (bundle is null) return TypedResults.NotFound();

        if (bundle.IsZipped)
        {
            var stream = File.OpenRead(bundle.Path);
            return TypedResults.File(stream, "application/zip", $"{bundleId}.tracerbundle.zip");
        }

        // Directory: stream-zip on the fly via a pipe
        var pipe = new Pipe();
        _ = Task.Run(async () =>
        {
            try
            {
                {
                    using var archive = new ZipArchive(pipe.Writer.AsStream(), ZipArchiveMode.Create, leaveOpen: false);
                    foreach (var file in Directory.EnumerateFiles(bundle.Path, "*", SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(bundle.Path, file);
                        var entry = archive.CreateEntry(rel, CompressionLevel.NoCompression);
                        await using var entryStream = entry.Open();
                        await using var src = File.OpenRead(file);
                        await src.CopyToAsync(entryStream, ct);
                    }
                }
            }
            catch (Exception) { /* best effort */ }
            finally { await pipe.Writer.CompleteAsync(); }
        }, ct);
        return TypedResults.File(pipe.Reader.AsStream(), "application/zip", $"{bundleId}.tracerbundle.zip");
    }

    public static async Task<Results<NoContent, NotFound>> HandleDeleteAsync(
        string bundleId,
        [FromServices] BundleCatalog catalog,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var removed = await catalog.DeleteAsync(bundleId, ct);
        return removed ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
