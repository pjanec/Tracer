using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Queries;

namespace Tracer.WebApi.Endpoints;

public static class BundleLibraryEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet   ("/api/bundles/library",           HandleListAsync).WithOpenApi();
        app.MapPut   ("/api/bundles/{id}/metadata",     HandleUpdateMetadataAsync).WithOpenApi();
        app.MapPost  ("/api/bundles/{id}/opened",       HandleRecordOpenedAsync).WithOpenApi();
        app.MapPost  ("/api/bundles/import",            HandleImportAsync).WithOpenApi();
    }

    public static async Task<Ok<BundleLibraryListDto>> HandleListAsync(
        [FromQuery] bool? archived,
        [FromQuery] string? tag,
        [FromQuery] string? sortBy,
        [FromQuery] bool? desc,
        [FromServices] BundleLibraryService service,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(service);
        var all = await service.ListAsync(ct);

        // Filter
        var filtered = all.AsEnumerable();
        if (archived != true)
            filtered = filtered.Where(b => !b.IsArchived);
        if (!string.IsNullOrEmpty(tag))
            filtered = filtered.Where(b => b.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));

        // Sort
        var descending = desc ?? false;
        filtered = (sortBy?.ToLowerInvariant()) switch
        {
            "sessionstart" => descending
                ? filtered.OrderByDescending(b => b.SessionStartUtc)
                : filtered.OrderBy(b => b.SessionStartUtc),
            "size" => descending
                ? filtered.OrderByDescending(b => b.SizeBytes)
                : filtered.OrderBy(b => b.SizeBytes),
            "label" => descending
                ? filtered.OrderByDescending(b => b.Label ?? "")
                : filtered.OrderBy(b => b.Label ?? ""),
            _ => descending
                ? filtered.OrderByDescending(b => b.BuiltAtUtc)
                : filtered.OrderBy(b => b.BuiltAtUtc),
        };

        var dtos = filtered.Select(BundleLibraryDtoMapper.Map).ToList();
        return TypedResults.Ok(new BundleLibraryListDto { Entries = dtos });
    }

    public static async Task<Results<Ok<BundleLibraryEntryDto>, NotFound>> HandleUpdateMetadataAsync(
        string id,
        [FromBody] UpdateBundleMetadataDto? dto,
        [FromServices] BundleLibraryService service,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (dto is null) return TypedResults.NotFound();

        var update = new BundleMetadataUpdate
        {
            Label       = dto.Label,
            Description = dto.Description,
            Tags        = dto.Tags,
            IsArchived  = dto.Archived,
        };

        var updated = await service.UpdateMetadataAsync(id, update, ct);
        if (!updated) return TypedResults.NotFound();

        // Reload the entry to return it
        var all = await service.ListAsync(ct);
        var entry = all.FirstOrDefault(e => e.BundleId == id);
        return entry is null ? TypedResults.NotFound() : TypedResults.Ok(BundleLibraryDtoMapper.Map(entry));
    }

    public static async Task<Results<NoContent, NotFound>> HandleRecordOpenedAsync(
        string id,
        [FromServices] BundleLibraryService service,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(service);
        var updated = await service.RecordOpenedAsync(id, ct);
        return updated ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    public static async Task<Results<NoContent, NotFound>> HandleDeleteAsync(
        string id,
        [FromServices] BundleLibraryService service,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(service);
        var deleted = await service.DeleteAsync(id, ct);
        return deleted ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    public static async Task<Results<Created<BundleLibraryEntryDto>, Conflict<ProblemDetails>, BadRequest<ProblemDetails>>> HandleImportAsync(
        HttpRequest request,
        [FromServices] BundleImportService importService,
        [FromServices] BundleLibraryService libraryService,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(importService);
        ArgumentNullException.ThrowIfNull(libraryService);
        ArgumentNullException.ThrowIfNull(request);

        Stream zipStream;
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null)
                return TypedResults.BadRequest(new ProblemDetails { Title = "No file uploaded", Status = 400 });
            zipStream = file.OpenReadStream();
        }
        else
        {
            zipStream = request.Body;
        }

        var result = await importService.ImportAsync(zipStream, ct);

        if (result.AlreadyExists)
            return TypedResults.Conflict(new ProblemDetails
            {
                Title  = "Bundle already exists",
                Detail = $"Bundle '{result.BundleId}' is already present in the library",
                Status = 409,
            });

        if (result.IsInvalidFormat)
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title  = "Invalid bundle archive",
                Detail = result.ErrorMessage,
                Status = 400,
            });

        // Reload entry
        var all = await libraryService.ListAsync(ct);
        var entry = all.FirstOrDefault(e => e.BundleId == result.BundleId);
        if (entry is null)
            return TypedResults.BadRequest(new ProblemDetails { Title = "Import succeeded but bundle not found", Status = 400 });

        return TypedResults.Created($"/api/bundles/library", BundleLibraryDtoMapper.Map(entry));
    }

    public static async Task<Results<FileStreamHttpResult, NotFound>> HandleDownloadAsync(
        string id,
        [FromServices] BundleExportService exportService,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(exportService);

        var ms = new MemoryStream();
        var found = await exportService.ExportAsync(id, ms, ct);
        if (!found)
        {
            await ms.DisposeAsync();
            return TypedResults.NotFound();
        }
        ms.Position = 0;
        return TypedResults.File(ms, "application/zip", $"{id}.bundle.zip");
    }
}
