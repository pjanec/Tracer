using Tracer.WebApi.Queries;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

/// <summary>
/// Tests for BundleLibraryService file-system operations using a temp directory.
/// </summary>
public sealed class BundleLibraryServiceTests : IDisposable
{
    private readonly string _root;
    private readonly BundleLibraryService _svc;

    public BundleLibraryServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"blsvc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _svc = new BundleLibraryService(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void CreateBundle(string id, string createdAt = "2024-01-01T00:00:00Z")
    {
        var dir = Path.Combine(_root, id);
        Directory.CreateDirectory(dir);
        var manifest = $$"""
            {
              "bundleId": "{{id}}",
              "schemaVersion": 1,
              "createdAtUtc": "{{createdAt}}",
              "tracerVersion": "1.0",
              "writer": {"tool":"agg","version":"1.0","host":"h"},
              "timeRange": {"startUtc": "2024-01-01T00:00:00Z", "endUtc": "2024-01-02T00:00:00Z"},
              "sessionContext": {"sessionId": "sess-1", "scenarioId": "sc-1"},
              "participatingNodes": [],
              "fastStateScope": "",
              "fastStateEntities": [],
              "statistics": {"eventCount": 0, "nodeCount": 0},
              "files": []
            }
            """;
        File.WriteAllText(Path.Combine(dir, "metadata.json"), manifest);
    }

    [Fact]
    public async Task List_EmptyRoot_ReturnsEmpty()
    {
        var entries = await _svc.ListAsync();
        Assert.Empty(entries);
    }

    [Fact]
    public async Task List_BundleWithNoMetadata_IsSkipped()
    {
        Directory.CreateDirectory(Path.Combine(_root, "orphan"));
        var entries = await _svc.ListAsync();
        Assert.Empty(entries);
    }

    [Fact]
    public async Task List_ReturnsBundle()
    {
        CreateBundle("bundle-abc");
        var entries = await _svc.ListAsync();
        Assert.Single(entries);
        Assert.Equal("bundle-abc", entries[0].BundleId);
    }

    [Fact]
    public async Task UpdateMetadata_ChangesLabel()
    {
        CreateBundle("bundle-xyz");
        var ok = await _svc.UpdateMetadataAsync("bundle-xyz", new BundleMetadataUpdate { Label = "My Bundle" });
        Assert.True(ok);
        var entries = await _svc.ListAsync();
        Assert.Equal("My Bundle", entries[0].Label);
    }

    [Fact]
    public async Task UpdateMetadata_NonExistentBundle_ReturnsFalse()
    {
        var ok = await _svc.UpdateMetadataAsync("no-such-bundle", new BundleMetadataUpdate { Label = "X" });
        Assert.False(ok);
    }

    [Fact]
    public async Task RecordOpened_SetsLastOpenedAt()
    {
        CreateBundle("bundle-open");
        await _svc.RecordOpenedAsync("bundle-open");
        var entries = await _svc.ListAsync();
        Assert.NotNull(entries[0].LastOpenedAtUtc);
    }

    [Fact]
    public async Task Delete_RemovesDirectory()
    {
        CreateBundle("bundle-del");
        var deleted = await _svc.DeleteAsync("bundle-del");
        Assert.True(deleted);
        Assert.False(Directory.Exists(Path.Combine(_root, "bundle-del")));
    }

    [Fact]
    public async Task Delete_NonExistent_ReturnsFalse()
    {
        var deleted = await _svc.DeleteAsync("bundle-ghost");
        Assert.False(deleted);
    }

    [Fact]
    public void ComputeDirectorySize_ReturnsSum()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sz-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "a.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(dir, "b.bin"), new byte[200]);
        var size = BundleLibraryService.ComputeDirectorySize(dir);
        Assert.Equal(300L, size);
        Directory.Delete(dir, recursive: true);
    }
}
