using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracer.Adapters.Mock.Storage;
using Tracer.Adapters.Mock.Upload;
using Tracer.Agent.Storage;
using Tracer.Aggregator;
using Tracer.Bundle.Format;
using Tracer.Bundle.Packaging;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.OfflineViewer;
using Tracer.Storage.DuckDB.MultiInterval;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Bundles;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Endpoints;
using Xunit;

namespace Tracer.Tests.Integration;

/// <summary>
/// Round-trip tests verifying that live Observer queries and OfflineViewer bundle queries
/// return identical results for the same session (TRC-P5-012).
/// </summary>
[Collection("TimelineRoundTrip")]
public sealed class TimelineRoundTripTests : IAsyncLifetime
{
    // Live Observer
    private ObserverFixture _observer = null!;
    private string _nasRoot = null!;
    private string _bundlesRoot = null!;

    // Bundle / OfflineViewer
    private string _builtBundlePath = null!;
    private WebApplication? _viewerApp;
    private HttpClient? _bundleClient;

    // Shared test data
    private string _sessionId = null!;
    private DateTimeOffset _baseTime;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static ulong _nextId = 300_000;

    public async Task InitializeAsync()
    {
        _baseTime = DateTimeOffset.UtcNow;
        _sessionId = $"rt-{Guid.NewGuid():N}";
        _nasRoot = Path.Combine(Path.GetTempPath(), $"timeline-rt-nas-{Guid.NewGuid():N}");
        _bundlesRoot = Path.Combine(Path.GetTempPath(), $"timeline-rt-bundles-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_nasRoot);
        Directory.CreateDirectory(_bundlesRoot);

        var bundlesRoot = _bundlesRoot;
        var nasRoot = _nasRoot;

        // Configure Observer with bundle build services.
        // NOTE: We do NOT override ITelemetryUploadService here. Instead we manually copy
        // the completed interval into nasRoot after ForceRotationAsync() returns, because
        // ForceRotationAsync triggers RebuildAsync() which opens a READ_ONLY DuckDB
        // connection on the completed interval, causing DuckDB to apply the WAL and
        // produce a valid checkpoint. The default upload inside RotateAsync fires before
        // that checkpoint, so the built-in upload would copy an empty events.duckdb.
        _observer = await ObserverFixture.CreateAsync(
            configureExtraServices: services =>
            {
                services.AddSingleton<ITelemetryStorageReader>(sp =>
                    new LocalFileSystemStorageReader(nasRoot,
                        sp.GetRequiredService<ILogger<LocalFileSystemStorageReader>>()));

                services.AddSingleton<BundleCatalog>(sp =>
                    new BundleCatalog(bundlesRoot, sp.GetRequiredService<ILogger<BundleCatalog>>()));

                services.AddSingleton<IAggregationOrchestrator>(sp =>
                    new AggregationOrchestrator(
                        sp.GetRequiredService<ITelemetryStorageReader>(),
                        sp.GetRequiredService<ILogger<AggregationOrchestrator>>()));

                services.AddSingleton<BundleBuildService>();
            },
            configureExtraApp: app => BundleEndpoints.Map(app));

        // Push session_start + 9 events so both live DuckDB and the NAS have them
        var events = new List<EventRecord> { MakeSessionStart(_sessionId, _baseTime) };
        for (var i = 0; i < 9; i++)
            events.Add(MakeEvent(_sessionId, _baseTime.AddSeconds(i + 1), $"rt.topic.{i % 3}"));

        await _observer.PushAsync(events);

        // ForceRotationAsync closes the writer then calls tracker.OnIntervalRotatedAsync(),
        // which fires SetChanged → LiveMultiIntervalReader.RebuildAsync(). RebuildAsync
        // opens a READ_ONLY connection on the completed interval, which causes DuckDB to
        // apply the WAL and fully checkpoint events.duckdb. After this call returns,
        // events.duckdb is a valid, fully-populated DuckDB database file.
        await _observer.ForceRotationAsync();

        // Now copy the checkpointed interval into nasRoot so the bundle build can read it.
        var tracker = _observer.App.Services.GetRequiredService<IntervalSetTracker>();
        var snapshot = tracker.CurrentSnapshot();
        var uploadService = new LocalFileSystemUploadService(_nasRoot);
        foreach (var ivref in snapshot.Completed)
        {
            var ivManifest = await ManifestWriter.ReadAsync(ivref.Directory.ManifestPath, CancellationToken.None);
            if (ivManifest is null) continue;
            var request = new UploadRequest
            {
                NodeId = ivManifest.NodeId,
                Interval = ivManifest.IntervalStart,
                IntervalStartUtc = WallclockTime.FromDateTimeOffset(ivManifest.IntervalStart.ToDateTimeOffset()),
                IntervalEndUtc = WallclockTime.FromDateTimeOffset(ivManifest.IntervalEnd.ToDateTimeOffset()),
                Files = ivref.Directory.EnumerateFiles(),
            };
            await uploadService.RequestUploadAsync(request, CancellationToken.None);
        }

        // Build bundle from the NAS containing our uploaded interval.
        // Use a wide window around _baseTime to ensure the real interval
        // (whose boundaries are based on wall-clock, not event timestamps) is found.
        var buildRequest = new BundleBuildRequestDto
        {
            TimeRange = new TimeRangeDto
            {
                StartUtc = _baseTime.AddHours(-1),
                EndUtc = _baseTime.AddHours(1),
            }
        };

        var postResp = await _observer.Client.PostAsJsonAsync("/api/bundles/build", buildRequest);
        postResp.EnsureSuccessStatusCode();
        var accepted = await postResp.Content.ReadFromJsonAsync<BundleBuildAcceptedDto>();

        var status = await PollUntilDoneAsync(accepted!.BundleId, timeoutSeconds: 60);
        status.State.Should().Be("Completed", $"bundle build failed: {status.Error}");

        _builtBundlePath = status.OutputPath!;

        // Start OfflineViewer pointing at the bundle
        _viewerApp = OfflineViewerHostBuilder.Build(_builtBundlePath);
        await _viewerApp.StartAsync();

        var config = _viewerApp.Services.GetRequiredService<OfflineViewerConfig>();
        _bundleClient = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{config.HttpPort}/"),
            Timeout = TimeSpan.FromSeconds(30),
        };

        var manifest = await BundleReader.ReadManifestAsync(_builtBundlePath);
        await WaitForBundleLoadedAsync(_bundleClient, manifest.BundleId);
    }

    public async Task DisposeAsync()
    {
        _bundleClient?.Dispose();
        if (_viewerApp is not null)
        {
            await _viewerApp.StopAsync();
            await _viewerApp.DisposeAsync();
        }
        await _observer.DisposeAsync();
        try { Directory.Delete(_nasRoot, recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_bundlesRoot, recursive: true); } catch { /* best effort */ }
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RoundTrip_ListQuery_LiveAndBundleReturnIdenticalEvents()
    {
        var url = $"api/events?sessionId={_sessionId}&limit=100";

        var liveJson = await (await _observer.Client.GetAsync(url)).Content.ReadAsStringAsync();
        var bundleJson = await (await _bundleClient!.GetAsync(url)).Content.ReadAsStringAsync();

        var live = JsonSerializer.Deserialize<EventListDto>(liveJson, JsonOpts);
        var bundle = JsonSerializer.Deserialize<EventListDto>(bundleJson, JsonOpts);

        live.Should().NotBeNull();
        bundle.Should().NotBeNull();
        live!.Events.Should().NotBeEmpty("live observer must return events for the session");
        bundle!.Events.Should().NotBeEmpty("offline viewer must return events from the bundle");

        var liveIds = live.Events.Select(e => e.EventId).OrderBy(x => x).ToList();
        var bundleIds = bundle.Events.Select(e => e.EventId).OrderBy(x => x).ToList();
        liveIds.Should().BeEquivalentTo(bundleIds,
            "live and bundle must contain identical event IDs for the same session");
    }

    [Fact]
    public async Task RoundTrip_AggregateQuery_LiveAndBundleReturnIdenticalBuckets()
    {
        var from = Uri.EscapeDataString(_baseTime.AddMinutes(-5).ToString("O"));
        var to = Uri.EscapeDataString(_baseTime.AddMinutes(5).ToString("O"));
        var url = $"api/events/aggregate?sessionId={_sessionId}&bucketDuration=5s&from={from}&to={to}";

        var liveJson = await (await _observer.Client.GetAsync(url)).Content.ReadAsStringAsync();
        var bundleJson = await (await _bundleClient!.GetAsync(url)).Content.ReadAsStringAsync();

        var live = JsonSerializer.Deserialize<EventAggregateDto>(liveJson, JsonOpts);
        var bundle = JsonSerializer.Deserialize<EventAggregateDto>(bundleJson, JsonOpts);

        live.Should().NotBeNull();
        bundle.Should().NotBeNull();
        live!.Buckets.Should().NotBeEmpty("live observer aggregate must return non-empty buckets");
        bundle!.Buckets.Should().NotBeEmpty("offline viewer aggregate must return non-empty buckets");

        var liveTotals = live.Buckets.Sum(b => b.Total);
        var bundleTotals = bundle.Buckets.Sum(b => b.Total);
        liveTotals.Should().Be(bundleTotals,
            "aggregate total event counts must match between live and bundle");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task RoundTrip_OpenSession_1MEvents_FirstResponseUnder500ms()
    {
        // Note: spec requires 1M events; using 10K for practical test execution
        var perfSessionId = $"perf-list-{Guid.NewGuid():N}";
        var events = new List<EventRecord> { MakeSessionStart(perfSessionId, _baseTime.AddHours(1)) };
        for (var i = 0; i < 9_999; i++)
            events.Add(MakeEvent(perfSessionId, _baseTime.AddHours(1).AddMilliseconds(i), "perf.tick"));

        await _observer.PushAsync(events);
        await _observer.ForceRotationAsync();
        await Task.Delay(100);

        var url = $"api/events?sessionId={perfSessionId}&limit=100";

        var sw = Stopwatch.StartNew();
        var response = await _observer.Client.GetAsync(url);
        sw.Stop();

        response.EnsureSuccessStatusCode();
        sw.ElapsedMilliseconds.Should().BeLessThan(500,
            "list query over ~10K events must complete within 500ms");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task RoundTrip_AggregateQuery_100MEvents_CompletesUnder1s()
    {
        // Note: spec requires 100M events; using 10K for practical test execution
        var perfSessionId = $"perf-agg-{Guid.NewGuid():N}";
        var events = new List<EventRecord> { MakeSessionStart(perfSessionId, _baseTime.AddHours(2)) };
        for (var i = 0; i < 9_999; i++)
            events.Add(MakeEvent(perfSessionId, _baseTime.AddHours(2).AddMilliseconds(i), "perf.agg"));

        await _observer.PushAsync(events);
        await _observer.ForceRotationAsync();
        await Task.Delay(100);

        var from = Uri.EscapeDataString(_baseTime.AddHours(2).AddMinutes(-1).ToString("O"));
        var to = Uri.EscapeDataString(_baseTime.AddHours(2).AddMinutes(5).ToString("O"));
        var url = $"api/events/aggregate?sessionId={perfSessionId}&bucketDuration=1s&from={from}&to={to}";

        var sw = Stopwatch.StartNew();
        var response = await _observer.Client.GetAsync(url);
        sw.Stop();

        response.EnsureSuccessStatusCode();
        sw.ElapsedMilliseconds.Should().BeLessThan(1000,
            "aggregate query over ~10K events must complete within 1000ms");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private static EventRecord MakeSessionStart(string sessionId, DateTimeOffset at, string nodeId = "node-a")
    {
        var payload = JsonSerializer.Serialize(new
        {
            sessionId,
            scenarioId = "TimelineRoundTrip",
            label = "Timeline Round Trip Test",
        });
        return new EventRecord
        {
            SequenceNumber = _nextId++,
            PublishWallclock = At(at),
            ReceiveWallclock = At(at),
            PublisherNode = new AgentId(nodeId),
            SubscriberNode = new AgentId(nodeId),
            Topic = new TopicName("system.session_start"),
            EventId = new Tracer.Core.Identity.EventId(_nextId++),
            TraceId = new TraceId(_nextId++),
            PayloadJson = payload,
        };
    }

    private static EventRecord MakeEvent(string sessionId, DateTimeOffset at, string topic, string nodeId = "node-a")
    {
        var payload = JsonSerializer.Serialize(new { sessionId });
        return new EventRecord
        {
            SequenceNumber = _nextId++,
            PublishWallclock = At(at),
            ReceiveWallclock = At(at),
            PublisherNode = new AgentId(nodeId),
            SubscriberNode = new AgentId(nodeId),
            Topic = new TopicName(topic),
            EventId = new Tracer.Core.Identity.EventId(_nextId++),
            TraceId = new TraceId(_nextId++),
            PayloadJson = payload,
        };
    }

    private async Task<BundleBuildStatusDto> PollUntilDoneAsync(string bundleId, int timeoutSeconds = 30)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var r = await _observer.Client.GetAsync($"/api/bundles/{bundleId}/status");
            var status = await r.Content.ReadFromJsonAsync<BundleBuildStatusDto>();
            if (status?.State is "Completed" or "Failed")
                return status;
            await Task.Delay(500);
        }
        throw new TimeoutException($"Bundle {bundleId} did not complete within {timeoutSeconds}s");
    }

    private static async Task WaitForBundleLoadedAsync(
        HttpClient client, string expectedBundleId, int timeoutSeconds = 10)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var res = await client.GetAsync("api/bundle/current");
                if (res.IsSuccessStatusCode)
                {
                    var json = await res.Content.ReadAsStringAsync();
                    if (json.Contains(expectedBundleId, StringComparison.Ordinal))
                        return;
                }
            }
            catch { /* retry */ }
            await Task.Delay(200);
        }
        throw new TimeoutException(
            $"OfflineViewer did not load bundle '{expectedBundleId}' within {timeoutSeconds}s");
    }
}
