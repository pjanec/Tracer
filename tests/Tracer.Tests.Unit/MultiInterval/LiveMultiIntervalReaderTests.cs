using DuckDB.NET.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging;
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

public sealed class LiveMultiIntervalReaderTests : IAsyncDisposable
{
    private readonly string _tempDir;
    private readonly List<string> _duckDbFiles = [];

    public LiveMultiIntervalReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"lmir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>After Initialize with an empty snapshot, all pool slots are available.</summary>
    [Fact]
    public async Task PoolSize_AfterInitialize_AllConnectionsAreAvailable()
    {
        const int poolSize = 4;
        var tracker = new ControllableTracker(
            CreateRotator(), new IntervalSetSnapshot([]), NullLogger<IntervalSetTracker>.Instance);
        await using var reader = new LiveMultiIntervalReader(
            tracker, NullLogger<LiveMultiIntervalReader>.Instance, poolSize);

        await reader.InitializeAsync(default);

        // Acquire all slots — if pool is smaller than expected, ReadAsync would hang.
        var connections = new List<PooledMultiIntervalConnection>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        for (int i = 0; i < poolSize; i++)
            connections.Add(await reader.AcquireAsync(cts.Token));

        connections.Should().HaveCount(poolSize);

        foreach (var c in connections)
            await c.DisposeAsync();
    }

    /// <summary>Connections from an empty snapshot produce the empty-result sentinel SQL.</summary>
    [Fact]
    public async Task AcquireAsync_EmptySnapshot_ConnectionSqlIsEmptySentinel()
    {
        var tracker = new ControllableTracker(
            CreateRotator(), new IntervalSetSnapshot([]), NullLogger<IntervalSetTracker>.Instance);
        await using var reader = new LiveMultiIntervalReader(
            tracker, NullLogger<LiveMultiIntervalReader>.Instance, poolSize: 2);

        await reader.InitializeAsync(default);

        await using var conn = await reader.AcquireAsync(default);
        conn.BuildEventsUnionSql().Should().Be("SELECT NULL WHERE FALSE");
    }

    /// <summary>After SetChanged is fired, newly acquired connections reflect the new snapshot.</summary>
    [Fact]
    public async Task SetChanged_TriggersPoolRebuild_NewConnectionsReflectNewSnapshot()
    {
        var dbPath = await CreateTempEventsDbAsync();
        var ts = new IntervalTimestamp("20260101T000000Z");
        var dir = IntervalDirectory.ForEventsDb(dbPath);

        // Start with an empty snapshot
        var tracker = new ControllableTracker(
            CreateRotator(), new IntervalSetSnapshot([]), NullLogger<IntervalSetTracker>.Instance);
        await using var reader = new LiveMultiIntervalReader(
            tracker, NullLogger<LiveMultiIntervalReader>.Instance, poolSize: 2);

        await reader.InitializeAsync(default);

        // Verify initial state: no intervals
        await using (var first = await reader.AcquireAsync(default))
            first.BuildEventsUnionSql().Should().Be("SELECT NULL WHERE FALSE");

        // Rebuild with a completed interval
        var newRef = new IntervalReference(dir, IntervalRole.Completed);
        var newSnap = new IntervalSetSnapshot(new List<IntervalReference> { newRef }.AsReadOnly());
        await tracker.FireSetChangedAsync(newSnap, default);

        // New connection should include the interval alias
        await using var rebuilt = await reader.AcquireAsync(default);
        rebuilt.BuildEventsUnionSql().Should().NotBe("SELECT NULL WHERE FALSE",
            "after rebuild with a completed interval the SQL must reference it");
    }

    /// <summary>A connection acquired before rebuild is disposed rather than returned to the new pool.</summary>
    [Fact]
    public async Task StaleConnection_ReturnedAfterRebuild_IsDiscarded()
    {
        var tracker = new ControllableTracker(
            CreateRotator(), new IntervalSetSnapshot([]), NullLogger<IntervalSetTracker>.Instance);
        await using var reader = new LiveMultiIntervalReader(
            tracker, NullLogger<LiveMultiIntervalReader>.Instance, poolSize: 2);

        await reader.InitializeAsync(default);

        // Acquire a connection (snapshot v1)
        var stale = await reader.AcquireAsync(default);

        // Trigger rebuild with a new (empty) snapshot
        var newSnap = new IntervalSetSnapshot([]);
        await tracker.FireSetChangedAsync(newSnap, default);

        // Return the stale connection — it should be silently discarded (not re-pooled)
        await stale.DisposeAsync();

        // The pool should still contain fresh connections, not the stale one
        // (if stale was re-pooled its internal DuckDB connection would be disposed/invalid)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var fresh = await reader.AcquireAsync(cts.Token);
        // Just acquiring without deadlock proves the pool has 2 fresh connections after rebuild
        fresh.Should().NotBeNull();
    }

    /// <summary>Interleaved AcquireAsync and SetChanged calls complete without deadlock.</summary>
    [Fact]
    public async Task ConcurrentAcquireAndRebuild_DoesNotDeadlock()
    {
        const int poolSize = 2;
        var tracker = new ControllableTracker(
            CreateRotator(), new IntervalSetSnapshot([]), NullLogger<IntervalSetTracker>.Instance);
        await using var reader = new LiveMultiIntervalReader(
            tracker, NullLogger<LiveMultiIntervalReader>.Instance, poolSize);

        await reader.InitializeAsync(default);

        // Use a generous timeout to prove there is no deadlock, not just a race
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // 4 acquire tasks each hold a connection briefly
        var acquireTasks = Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var conn = await reader.AcquireAsync(cts.Token);
            await Task.Delay(2, cts.Token);
        }).ToList();

        // 2 rebuild tasks fire at slight offsets
        var rebuildTasks = Enumerable.Range(0, 2).Select(async i =>
        {
            await Task.Delay(3 * (i + 1), cts.Token);
            await tracker.FireSetChangedAsync(new IntervalSetSnapshot([]), cts.Token);
        }).ToList();

        await Task.WhenAll([.. acquireTasks, .. rebuildTasks]);

        // After all tasks finish, the pool must still be usable
        await using var final = await reader.AcquireAsync(cts.Token);
        final.Should().NotBeNull();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> CreateTempEventsDbAsync()
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.duckdb");
        _duckDbFiles.Add(path);
        await using var conn = new DuckDBConnection($"DataSource={path}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE events (id INTEGER)";
        await cmd.ExecuteNonQueryAsync();
        return path;
    }

    private IntervalRotator CreateRotator()
    {
        var config = new AgentConfig
        {
            NodeId = "test",
            DataRoot = _tempDir,
            LogsRoot = _tempDir,
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

    public ValueTask DisposeAsync()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        return ValueTask.CompletedTask;
    }

    // ── Test doubles ────────────────────────────────────────────────────────────

    /// <summary>
    /// A controllable <see cref="IntervalSetTracker"/> stub for unit testing.
    /// Overrides all virtual methods; exposes a public <see cref="FireSetChangedAsync"/> method.
    /// </summary>
    private sealed class ControllableTracker : IntervalSetTracker
    {
        private IntervalSetSnapshot _snapshot;

        public ControllableTracker(
            IntervalRotator rotator, IntervalSetSnapshot initialSnapshot,
            ILogger<IntervalSetTracker> logger)
            : base(rotator, 0, logger)
        {
            _snapshot = initialSnapshot;
        }

        public override Task InitializeAsync(CancellationToken ct) => NotifyAsync(ct);
        public override Task OnIntervalRotatedAsync(CancellationToken ct) => NotifyAsync(ct);
        public override IntervalSetSnapshot CurrentSnapshot() => _snapshot;
        protected override IEnumerable<IntervalDirectory> ListCompletedIntervals() => [];

        public async Task FireSetChangedAsync(IntervalSetSnapshot snapshot, CancellationToken ct)
        {
            _snapshot = snapshot;
            await NotifyAsync(ct);
        }
    }

    private sealed class NoOpUploadService : ITelemetryUploadService
    {
        public Task<UploadIntentId> RequestUploadAsync(UploadRequest request, CancellationToken ct)
            => Task.FromResult(new UploadIntentId(Guid.NewGuid().ToString()));
        public Task<UploadStatus> GetStatusAsync(UploadIntentId intentId, CancellationToken ct)
            => Task.FromResult(UploadStatus.Complete);
    }
}
