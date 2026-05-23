using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Adapters.Mock.Storage;
using Tracer.Agent.Storage;
using Tracer.Aggregator;
using Tracer.Aggregator.Configuration;
using Tracer.Aggregator.Consolidation;
using Tracer.Aggregator.Progress;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB;
using Tracer.Storage.SavedViews;
using Xunit;

namespace Tracer.Tests.Unit.Aggregator;

public sealed class SavedViewsExporterTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        _tempDirs.Add(d);
        return d;
    }

    public void Dispose()
    {
        foreach (var d in _tempDirs)
        {
            try { if (Directory.Exists(d)) Directory.Delete(d, recursive: true); }
            catch { }
        }
    }

    // ── Tests ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_WhenStoreHasViews_WritesJsonFile()
    {
        var staging = TempDir();
        var sessionId = "session-abc";

        var views = new List<SavedViewRecord>
        {
            new SavedViewRecord
            {
                SavedViewId = "view-1",
                SessionId = sessionId,
                Kind = SavedViewKind.SavedView,
                ViewType = "timeline",
                Url = "/timeline?session=abc",
                Label = "My Timeline",
                Persona = "analyst",
                CreatedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                OpenCount = 3,
            },
            new SavedViewRecord
            {
                SavedViewId = "view-2",
                SessionId = sessionId,
                Kind = SavedViewKind.Bookmark,
                ViewType = "events",
                Url = "/events?session=abc&t=100",
                Label = "Bookmark 1",
                Persona = "developer",
                CreatedAtUtc = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                OpenCount = 1,
            },
        };

        var store = new FakeSavedViewStore(sessionId, views);

        await SavedViewsExporter.ExportAsync(store, sessionId, staging, CancellationToken.None);

        var expectedPath = Path.Combine(staging, "saved_views", "saved_views.json");
        File.Exists(expectedPath).Should().BeTrue("saved_views.json should be created");

        var json = await File.ReadAllTextAsync(expectedPath);
        json.Should().Contain("\"savedViewId\"", "JSON should use camelCase");
        json.Should().Contain("view-1");
        json.Should().Contain("view-2");
        json.Should().Contain("savedView", "enum values should be camelCase strings");
    }

    [Fact]
    public async Task ExportAsync_WhenStoreIsEmpty_DoesNotCreateFile()
    {
        var staging = TempDir();
        var store = new FakeSavedViewStore("session-xyz", new List<SavedViewRecord>());

        await SavedViewsExporter.ExportAsync(store, "session-xyz", staging, CancellationToken.None);

        var expectedPath = Path.Combine(staging, "saved_views", "saved_views.json");
        File.Exists(expectedPath).Should().BeFalse("no file should be created when store is empty");
    }

    [Fact]
    public async Task AggregationOrchestrator_WithSavedViewStore_FiresSavedViewsExportedStage()
    {
        var nasRoot = TempDir();
        var nodeId = "test-node-sv";
        var start = new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(1);
        var sessionId = "sess-saved-views";

        await CreateMinimalNasZipAsync(nasRoot, nodeId, start, end, sessionId);

        var outputDir = Path.Combine(TempDir(), "output-bundle");
        var reader = new LocalFileSystemStorageReader(nasRoot);

        var view = new SavedViewRecord
        {
            SavedViewId = "v1",
            SessionId = sessionId,
            Kind = SavedViewKind.SavedView,
            ViewType = "timeline",
            Url = "/timeline",
            Label = "Test",
            Persona = "dev",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            OpenCount = 0,
        };
        var savedViewStore = new FakeSavedViewStore(sessionId, new List<SavedViewRecord> { view });

        var orchestrator = new AggregationOrchestrator(
            reader,
            NullLogger<AggregationOrchestrator>.Instance,
            annotationStore: null,
            savedViewStore: savedViewStore);

        var stages = new List<AggregationStage>();
        var reporter = new LambdaProgressReporter((s, _) => stages.Add(s));

        var request = new AggregationRequest
        {
            OutputPath = outputDir,
            SessionId = sessionId,
            TimeRange = new Tracer.Core.Time.TimeRange(
                WallclockTime.FromDateTimeOffset(start.AddMinutes(-1)),
                WallclockTime.FromDateTimeOffset(end.AddMinutes(1))),
        };

        await orchestrator.RunAsync(request, reporter);

        stages.Should().Contain(AggregationStage.SavedViewsExported);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    private async Task CreateMinimalNasZipAsync(
        string nasRoot, string nodeId, DateTimeOffset start, DateTimeOffset end, string sessionId)
    {
        var ts = IntervalTimestamp.FromUtc(start);
        var nodeDir = Path.Combine(nasRoot, nodeId);
        Directory.CreateDirectory(nodeDir);

        var manifest = new IntervalManifest
        {
            IntervalStart = ts,
            IntervalEnd = IntervalTimestamp.FromUtc(end),
            NodeId = new AgentId(nodeId),
            TracerVersion = "1.0.0",
            SchemaVersion = 1,
            EventCount = 0,
            SlowStateCount = 0,
            FastStateTopics = Array.Empty<string>(),
            CaptureGaps = Array.Empty<CaptureGap>(),
            SessionMarkers = Array.Empty<SessionMarker>(),
            FinalizedAt = WallclockTime.FromDateTimeOffset(end),
            FinalizationReason = ManifestFinalizationReason.ScheduledRotation,
        };

        var staging = TempDir();
        var manifestPath = Path.Combine(staging, "manifest.json");
        await ManifestWriter.WriteAsync(manifestPath, manifest, CancellationToken.None);

        {
            await using var writer = await DuckDbStorageWriter.CreateAsync(
                staging,
                new Dictionary<string, Tracer.Storage.DuckDB.Parquet.ParquetTopicSchema>(),
                NullLogger<DuckDbStorageWriter>.Instance);
            await writer.FlushAsync();
        }

        var zipPath = Path.Combine(nodeDir, $"{ts.Value}.zip");
        ZipFile.CreateFromDirectory(staging, zipPath);
    }

    private sealed class LambdaProgressReporter : IAggregationProgressReporter
    {
        private readonly Action<AggregationStage, string?> _action;
        public LambdaProgressReporter(Action<AggregationStage, string?> action) => _action = action;
        public void Report(AggregationStage stage, string? message = null) => _action(stage, message);
    }

    private sealed class FakeSavedViewStore : ISavedViewStore
    {
        private readonly string _sessionId;
        private readonly IReadOnlyList<SavedViewRecord> _views;

        public FakeSavedViewStore(string sessionId, IReadOnlyList<SavedViewRecord> views)
        {
            _sessionId = sessionId;
            _views = views;
        }

        public Task<IReadOnlyList<SavedViewRecord>> ListAsync(SavedViewFilter filter, CancellationToken ct)
        {
            IReadOnlyList<SavedViewRecord> result = filter.SessionId == _sessionId
                ? _views
                : Array.Empty<SavedViewRecord>();
            return Task.FromResult(result);
        }

        public Task<SavedViewRecord?> GetAsync(string id, CancellationToken ct)
            => Task.FromResult<SavedViewRecord?>(null);

        public Task<SavedViewRecord> CreateAsync(SavedViewRecord record, CancellationToken ct)
            => Task.FromResult(record);

        public Task<SavedViewRecord?> UpdateAsync(SavedViewRecord record, CancellationToken ct)
            => Task.FromResult<SavedViewRecord?>(null);

        public Task<bool> DeleteAsync(string id, CancellationToken ct)
            => Task.FromResult(false);

        public Task RecordOpenedAsync(string id, CancellationToken ct)
            => Task.CompletedTask;
    }
}
