using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Agent.Storage;
using Tracer.Agent.Upload;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Time;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Streaming;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class HealthEndpointsTests : IAsyncDisposable
{
    private WebApiFixture? _fixture;

    public async ValueTask DisposeAsync()
    {
        if (_fixture is not null)
            await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task GetHealth_WithAllServicesNull_ReturnsZeroMetrics()
    {
        // WebApiFixture registers SseConnectionManager (with 0 active connections)
        // but does NOT register IAgentTransport or UploadIntentDispatcher
        _fixture = await WebApiFixture.CreateAsync();

        var response = await _fixture.Client.GetAsync("/api/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("ok");
        root.GetProperty("sharedMemoryDropped").GetInt64().Should().Be(0L);
        root.GetProperty("ingestChannelDepth").GetInt32().Should().Be(0);
        root.GetProperty("sseConnectionsActive").GetInt32().Should().Be(0);
        root.GetProperty("intervalsAwaitingUpload").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GetHealth_WithSseManager_ReturnsActiveCount()
    {
        _fixture = await WebApiFixture.CreateAsync();

        // Register 3 active SSE connections
        for (int i = 0; i < 3; i++)
            _fixture.SseConnections.TryRegister(new SseFilter());

        var response = await _fixture.Client.GetAsync("/api/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("sseConnectionsActive").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task GetHealth_WithUploadDispatcher_ReturnsPendingCount()
    {
        // Create a dispatcher whose upload service never completes,
        // keeping PendingCount elevated while dispatches are in-flight.
        var uploadStarted = new SemaphoreSlim(0);
        var allowComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var uploadService = new LambdaUploadService(async ct =>
        {
            uploadStarted.Release();
            await allowComplete.Task.WaitAsync(ct);
            return new UploadIntentId(Guid.NewGuid().ToString());
        });

        var dispatcher = new UploadIntentDispatcher(
            uploadService, NullLogger<UploadIntentDispatcher>.Instance);

        _fixture = await WebApiFixture.CreateAsync(
            configureExtraServices: services => services.AddSingleton(dispatcher));

        // Build minimal IntervalDirectory and manifest (no files on disk needed for dispatch)
        var ts = new IntervalTimestamp("20260101T000000Z");
        var dir = new IntervalDirectory(Path.GetTempPath(), ts);
        var manifest = new IntervalManifest
        {
            IntervalStart = ts,
            IntervalEnd = new IntervalTimestamp("20260101T010000Z"),
            NodeId = new AgentId("test"),
            TracerVersion = "1.0.0",
            SchemaVersion = 1,
            EventCount = 0,
            SlowStateCount = 0,
            FastStateTopics = Array.Empty<string>(),
            CaptureGaps = Array.Empty<CaptureGap>(),
            SessionMarkers = Array.Empty<SessionMarker>(),
            FinalizedAt = WallclockTime.Zero,
            FinalizationReason = ManifestFinalizationReason.ScheduledRotation,
        };

        // Fire 5 dispatches without awaiting — they block on the upload service
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var dispatchTasks = Enumerable.Range(0, 5)
            .Select(_ => dispatcher.DispatchAsync(dir, manifest, cts.Token))
            .ToList();

        // Wait until all 5 uploads have reached the blocking point
        for (int i = 0; i < 5; i++)
            await uploadStarted.WaitAsync(TimeSpan.FromSeconds(10));

        // PendingCount must be 5 now
        var response = await _fixture.Client.GetAsync("/api/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("intervalsAwaitingUpload").GetInt32().Should().Be(5);

        // Cleanup: unblock the dispatches
        allowComplete.SetResult();
        try { await Task.WhenAll(dispatchTasks); } catch { /* ignore cancellation */ }
    }

    // ── Test doubles ─────────────────────────────────────────────────────────────

    private sealed class LambdaUploadService : ITelemetryUploadService
    {
        private readonly Func<CancellationToken, Task<UploadIntentId>> _handler;

        public LambdaUploadService(Func<CancellationToken, Task<UploadIntentId>> handler)
            => _handler = handler;

        public Task<UploadIntentId> RequestUploadAsync(UploadRequest request, CancellationToken ct)
            => _handler(ct);

        public Task<UploadStatus> GetStatusAsync(UploadIntentId intentId, CancellationToken ct)
            => Task.FromResult(UploadStatus.Complete);
    }
}
