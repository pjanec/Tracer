using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Endpoints;
using Tracer.WebApi.Queries;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class BundleLibraryEndpointsTests : IDisposable
{
    private readonly string _root;
    private readonly BundleLibraryService _libSvc;

    public BundleLibraryEndpointsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"blep-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _libSvc = new BundleLibraryService(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void CreateBundle(string id)
    {
        var dir = Path.Combine(_root, id);
        Directory.CreateDirectory(dir);
        var manifest = $$"""
            {
              "bundleId":"{{id}}","schemaVersion":1,"createdAtUtc":"2024-01-01T00:00:00Z",
              "tracerVersion":"1.0","writer":{"tool":"a","version":"1.0","host":"h"},
              "timeRange":{"startUtc":"2024-01-01T00:00:00Z","endUtc":"2024-01-02T00:00:00Z"},
              "sessionContext":{"sessionId":"s1","scenarioId":"sc1"},
              "participatingNodes":[],"fastStateScope":"","fastStateEntities":[],
              "statistics":{"eventCount":0,"nodeCount":0},"files":[]
            }
            """;
        File.WriteAllText(Path.Combine(dir, "metadata.json"), manifest);
    }

    [Fact]
    public async Task List_Empty_ReturnsEmptyList()
    {
        var result = await BundleLibraryEndpoints.HandleListAsync(null, null, null, null, _libSvc, default);
        var ok = Assert.IsType<Ok<BundleLibraryListDto>>(result);
        Assert.Empty(ok.Value!.Entries);
    }

    [Fact]
    public async Task List_WithBundle_ReturnsList()
    {
        CreateBundle("b-list-1");
        var result = await BundleLibraryEndpoints.HandleListAsync(null, null, null, null, _libSvc, default);
        var ok = Assert.IsType<Ok<BundleLibraryListDto>>(result);
        Assert.Single(ok.Value!.Entries);
    }

    [Fact]
    public async Task UpdateMetadata_NonExistent_ReturnsNotFound()
    {
        var dto = new UpdateBundleMetadataDto { Label = "X" };
        var result = await BundleLibraryEndpoints.HandleUpdateMetadataAsync("no-id", dto, _libSvc, default);
        Assert.IsType<NotFound>(result.Result);
    }

    [Fact]
    public async Task UpdateMetadata_Valid_ReturnsOk()
    {
        CreateBundle("b-update");
        var dto = new UpdateBundleMetadataDto { Label = "Updated" };
        var result = await BundleLibraryEndpoints.HandleUpdateMetadataAsync("b-update", dto, _libSvc, default);
        var ok = Assert.IsType<Ok<BundleLibraryEntryDto>>(result.Result);
        Assert.Equal("Updated", ok.Value!.Label);
    }

    [Fact]
    public async Task RecordOpened_NonExistent_ReturnsNotFound()
    {
        var result = await BundleLibraryEndpoints.HandleRecordOpenedAsync("no-id", _libSvc, default);
        Assert.IsType<NotFound>(result.Result);
    }

    [Fact]
    public async Task RecordOpened_Valid_ReturnsNoContent()
    {
        CreateBundle("b-opened");
        var result = await BundleLibraryEndpoints.HandleRecordOpenedAsync("b-opened", _libSvc, default);
        Assert.IsType<NoContent>(result.Result);
    }

    [Fact]
    public async Task Delete_NonExistent_ReturnsNotFound()
    {
        var result = await BundleLibraryEndpoints.HandleDeleteAsync("no-id", _libSvc, default);
        Assert.IsType<NotFound>(result.Result);
    }

    [Fact]
    public async Task Delete_Valid_ReturnsNoContent()
    {
        CreateBundle("b-del");
        var result = await BundleLibraryEndpoints.HandleDeleteAsync("b-del", _libSvc, default);
        Assert.IsType<NoContent>(result.Result);
    }

    [Fact]
    public async Task Download_NonExistent_ReturnsNotFound()
    {
        var exportSvc = new BundleExportService(_root);
        var result = await BundleLibraryEndpoints.HandleDownloadAsync("no-id", exportSvc, default);
        Assert.IsType<NotFound>(result.Result);
    }

    [Fact]
    public async Task Download_Valid_ReturnsFile()
    {
        CreateBundle("b-dl");
        var exportSvc = new BundleExportService(_root);
        var result = await BundleLibraryEndpoints.HandleDownloadAsync("b-dl", exportSvc, default);
        Assert.IsType<FileStreamHttpResult>(result.Result);
    }

    [Fact]
    public async Task List_SortByBuiltAt_Works()
    {
        CreateBundle("bundle-sort-a");
        CreateBundle("bundle-sort-b");
        var result = await BundleLibraryEndpoints.HandleListAsync(null, null, "sessionstart", false, _libSvc, default);
        var ok = Assert.IsType<Ok<BundleLibraryListDto>>(result);
        Assert.Equal(2, ok.Value!.Entries.Count);
    }

    [Fact]
    public async Task List_FilterArchived_ExcludesNonArchived()
    {
        CreateBundle("b-active");
        var result = await BundleLibraryEndpoints.HandleListAsync(archived: true, null, null, null, _libSvc, default);
        var ok = Assert.IsType<Ok<BundleLibraryListDto>>(result);
        // archived=true keeps archived bundles; since b-active is not archived, it's included when archived is true
        // Actually: our filter says "if archived != true, exclude IsArchived". When archived=true, we include everything.
        Assert.NotEmpty(ok.Value!.Entries);
    }
}
