using System.IO.Compression;
using Tracer.WebApi.Queries;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class BundleExportServiceTests : IDisposable
{
    private readonly string _root;
    private readonly BundleExportService _svc;

    public BundleExportServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"bexp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _svc = new BundleExportService(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void CreateBundle(string id)
    {
        var dir = Path.Combine(_root, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "metadata.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "events.parquet"), "fake");
    }

    [Fact]
    public async Task Export_NonExistentBundle_ReturnsFalse()
    {
        using var ms = new MemoryStream();
        var found = await _svc.ExportAsync("no-such-bundle", ms, default);
        Assert.False(found);
    }

    [Fact]
    public async Task Export_CreatesZip()
    {
        CreateBundle("b1");
        using var ms = new MemoryStream();
        var found = await _svc.ExportAsync("b1", ms, default);
        Assert.True(found);
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.NotEmpty(zip.Entries);
    }

    [Fact]
    public async Task Export_ZipContainsMetadata()
    {
        CreateBundle("b2");
        using var ms = new MemoryStream();
        await _svc.ExportAsync("b2", ms, default);
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.Contains(zip.Entries, e => e.FullName.EndsWith("metadata.json"));
    }
}
