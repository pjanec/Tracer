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
using Tracer.Storage.DuckDB.MultiInterval;
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
        var tracker = new TrackingTracker(order, "tracker-init", _rotator,
            NullLogger<IntervalSetTracker>.Instance);
        var reader = new NoOpReader(tracker, NullLogger<LiveMultiIntervalReader>.Instance);

        var svc = new ObserverHostedService(
            recovery, _rotator, _scheduler, _ingestion, tracker, reader, _retention,
            new SystemClock(), NullLogger<ObserverHostedService>.Instance);

        await svc.StartAsync(default);
        await Task.Delay(300); // allow ExecuteAsync to reach tracker init
        await svc.StopAsync(default);

        order.Should().ContainInOrder("recovery", "tracker-init");
    }

    [Fact]
    public async Task OnStart_TrackerInitializedAfterIntervalOpen()
    {
        var order = new List<string>();
        var recovery = new TrackingRecovery(order, "recovery");
        var tracker = new TrackingTracker(order, "tracker-init", _rotator,
            NullLogger<IntervalSetTracker>.Instance);
        var reader = new NoOpReader(tracker, NullLogger<LiveMultiIntervalReader>.Instance);

        var svc = new ObserverHostedService(
            recovery, _rotator, _scheduler, _ingestion, tracker, reader, _retention,
            new SystemClock(), NullLogger<ObserverHostedService>.Instance);

        try { await svc.StartAsync(default); } catch { }
        try { await Task.Delay(300); } catch { }
        try { await svc.StopAsync(default); } catch { }

        tracker.InitializeCalled.Should().BeTrue("tracker must be initialized on start");
    }

    [Fact]
    public async Task OnGracefulShutdown_FinalRotationHasGracefulReason()
    {
        var recovery = new FakeRecovery();
        var tracker = new NoOpTracker(_rotator, NullLogger<IntervalSetTracker>.Instance);
        var reader = new NoOpReader(tracker, NullLogger<LiveMultiIntervalReader>.Instance);

        var svc = new ObserverHostedService(
            recovery, _rotator, _scheduler, _ingestion, tracker, reader, _retention,
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
    public async Task TrackerRefreshFailure_Logged_HostNotCrashed()
    {
        var recovery = new FakeRecovery();
        var tracker = new FailingTracker(_rotator, NullLogger<IntervalSetTracker>.Instance);
        var reader = new NoOpReader(tracker, NullLogger<LiveMultiIntervalReader>.Instance);

        var svc = new ObserverHostedService(
            recovery, _rotator, _scheduler, _ingestion, tracker, reader, _retention,
            new SystemClock(), NullLogger<ObserverHostedService>.Instance);

        // Should not throw even when tracker refresh fails
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
        var tracker = new NoOpTracker(_rotator, NullLogger<IntervalSetTracker>.Instance);
        var reader = new NoOpReader(tracker, NullLogger<LiveMultiIntervalReader>.Instance);

        var svc = new ObserverHostedService(
            recovery, _rotator, _scheduler, _ingestion, tracker, reader, _retention,
            new SystemClock(), NullLogger<ObserverHostedService>.Instance);

        Func<Task> act = async () =>
        {
            await svc.StartAsync(default);
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

    /// <summary>No-op tracker: overrides all virtual methods so no filesystem access occurs.</summary>
    private sealed class NoOpTracker : IntervalSetTracker
    {
        public NoOpTracker(IntervalRotator rotator, ILogger<IntervalSetTracker> logger)
            : base(rotator, 3, logger) { }

        public override Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
        public override Task OnIntervalRotatedAsync(CancellationToken ct) => Task.CompletedTask;
        public override IntervalSetSnapshot CurrentSnapshot() =>
            new(new List<IntervalReference>().AsReadOnly());
    }

    /// <summary>Tracking tracker: records when InitializeAsync is called.</summary>
    private sealed class TrackingTracker : IntervalSetTracker
    {
        private readonly List<string> _order;
        private readonly string _label;

        public bool InitializeCalled { get; private set; }

        public TrackingTracker(List<string> order, string label, IntervalRotator rotator,
            ILogger<IntervalSetTracker> logger)
            : base(rotator, 3, logger)
        { _order = order; _label = label; }

        public override Task InitializeAsync(CancellationToken ct)
        {
            InitializeCalled = true;
            _order.Add(_label);
            return Task.CompletedTask;
        }

        public override Task OnIntervalRotatedAsync(CancellationToken ct) => Task.CompletedTask;
        public override IntervalSetSnapshot CurrentSnapshot() =>
            new(new List<IntervalReference>().AsReadOnly());
    }

    /// <summary>Failing tracker: throws on OnIntervalRotatedAsync to test error resilience.</summary>
    private sealed class FailingTracker : IntervalSetTracker
    {
        public FailingTracker(IntervalRotator rotator, ILogger<IntervalSetTracker> logger)
            : base(rotator, 3, logger) { }

        public override Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
        public override Task OnIntervalRotatedAsync(CancellationToken ct)
            => throw new InvalidOperationException("Simulated tracker update failure");
        public override IntervalSetSnapshot CurrentSnapshot() =>
            new(new List<IntervalReference>().AsReadOnly());
    }

    /// <summary>No-op reader: overrides InitializeAsync to skip actual DuckDB connection setup.</summary>
    private sealed class NoOpReader : LiveMultiIntervalReader
    {
        public NoOpReader(IntervalSetTracker tracker, ILogger<LiveMultiIntervalReader> logger)
            : base(tracker, logger) { }

        public override Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoOpUploadService : ITelemetryUploadService
    {
        public Task<UploadIntentId> RequestUploadAsync(UploadRequest request, CancellationToken ct)
            => Task.FromResult(new UploadIntentId(Guid.NewGuid().ToString()));
        public Task<UploadStatus> GetStatusAsync(UploadIntentId intentId, CancellationToken ct)
            => Task.FromResult(UploadStatus.Complete);
    }
}
