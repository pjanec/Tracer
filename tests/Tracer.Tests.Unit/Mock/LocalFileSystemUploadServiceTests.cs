using System.IO.Compression;
using FluentAssertions;
using Tracer.Adapters.Mock.Upload;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Mock;

public sealed class LocalFileSystemUploadServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _stagingRoot;

    public LocalFileSystemUploadServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _stagingRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(_stagingRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
        if (Directory.Exists(_stagingRoot))
            Directory.Delete(_stagingRoot, recursive: true);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static UploadRequest MakeRequest(
        string nodeId,
        string interval,
        IReadOnlyList<FileToUpload> files) => new()
    {
        NodeId = new AgentId(nodeId),
        Interval = ParseTs(interval),
        IntervalStartUtc = WallclockTime.Zero,
        IntervalEndUtc = WallclockTime.Zero,
        Files = files,
    };

    private static IntervalTimestamp ParseTs(string ts)
    {
        IntervalTimestamp.TryParse(ts, out var result);
        return result!;
    }

    private string CreateFile(string relativePath, string content = "data")
    {
        var full = Path.Combine(_tempRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LocalFileSystemUploadService_Upload_CreatesZipAtExpectedPath()
    {
        var svc = new LocalFileSystemUploadService(_stagingRoot);
        var nodeFile = CreateFile("events.duckdb", "DUCKDB");
        var request = MakeRequest("node-1", "20260519T140000Z",
            new[] { new FileToUpload { Path = nodeFile, SizeBytes = 6, Description = "events" } });

        var id = await svc.RequestUploadAsync(request, CancellationToken.None);

        var expectedPath = Path.Combine(_stagingRoot, "node-1", "20260519T140000Z.zip");
        File.Exists(expectedPath).Should().BeTrue();
    }

    [Fact]
    public async Task LocalFileSystemUploadService_Upload_ZipContainsAllFiles()
    {
        var svc = new LocalFileSystemUploadService(_stagingRoot);
        var f1 = CreateFile("events.duckdb", "DUCKDB");
        var f2 = CreateFile("fast_state/sensors.parquet", "PARQUET");
        var request = MakeRequest("node-1", "20260519T140000Z",
            new[]
            {
                new FileToUpload { Path = f1, SizeBytes = 6, Description = "events" },
                new FileToUpload { Path = f2, SizeBytes = 7, Description = "fast_state" },
            });

        await svc.RequestUploadAsync(request, CancellationToken.None);

        var zipPath = Path.Combine(_stagingRoot, "node-1", "20260519T140000Z.zip");
        using var zip = ZipFile.OpenRead(zipPath);
        zip.Entries.Should().Contain(e => e.Name == "events.duckdb");
        zip.Entries.Should().Contain(e => e.FullName == "fast_state/sensors.parquet");
    }

    [Fact]
    public async Task LocalFileSystemUploadService_Upload_Idempotent()
    {
        var svc = new LocalFileSystemUploadService(_stagingRoot);
        var nodeFile = CreateFile("events.duckdb", "DATA");
        var request = MakeRequest("node-1", "20260519T140000Z",
            new[] { new FileToUpload { Path = nodeFile, SizeBytes = 4, Description = "events" } });

        // Upload twice — should not throw
        var id1 = await svc.RequestUploadAsync(request, CancellationToken.None);
        var id2 = await svc.RequestUploadAsync(request, CancellationToken.None);

        var status1 = await svc.GetStatusAsync(id1, CancellationToken.None);
        var status2 = await svc.GetStatusAsync(id2, CancellationToken.None);

        status1.Should().Be(UploadStatus.Complete);
        status2.Should().Be(UploadStatus.Complete);
    }

    [Fact]
    public async Task LocalFileSystemUploadService_GetStatus_UnknownId_ReturnsUnknown()
    {
        var svc = new LocalFileSystemUploadService(_stagingRoot);
        var unknownId = new UploadIntentId("00000000000000000000000000000000");

        var status = await svc.GetStatusAsync(unknownId, CancellationToken.None);

        status.Should().Be(UploadStatus.Unknown);
    }
}
