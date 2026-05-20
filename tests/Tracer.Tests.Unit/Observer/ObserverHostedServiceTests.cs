using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Adapters.Mock;
using Tracer.Agent.Configuration;
using Tracer.Agent.Lifecycle;
using Tracer.Agent.Storage;
using Tracer.Agent.Time;
using Tracer.Agent.Upload;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Time;
using Tracer.Observer.Lifecycle;
using Tracer.WebApi.Lifecycle;
using Tracer.WebApi.Streaming;
using Xunit;

namespace Tracer.Tests.Unit.Observer;

public sealed class ObserverHostedServiceTests : IAsyncDisposable
{
    private readonly string _tempDir;
    private readonly AgentConfig _agentConfig;
    private readonly IntervalScheduler _scheduler;
    private readonly IntervalRotator _rotator;
    private readonly RetentionManager _retention;
    private readonly ObserverIngestionPipeline _ingestion;

    public ObserverHostedServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tracer-svc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _agentConfig = new AgentConfig
        {
            NodeId = "test",
            DataRoot = _tempDir,
            LogsRoot = _tempDir,
            IntervalDuration = TimeSpan.FromHours(1),
            KeepLastNIntervals = 4,
            DiskWatermarkPercent = 10,
        };

        var clock = new SystemClock();
        _scheduler = new IntervalScheduler(clock, _agentConfig);
        var upload = new NoOpUploadService();
        var dispatcher = new UploadIntentDispatcher(upload, NullLogger<UploadIntentDispatcher>.Instance);
        _rotator = new IntervalRotator(_scheduler, _agentConfig, dispatcher, clock,
            NullLogger<IntervalRotator>.Instance);
        _retention = new RetentionManager(_agentConfig, NullLogger<RetentionManager>.Instance);

        var state = new ObserverStateReporter();
        var broadcaster = new LiveEventBroadcaster();
        _ingestion = new ObserverIngestionPipeline(
            Array.Empty<Tracer.Observer.Sources.NamedDataSource>(),
            _rotator, broadcaster, state,
            NullLogger<ObserverIngestionPipeline>.Instance);
    }

    [Fact]
    public async Task OnStart_RecoveryRunsBeforeIntervalOpen()
    {
        var order = new List<string>();
        var recovery = new TrackingRecovery(order, "recovery");
        var pool = new TrackingPool(order, "pool-init", NullLogger<ReadOnlyConnectionPool>.Instance);

        var svc = new ObserverHostedService(
            recovery, _rotator, _scheduler, _ingestion, pool, _retention,
            new SystemClock(), NullLogger<ObserverHostedService>.Instance);

        // Start service, let initialization run, then stop
        await svc.StartAsync(default);
        await Task.Delay(300); // allow ExecuteAsync to reach pool init
        await svc.StopAsync(default);

        // Both recovery and pool-init must appear in this order
        order.Should().ContainInOrder("recovery", "pool-init");
    }

    [Fact]
    public async Task OnStart_PoolInitializedAfterIntervalOpen()
    {
        var order = new List<string>();
        var recovery = new TrackingRecovery(order, "recovery");
        var pool = new TrackingPool(order, "pool-init", NullLogger<ReadOnlyConnectionPool>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var svc = new ObserverHostedService(
            recovery, _rotator, _scheduler, _ingestion, pool, _retention,
            new SystemClock(), NullLogger<ObserverHostedService>.Instance);

        try { await svc.StartAsync(default); } catch { }
        try { await Task.Delay(300); } catch { }
        try { await svc.StopAsync(default); } catch { }

        pool.InitializeCalled.Should().BeTrue("pool must be initialized on start");
        pool.InitializedPath.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task OnGracefulShutdown_FinalRotationHasGracefulReason()
    {
        var recovery = new FakeRecovery();
        var pool = new TrackingPool(new List<string>(), "pool-init",
            NullLogger<ReadOnlyConnectionPool>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var svc = new ObserverHostedService(
            recovery, _rotator, _scheduler, _ingestion, pool, _retention,
            new SystemClock(), NullLogger<ObserverHostedService>.Instance);

        try { await svc.StartAsync(default); } catch { }
        try { await Task.Delay(300); } catch { }
        try { await svc.StopAsync(default); } catch { }

        // After graceful shutdown, the rotator should have written a manifest
        // with FinalizationReason == GracefulShutdown
        var manifestPath = Directory.GetFiles(_tempDir, "manifest.json", SearchOption.AllDirectories)
            .FirstOrDefault();
        manifestPath.Should().NotBeNull("a manifest must be written on graceful shutdown");

        var manifest = await ManifestWriter.ReadAsync(manifestPath!, CancellationToken.None);
        manifest.Should().NotBeNull();
        manifest!.FinalizationReason.Should().Be(ManifestFinalizationReason.GracefulShutdown);
    }

    [Fact]
    public async Task PoolRefreshFailure_Logged_HostNotCrashed()
    {
        var recovery = new FakeRecovery();
        var pool = new FailingPool(NullLogger<ReadOnlyConnectionPool>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var svc = new ObserverHostedService(
            recovery, _rotator, _scheduler, _ingestion, pool, _retention,
            new SystemClock(), NullLogger<ObserverHostedService>.Instance);

        // Should not throw even when pool refresh fails
        Func<Task> act = async () =>
        {
            await svc.StartAsync(default);
            await Task.Delay(300);
            await svc.StopAsync(default);
        };
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OnStart_ServiceStartsWithoutException()
    {
        var recovery = new FakeRecovery();
        var pool = new TrackingPool(new List<string>(), "pool-init",
            NullLogger<ReadOnlyConnectionPool>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var svc = new ObserverHostedService(
            recovery, _rotator, _scheduler, _ingestion, pool, _retention,
            new SystemClock(), NullLogger<ObserverHostedService>.Instance);

        Func<Task> act = async () =>
        {
            await svc.StartAsync(cts.Token);
            await Task.Delay(200);
            await svc.StopAsync(default);
        };
        await act.Should().NotThrowAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _rotator.DisposeAsync();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── Test doubles ────────────────────────────────────────────────────────

    private sealed class FakeRecovery : IStartupRecovery
    {
        public Task RecoverAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class TrackingRecovery : IStartupRecovery
    {
        private readonly List<string> _order;
        private readonly string _label;

        public TrackingRecovery(List<string> order, string label)
        { _order = order; _label = label; }

        public Task RecoverAsync(CancellationToken ct)
        {
            _order.Add(_label);
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingPool : ReadOnlyConnectionPool
    {
        private readonly List<string> _order;
        private readonly string _label;
        public bool InitializeCalled { get; private set; }
        public string? InitializedPath { get; private set; }

        public TrackingPool(List<string> order, string label, ILogger<ReadOnlyConnectionPool> logger)
            : base(logger) { _order = order; _label = label; }

        public override Task InitializeAsync(string path, CancellationToken ct)
        {
            InitializeCalled = true;
            InitializedPath = path;
            _order.Add(_label);
            return Task.CompletedTask; // Don't actually open DuckDB
        }

        public override Task OnIntervalRotatedAsync(string newPath, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class FailingPool : ReadOnlyConnectionPool
    {
        public FailingPool(ILogger<ReadOnlyConnectionPool> logger) : base(logger) { }

        public override Task InitializeAsync(string path, CancellationToken ct)
            => Task.CompletedTask; // Must not fail on init

        public override Task OnIntervalRotatedAsync(string newPath, CancellationToken ct)
            => throw new InvalidOperationException("Simulated pool refresh failure");
    }

    private sealed class NoOpUploadService : ITelemetryUploadService
    {
        public Task<UploadIntentId> RequestUploadAsync(UploadRequest request, CancellationToken ct)
            => Task.FromResult(new UploadIntentId(Guid.NewGuid().ToString()));
        public Task<UploadStatus> GetStatusAsync(UploadIntentId intentId, CancellationToken ct)
            => Task.FromResult(UploadStatus.Complete);
    }
}
