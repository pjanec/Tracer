using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Agent.Configuration;
using Tracer.Agent.Lifecycle;
using Tracer.Agent.Storage;
using Tracer.Agent.Upload;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Agent;

public sealed class StartupRecoveryTests : IAsyncDisposable
{
    private readonly string _tempDir;

    public StartupRecoveryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
        return ValueTask.CompletedTask;
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeClock : IClock
    {
        public WallclockTime Now => WallclockTime.FromDateTimeOffset(
            new DateTimeOffset(2026, 5, 20, 0, 0, 0, TimeSpan.Zero));
    }

    private sealed class FakeUploadService : ITelemetryUploadService
    {
        public List<UploadRequest> Requests { get; } = new();

        public Task<UploadIntentId> RequestUploadAsync(UploadRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(new UploadIntentId(Guid.NewGuid().ToString("N")));
        }

        public Task<UploadStatus> GetStatusAsync(UploadIntentId intentId, CancellationToken ct)
            => Task.FromResult(UploadStatus.Unknown);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static AgentConfig MakeConfig(string dataRoot) => new()
    {
        NodeId = "test-node",
        DataRoot = dataRoot,
        LogsRoot = dataRoot,
        IntervalDuration = TimeSpan.FromHours(1),
    };

    private StartupRecoveryService BuildService(AgentConfig config, FakeUploadService upload)
    {
        var dispatcher = new UploadIntentDispatcher(
            upload, NullLogger<UploadIntentDispatcher>.Instance);
        return new StartupRecoveryService(
            config, dispatcher, new FakeClock(),
            NullLogger<StartupRecoveryService>.Instance);
    }

    private static string MakeIntervalDir(string dataRoot, string timestamp)
    {
        var path = Path.Combine(dataRoot, "intervals", timestamp);
        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Path.Combine(path, "fast_state"));
        return path;
    }

    private static void MarkReady(string intervalPath)
    {
        File.WriteAllBytes(Path.Combine(intervalPath, "_ready"), Array.Empty<byte>());
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartupRecovery_NoIntervalsDirectory_CreatesDirectoryAndReturns()
    {
        var config = MakeConfig(_tempDir);
        var upload = new FakeUploadService();
        var service = BuildService(config, upload);

        await service.RecoverAsync(CancellationToken.None);

        Directory.Exists(Path.Combine(_tempDir, "intervals")).Should().BeTrue();
        upload.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task StartupRecovery_NoOrphans_LogsAndReturns()
    {
        var config = MakeConfig(_tempDir);
        var upload = new FakeUploadService();

        // Create a ready interval (not an orphan)
        var ts = "20260519T140000Z";
        var intervalPath = MakeIntervalDir(_tempDir, ts);
        MarkReady(intervalPath);

        var service = BuildService(config, upload);
        await service.RecoverAsync(CancellationToken.None);

        upload.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task StartupRecovery_OneOrphan_WritesManifestAndSentinel()
    {
        var config = MakeConfig(_tempDir);
        var upload = new FakeUploadService();

        var ts = "20260519T140000Z";
        var intervalPath = MakeIntervalDir(_tempDir, ts);
        // NOT marked ready — it's an orphan

        var service = BuildService(config, upload);
        await service.RecoverAsync(CancellationToken.None);

        File.Exists(Path.Combine(intervalPath, "manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(intervalPath, "_ready")).Should().BeTrue();
    }

    [Fact]
    public async Task StartupRecovery_OneOrphan_ManifestHasRecoveryReason()
    {
        var config = MakeConfig(_tempDir);
        var upload = new FakeUploadService();

        var ts = "20260519T140000Z";
        MakeIntervalDir(_tempDir, ts);

        var service = BuildService(config, upload);
        await service.RecoverAsync(CancellationToken.None);

        var manifestPath = Path.Combine(_tempDir, "intervals", ts, "manifest.json");
        var manifest = await ManifestWriter.ReadAsync(manifestPath, CancellationToken.None);

        manifest.Should().NotBeNull();
        manifest!.FinalizationReason.Should().Be(ManifestFinalizationReason.RecoveryAfterCrash);
        manifest.CaptureGaps.Should().ContainSingle()
            .Which.Reason.Should().Be(CaptureGapReason.UnrecoveredCrashGap);
    }

    [Fact]
    public async Task StartupRecovery_MultipleOrphans_AllFinalized()
    {
        var config = MakeConfig(_tempDir);
        var upload = new FakeUploadService();

        MakeIntervalDir(_tempDir, "20260519T130000Z");
        MakeIntervalDir(_tempDir, "20260519T140000Z");
        MakeIntervalDir(_tempDir, "20260519T150000Z");

        var service = BuildService(config, upload);
        await service.RecoverAsync(CancellationToken.None);

        upload.Requests.Should().HaveCount(3);
        foreach (var ts in new[] { "20260519T130000Z", "20260519T140000Z", "20260519T150000Z" })
        {
            File.Exists(Path.Combine(_tempDir, "intervals", ts, "_ready"))
                .Should().BeTrue($"interval {ts} should be finalized");
        }
    }

    [Fact]
    public async Task StartupRecovery_CorruptEventsDb_CountsAsZeroAndContinues()
    {
        var config = MakeConfig(_tempDir);
        var upload = new FakeUploadService();

        var ts = "20260519T140000Z";
        var intervalPath = MakeIntervalDir(_tempDir, ts);

        // Write corrupt (non-DuckDB) content to events.duckdb
        File.WriteAllText(Path.Combine(intervalPath, "events.duckdb"), "NOT A DUCKDB FILE");

        var service = BuildService(config, upload);
        var act = async () => await service.RecoverAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        // Finalization should still complete despite corrupt DB
        File.Exists(Path.Combine(intervalPath, "_ready")).Should().BeTrue();
        upload.Requests.Should().HaveCount(1);
    }
}
