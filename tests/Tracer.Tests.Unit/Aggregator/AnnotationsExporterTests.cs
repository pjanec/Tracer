using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Adapters.Mock.Storage;
using Tracer.Aggregator;
using Tracer.Aggregator.Consolidation;
using Tracer.Aggregator.Configuration;
using Tracer.Aggregator.Progress;
using Tracer.Storage.Annotations;
using Xunit;

namespace Tracer.Tests.Unit.Aggregator;

public sealed class AnnotationsExporterTests : IDisposable
{
    private readonly string _tempDir;

    public AnnotationsExporterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ann-exp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private async Task<(SqliteAnnotationStore Store, string DbPath)> CreateStoreAsync()
    {
        var path = Path.Combine(_tempDir, $"a-{Guid.NewGuid():N}.db");
        var store = new SqliteAnnotationStore(path, NullLogger<SqliteAnnotationStore>.Instance);
        await store.InitializeAsync();
        return (store, path);
    }

    private string NewBundleStagingDir() =>
        Path.Combine(_tempDir, $"staging-{Guid.NewGuid():N}");

    // ─── SC-1 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_NoAnnotations_DoesNotCreateFile()
    {
        var (store, _) = await CreateStoreAsync();
        var staging = NewBundleStagingDir();
        Directory.CreateDirectory(staging);

        await AnnotationsExporter.ExportAsync(store, "sess-empty", staging, CancellationToken.None);

        var annotationsDir = Path.Combine(staging, "annotations");
        Directory.Exists(annotationsDir).Should().BeFalse();
    }

    // ─── SC-2 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_WithAnnotations_WritesJsonFile()
    {
        var (store, _) = await CreateStoreAsync();
        for (int i = 0; i < 3; i++)
        {
            await store.CreateAsync(new AnnotationRecord
            {
                AnnotationId = "", SessionId = "sess-1", Kind = AnnotationKind.Event,
                EventId = $"evt-{i:D16}", Body = $"Note {i}", CreatedAtUtc = default,
            }, CancellationToken.None);
        }

        var staging = NewBundleStagingDir();
        Directory.CreateDirectory(staging);
        await AnnotationsExporter.ExportAsync(store, "sess-1", staging, CancellationToken.None);

        var filePath = Path.Combine(staging, "annotations", "annotations.json");
        File.Exists(filePath).Should().BeTrue();

        var json = await File.ReadAllTextAsync(filePath);
        var records = JsonSerializer.Deserialize<List<AnnotationRecord>>(json,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter() },
            });

        records.Should().NotBeNull();
        records!.Should().HaveCount(3);
        records.Should().OnlyContain(r => r.SessionId == "sess-1");
    }

    // ─── SC-3 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_FiltersToTargetSession()
    {
        var (store, _) = await CreateStoreAsync();
        // Session A
        await store.CreateAsync(new AnnotationRecord
        {
            AnnotationId = "", SessionId = "sess-A", Kind = AnnotationKind.Event,
            EventId = "evt-A00000000000001", Body = "A Note", CreatedAtUtc = default,
        }, CancellationToken.None);
        await store.CreateAsync(new AnnotationRecord
        {
            AnnotationId = "", SessionId = "sess-A", Kind = AnnotationKind.Event,
            EventId = "evt-A00000000000002", Body = "A Note 2", CreatedAtUtc = default,
        }, CancellationToken.None);
        // Session B
        await store.CreateAsync(new AnnotationRecord
        {
            AnnotationId = "", SessionId = "sess-B", Kind = AnnotationKind.Event,
            EventId = "evt-B00000000000001", Body = "B Note", CreatedAtUtc = default,
        }, CancellationToken.None);

        var staging = NewBundleStagingDir();
        Directory.CreateDirectory(staging);
        await AnnotationsExporter.ExportAsync(store, "sess-A", staging, CancellationToken.None);

        var filePath = Path.Combine(staging, "annotations", "annotations.json");
        var json = await File.ReadAllTextAsync(filePath);
        var records = JsonSerializer.Deserialize<List<AnnotationRecord>>(json,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter() },
            });

        records.Should().HaveCount(2);
        records!.Should().OnlyContain(r => r.SessionId == "sess-A");
    }

    // ─── SC-4 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_OutputPathMatchesBundleAnnotationStore()
    {
        var (store, _) = await CreateStoreAsync();
        await store.CreateAsync(new AnnotationRecord
        {
            AnnotationId = "", SessionId = "sess-1", Kind = AnnotationKind.Event,
            EventId = "evt-0000000000000001", Body = "hello", CreatedAtUtc = default,
        }, CancellationToken.None);

        var staging = NewBundleStagingDir();
        Directory.CreateDirectory(staging);
        await AnnotationsExporter.ExportAsync(store, "sess-1", staging, CancellationToken.None);

        // BundleAnnotationStore expects: <bundlePath>/annotations/annotations.json
        var expectedPath = Path.Combine(staging, "annotations", "annotations.json");
        File.Exists(expectedPath).Should().BeTrue();

        // Verify BundleAnnotationStore can read back what we wrote
        var bundleStore = new BundleAnnotationStore(staging);
        var loaded = await bundleStore.ListAsync(new AnnotationFilter { SessionId = "sess-1", Limit = 100 },
            CancellationToken.None);
        loaded.Should().HaveCount(1);
        loaded[0].Body.Should().Be("hello");
    }

    // ─── SC-5 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AggregationStage_AnnotationsExported_EnumValueExists()
    {
        Enum.IsDefined(typeof(AggregationStage), AggregationStage.AnnotationsExported)
            .Should().BeTrue();
    }

    // ─── SC-6 ─────────────────────────────────────────────────────────────────
    // Test that ExportAsync (the method actually called by the orchestrator) calls
    // ExportAllForSessionAsync on the provided store.

    [Fact]
    public async Task ExportAsync_CallsExportAllForSessionAsync()
    {
        var spy = new SpyAnnotationStore();
        var staging = NewBundleStagingDir();
        Directory.CreateDirectory(staging);

        await AnnotationsExporter.ExportAsync(spy, "sess-test", staging, CancellationToken.None);

        spy.ExportCallCount.Should().Be(1);
        spy.LastExportedSessionId.Should().Be("sess-test");
    }

    // ─── SC-7 ─────────────────────────────────────────────────────────────────
    // Orchestrator constructed without annotation store must not report
    // AnnotationsExported stage and must not throw annotation-related errors.

    [Fact]
    public async Task AggregationOrchestrator_WithoutAnnotationStore_SkipsExport()
    {
        var nasRoot = Path.Combine(_tempDir, "empty-nas");
        Directory.CreateDirectory(nasRoot);
        var outputDir = Path.Combine(_tempDir, "output");

        var reader = new LocalFileSystemStorageReader(nasRoot);
        // No annotation store passed
        var orchestrator = new AggregationOrchestrator(reader);
        var reporter = new StageCollector();

        // Will throw "No intervals found" because NAS is empty — that's expected
        var ex = await Record.ExceptionAsync(() => orchestrator.RunAsync(
            new AggregationRequest
            {
                OutputPath = outputDir,
                TimeRange  = new Tracer.Core.Time.TimeRange(
                    Tracer.Core.Time.WallclockTime.FromDateTimeOffset(DateTimeOffset.UnixEpoch),
                    Tracer.Core.Time.WallclockTime.FromDateTimeOffset(DateTimeOffset.UnixEpoch.AddHours(1))),
            },
            reporter));

        // AnnotationsExported must never have been reported
        reporter.Stages.Should().NotContain(AggregationStage.AnnotationsExported);
        // The exception should be InvalidOperationException about no intervals, not annotation-related
        ex.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("No intervals found");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private sealed class SpyAnnotationStore : IAnnotationStore
    {
        public int ExportCallCount { get; private set; }
        public string? LastExportedSessionId { get; private set; }

        public Task<IReadOnlyList<AnnotationRecord>> ListAsync(AnnotationFilter f, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AnnotationRecord>>(Array.Empty<AnnotationRecord>());

        public Task<AnnotationRecord?> GetAsync(string id, CancellationToken ct)
            => Task.FromResult<AnnotationRecord?>(null);

        public Task<AnnotationRecord> CreateAsync(AnnotationRecord r, CancellationToken ct)
            => Task.FromResult(r);

        public Task<AnnotationRecord?> UpdateAsync(AnnotationRecord r, CancellationToken ct)
            => Task.FromResult<AnnotationRecord?>(r);

        public Task<bool> DeleteAsync(string id, CancellationToken ct)
            => Task.FromResult(true);

        public Task<IReadOnlyList<AnnotationRecord>> ExportAllForSessionAsync(string sessionId, CancellationToken ct)
        {
            ExportCallCount++;
            LastExportedSessionId = sessionId;
            return Task.FromResult<IReadOnlyList<AnnotationRecord>>(Array.Empty<AnnotationRecord>());
        }
    }

    private sealed class StageCollector : IAggregationProgressReporter
    {
        public List<AggregationStage> Stages { get; } = new();
        public void Report(AggregationStage stage, string? message = null) => Stages.Add(stage);
    }
}
