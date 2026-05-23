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

public sealed class PooledMultiIntervalConnectionSlowStateTests : IAsyncDisposable
{
    private readonly string _tempDir;

    public PooledMultiIntervalConnectionSlowStateTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pmic-ss-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildSlowStateUnionSql_WithSlowStateAliases_UsesSlowStateAliases()
    {
        // Arrange: create a minimal in-memory connection to back PooledMultiIntervalConnection
        await using var duckConn = new DuckDBConnection("DataSource=:memory:");
        await duckConn.OpenAsync();

        var tracker = new ControllableTracker(
            CreateRotator(), new IntervalSetSnapshot([]), NullLogger<IntervalSetTracker>.Instance);
        await using var reader = new LiveMultiIntervalReader(
            tracker, NullLogger<LiveMultiIntervalReader>.Instance, poolSize: 1);
        await reader.InitializeAsync(default);

        IReadOnlyList<string> aliases = new[] { "iv_aaa" };
        IReadOnlyList<string> slowStateAliases = new[] { "ss_aaa" };

        var conn = new PooledMultiIntervalConnection(
            reader,
            duckConn,
            manager: null,
            aliases: aliases,
            slowStateAliases: slowStateAliases,
            issuingSnapshot: null,
            hasActive: false);

        // Act
        var sql = conn.BuildSlowStateUnionSql();

        // Assert
        sql.Should().Contain("ss_aaa.slow_state");
        sql.Should().NotContain("iv_aaa.slow_state");
    }

    [Fact]
    public async Task BuildSlowStateUnionSql_WithNullSlowStateAliases_FallsBackToAliases()
    {
        await using var duckConn = new DuckDBConnection("DataSource=:memory:");
        await duckConn.OpenAsync();

        var tracker = new ControllableTracker(
            CreateRotator(), new IntervalSetSnapshot([]), NullLogger<IntervalSetTracker>.Instance);
        await using var reader = new LiveMultiIntervalReader(
            tracker, NullLogger<LiveMultiIntervalReader>.Instance, poolSize: 1);
        await reader.InitializeAsync(default);

        IReadOnlyList<string> aliases = new[] { "iv_aaa" };

        var conn = new PooledMultiIntervalConnection(
            reader,
            duckConn,
            manager: null,
            aliases: aliases,
            slowStateAliases: null,
            issuingSnapshot: null,
            hasActive: false);

        // Act
        var sql = conn.BuildSlowStateUnionSql();

        // Assert
        sql.Should().Contain("iv_aaa.slow_state");
    }

    [Fact]
    public async Task BuildMemoryConnectionAsync_WhenSlowStateFileExists_AttachesSeparately()
    {
        // Arrange: create a real events.duckdb and slow_state.duckdb in the same temp dir
        var bundleDir = Path.Combine(_tempDir, "bundle");
        Directory.CreateDirectory(bundleDir);

        var eventsDbPath = Path.Combine(bundleDir, "events.duckdb");
        var slowStateDbPath = Path.Combine(bundleDir, "slow_state.duckdb");

        await CreateMinimalDuckDbAsync(eventsDbPath, "CREATE TABLE events (id INTEGER)");
        await CreateMinimalDuckDbAsync(slowStateDbPath, "CREATE TABLE slow_state (id INTEGER)");

        // IntervalDirectory.ForEventsDb automatically sets SlowStateDbPath to
        // Path.Combine(containingDir, "slow_state.duckdb")
        var dir = IntervalDirectory.ForEventsDb(eventsDbPath);
        dir.SlowStateDbPath.Should().Be(slowStateDbPath, "ForEventsDb should derive slow_state path");

        var ivRef = new IntervalReference(dir, IntervalRole.Completed);
        var snapshot = new IntervalSetSnapshot(new List<IntervalReference> { ivRef }.AsReadOnly());

        var tracker = new ControllableTracker(
            CreateRotator(), snapshot, NullLogger<IntervalSetTracker>.Instance);
        await using var reader = new LiveMultiIntervalReader(
            tracker, NullLogger<LiveMultiIntervalReader>.Instance, poolSize: 1);
        await reader.InitializeAsync(default);

        // Act
        await using var conn = await reader.AcquireAsync(default);
        var sql = conn.BuildSlowStateUnionSql();

        // Assert: SQL should reference the ss_-prefixed alias, not the iv_-prefixed one
        sql.Should().Contain("ss_", "slow_state should use the separately-attached ss_ alias");
        sql.Should().NotContain("FROM iv_",
            "iv_ alias belongs to events.duckdb which should not be used for slow_state");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    private static async Task CreateMinimalDuckDbAsync(string path, string ddl)
    {
        await using var conn = new DuckDBConnection($"DataSource={path}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = ddl;
        await cmd.ExecuteNonQueryAsync();
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
        var clock = new SystemClock(TimeProvider.System);
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

    // ── Test doubles ─────────────────────────────────────────────────────────────

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
    }

    private sealed class NoOpUploadService : ITelemetryUploadService
    {
        public Task<UploadIntentId> RequestUploadAsync(UploadRequest request, CancellationToken ct)
            => Task.FromResult(new UploadIntentId(Guid.NewGuid().ToString()));
        public Task<UploadStatus> GetStatusAsync(UploadIntentId intentId, CancellationToken ct)
            => Task.FromResult(UploadStatus.Complete);
    }
}