using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Agent.Configuration;
using Tracer.Agent.Lifecycle;
using Tracer.Agent.Storage;
using Tracer.Agent.Time;
using Tracer.Agent.Upload;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB.MultiInterval;
using Xunit;

namespace Tracer.Tests.Unit.MultiInterval;

public sealed class IntervalSetTrackerTests : IAsyncDisposable
{
    private readonly string _tempDir;

    public IntervalSetTrackerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ist-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task InitializeAsync_NoCompletedIntervals_SnapshotContainsOnlyActive()
    {
        await using var rotator = CreateRotator(_tempDir);
        await rotator.OpenCurrentAsync(default);

        var tracker = new IntervalSetTracker(rotator, 3, NullLogger<IntervalSetTracker>.Instance);
        await tracker.InitializeAsync(default);

        var snap = tracker.CurrentSnapshot();
        snap.Intervals.Should().HaveCount(1);
        snap.Active.Should().NotBeNull();
        snap.Completed.Should().BeEmpty();
    }

    [Fact]
    public async Task InitializeAsync_FiveCompleted_CapThree_SnapshotContainsThreeNewestPlusActive()
    {
        await using var rotator = CreateRotator(_tempDir);
        await rotator.OpenCurrentAsync(default);

        // Create 5 completed interval directories with _ready sentinel
        var timestamps = new[]
        {
            "20260101T000000Z",
            "20260101T010000Z",
            "20260101T020000Z",
            "20260101T030000Z",
            "20260101T040000Z",
        };
        foreach (var ts in timestamps)
        {
            var dir = new IntervalDirectory(_tempDir, new IntervalTimestamp(ts));
            dir.EnsureCreated();
            dir.WriteReadySentinel();
        }

        var tracker = new IntervalSetTracker(rotator, 3, NullLogger<IntervalSetTracker>.Instance);
        await tracker.InitializeAsync(default);

        var snap = tracker.CurrentSnapshot();
        snap.Intervals.Should().HaveCount(4, "3 completed + 1 active");
        snap.Completed.Count().Should().Be(3);
        snap.Active.Should().NotBeNull();

        var completedTs = snap.Completed.Select(c => c.Directory.Timestamp.Value).ToHashSet();
        completedTs.Should().Contain("20260101T040000Z");
        completedTs.Should().Contain("20260101T030000Z");
        completedTs.Should().Contain("20260101T020000Z");
        completedTs.Should().NotContain("20260101T000000Z");
        completedTs.Should().NotContain("20260101T010000Z");
    }

    [Fact]
    public async Task OnIntervalRotatedAsync_PreviousActiveBecomesCompleted()
    {
        await using var rotator = CreateRotator(_tempDir);
        await rotator.OpenCurrentAsync(default);

        var tracker = new IntervalSetTracker(rotator, 3, NullLogger<IntervalSetTracker>.Instance);
        await tracker.InitializeAsync(default);

        var originalActiveTs = rotator.CurrentDirectory!.Timestamp.Value;

        // Rotate so the current active becomes completed
        await rotator.RotateAsync(ManifestFinalizationReason.ScheduledRotation, default);
        await tracker.OnIntervalRotatedAsync(default);

        var snap = tracker.CurrentSnapshot();
        var completedValues = snap.Completed.Select(c => c.Directory.Timestamp.Value).ToList();
        completedValues.Should().Contain(originalActiveTs,
            "the previously-active interval should now be Completed");
        snap.Active.Should().NotBeNull("a new active interval must exist after rotation");
        snap.Active!.Directory.Timestamp.Value.Should().NotBe(originalActiveTs);
    }

    [Fact]
    public async Task OnIntervalEvictedAsync_RemovesEvictedIntervalFromSnapshot()
    {
        await using var rotator = CreateRotator(_tempDir);
        await rotator.OpenCurrentAsync(default);

        // Create one completed interval
        var completedTs = "20260101T000000Z";
        var completedDir = new IntervalDirectory(_tempDir, new IntervalTimestamp(completedTs));
        completedDir.EnsureCreated();
        completedDir.WriteReadySentinel();

        var tracker = new IntervalSetTracker(rotator, 3, NullLogger<IntervalSetTracker>.Instance);
        await tracker.InitializeAsync(default);

        tracker.CurrentSnapshot().Completed.Should().ContainSingle();

        await tracker.OnIntervalEvictedAsync(completedDir, default);

        tracker.CurrentSnapshot().Completed.Should().BeEmpty();
    }

    [Fact]
    public async Task SetChanged_FiredAfterInitialize()
    {
        await using var rotator = CreateRotator(_tempDir);
        await rotator.OpenCurrentAsync(default);

        var tracker = new IntervalSetTracker(rotator, 3, NullLogger<IntervalSetTracker>.Instance);

        int fired = 0;
        tracker.SetChanged += (_, _) => { fired++; return Task.CompletedTask; };

        await tracker.InitializeAsync(default);

        fired.Should().Be(1);
    }

    [Fact]
    public async Task SetChanged_FiredAfterRotation()
    {
        await using var rotator = CreateRotator(_tempDir);
        await rotator.OpenCurrentAsync(default);

        var tracker = new IntervalSetTracker(rotator, 3, NullLogger<IntervalSetTracker>.Instance);
        await tracker.InitializeAsync(default);

        int fired = 0;
        tracker.SetChanged += (_, _) => { fired++; return Task.CompletedTask; };

        await rotator.RotateAsync(ManifestFinalizationReason.ScheduledRotation, default);
        await tracker.OnIntervalRotatedAsync(default);

        fired.Should().Be(1);
    }

    [Fact]
    public async Task SetChanged_NotFiredIfEvictionTargetNotInSet()
    {
        await using var rotator = CreateRotator(_tempDir);
        await rotator.OpenCurrentAsync(default);

        var tracker = new IntervalSetTracker(rotator, 3, NullLogger<IntervalSetTracker>.Instance);
        await tracker.InitializeAsync(default);

        int fired = 0;
        tracker.SetChanged += (_, _) => { fired++; return Task.CompletedTask; };

        // Evict a directory that was never in the snapshot
        var notInSet = new IntervalDirectory(_tempDir, new IntervalTimestamp("20250101T000000Z"));
        await tracker.OnIntervalEvictedAsync(notInSet, default);

        fired.Should().Be(0, "SetChanged must not fire for an interval not in the set");
    }

    public ValueTask DisposeAsync()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        return ValueTask.CompletedTask;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IntervalRotator CreateRotator(string dataRoot)
    {
        var config = new AgentConfig
        {
            NodeId = "test",
            DataRoot = dataRoot,
            LogsRoot = dataRoot,
            IntervalDuration = TimeSpan.FromHours(1),
            KeepLastNIntervals = 4,
            DiskWatermarkPercent = 10,
        };
        var clock = new SystemClock();
        var scheduler = new IntervalScheduler(clock, config);
        var upload = new NoOpUploadService();
        var dispatcher = new UploadIntentDispatcher(upload, NullLogger<UploadIntentDispatcher>.Instance);
        return new IntervalRotator(scheduler, config, dispatcher, clock,
            NullLogger<IntervalRotator>.Instance);
    }

    private sealed class NoOpUploadService : ITelemetryUploadService
    {
        public Task<UploadIntentId> RequestUploadAsync(UploadRequest req, CancellationToken ct)
            => Task.FromResult(new UploadIntentId(Guid.NewGuid().ToString()));
        public Task<UploadStatus> GetStatusAsync(UploadIntentId id, CancellationToken ct)
            => Task.FromResult(UploadStatus.Complete);
    }
}
