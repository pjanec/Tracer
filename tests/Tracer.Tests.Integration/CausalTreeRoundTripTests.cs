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
/// return structurally identical trace tree results (TRC-P6-011).
/// Uses the same collection as TimelineRoundTripTests to serialize OfflineViewer port use.
/// </summary>
[Collection("TimelineRoundTrip")]
public sealed class CausalTreeRoundTripTests : IAsyncLifetime
{
    private ObserverFixture _observer = null!;
    private string _nasRoot = null!;
    private string _bundlesRoot = null!;
    private string _builtBundlePath = null!;
    private WebApplication? _viewerApp;
    private HttpClient? _bundleClient;

    private ulong _traceId;
    private ulong _rootEventId;
    private readonly List<ulong> _childEventIds = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static ulong _nextId = 500_000;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private static EventRecord MakeEvent(ulong eventId, ulong traceId, ulong parentId,
        DateTimeOffset at, string node = "causal-node-a")
    {
        return new EventRecord
        {
            SequenceNumber   = eventId,
            PublishWallclock = At(at),
            ReceiveWallclock = At(at),
            PublisherNode    = new AgentId(node),
            SubscriberNode   = new AgentId(node),
            Topic            = new TopicName("causal.rt.test"),
            EventId          = new Tracer.Core.Identity.EventId(eventId),
            TraceId          = new Tracer.Core.Identity.TraceId(traceId),
            ParentEventId    = parentId != 0 ? new Tracer.Core.Identity.EventId(parentId) : null,
            PayloadJson      = "{}",
        };
    }

    public async Task InitializeAsync()
    {
        var baseTime = DateTimeOffset.UtcNow;
        _nasRoot     = Path.Combine(Path.GetTempPath(), $"causal-rt-nas-{Guid.NewGuid():N}");
        _bundlesRoot = Path.Combine(Path.GetTempPath(), $"causal-rt-bundles-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_nasRoot);
        Directory.CreateDirectory(_bundlesRoot);

        var bundlesRoot = _bundlesRoot;
        var nasRoot = _nasRoot;

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

        // Build a 10-event trace: 1 root → 9 children
        _traceId = _nextId++;
        _rootEventId = _nextId++;
        var events = new List<EventRecord>
        {
            MakeEvent(_rootEventId, _traceId, 0, baseTime)
        };
        for (int i = 0; i < 9; i++)
        {
            var childId = _nextId++;
            _childEventIds.Add(childId);
            events.Add(MakeEvent(childId, _traceId, _rootEventId, baseTime.AddSeconds(i + 1)));
        }
        await _observer.PushAsync(events);

        // Force rotation + copy to NAS (same pattern as TimelineRoundTripTests)
        await _observer.ForceRotationAsync();

        var tracker = _observer.App.Services.GetRequiredService<IntervalSetTracker>();
        var snapshot = tracker.CurrentSnapshot();
        var uploadService = new LocalFileSystemUploadService(_nasRoot);
        foreach (var ivref in snapshot.Completed)
        {
            var ivManifest = await ManifestWriter.ReadAsync(ivref.Directory.ManifestPath, CancellationToken.None);
            if (ivManifest is null) continue;
            var request = new UploadRequest
            {
                NodeId               = ivManifest.NodeId,
                Interval             = ivManifest.IntervalStart,
                IntervalStartUtc     = WallclockTime.FromDateTimeOffset(ivManifest.IntervalStart.ToDateTimeOffset()),
                IntervalEndUtc       = WallclockTime.FromDateTimeOffset(ivManifest.IntervalEnd.ToDateTimeOffset()),
                Files                = ivref.Directory.EnumerateFiles(),
            };
            await uploadService.RequestUploadAsync(request, CancellationToken.None);
        }

        // Build bundle
        var buildRequest = new BundleBuildRequestDto
        {
            TimeRange = new TimeRangeDto
            {
                StartUtc = baseTime.AddHours(-1),
                EndUtc   = baseTime.AddHours(1),
            }
        };
        var postResp = await _observer.Client.PostAsJsonAsync("/api/bundles/build", buildRequest);
        postResp.EnsureSuccessStatusCode();
        var accepted = await postResp.Content.ReadFromJsonAsync<BundleBuildAcceptedDto>();
        var status = await PollUntilDoneAsync(accepted!.BundleId, timeoutSeconds: 60);
        status.State.Should().Be("Completed", $"bundle build failed: {status.Error}");
        _builtBundlePath = status.OutputPath!;

        // Start OfflineViewer
        _viewerApp = OfflineViewerHostBuilder.Build(_builtBundlePath);
        await _viewerApp.StartAsync();
        var config = _viewerApp.Services.GetRequiredService<OfflineViewerConfig>();
        _bundleClient = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{config.HttpPort}/"),
            Timeout     = TimeSpan.FromSeconds(30),
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

    [Fact]
    public async Task LiveAndBundleResponses_AreStructurallyIdentical()
    {
        var traceIdHex = _traceId.ToString("X16");
        var url = $"api/traces/{traceIdHex}/tree";

        var liveResponse   = await _observer.Client.GetAsync(url);
        var bundleResponse = await _bundleClient!.GetAsync(url);

        liveResponse.IsSuccessStatusCode.Should().BeTrue($"live query returned {liveResponse.StatusCode}");
        bundleResponse.IsSuccessStatusCode.Should().BeTrue($"bundle query returned {bundleResponse.StatusCode}");

        var liveJson   = await liveResponse.Content.ReadAsStringAsync();
        var bundleJson = await bundleResponse.Content.ReadAsStringAsync();

        var liveTree   = JsonSerializer.Deserialize<TraceTreeDto>(liveJson, JsonOpts);
        var bundleTree = JsonSerializer.Deserialize<TraceTreeDto>(bundleJson, JsonOpts);

        liveTree.Should().NotBeNull();
        bundleTree.Should().NotBeNull();

        // Assert structural identity
        var liveNodeIds   = liveTree!.Nodes.Select(n => n.EventId).OrderBy(x => x).ToList();
        var bundleNodeIds = bundleTree!.Nodes.Select(n => n.EventId).OrderBy(x => x).ToList();
        liveNodeIds.Should().BeEquivalentTo(bundleNodeIds,
            "live and bundle must return identical node IDs");

        var liveEdges   = liveTree.Edges.Select(e => (e.ParentEventId, e.ChildEventId)).OrderBy(x => x).ToList();
        var bundleEdges = bundleTree.Edges.Select(e => (e.ParentEventId, e.ChildEventId)).OrderBy(x => x).ToList();
        liveEdges.Should().BeEquivalentTo(bundleEdges,
            "live and bundle must return identical edges");

        var liveRoots   = liveTree.RootEventIds.OrderBy(x => x).ToList();
        var bundleRoots = bundleTree.RootEventIds.OrderBy(x => x).ToList();
        liveRoots.Should().BeEquivalentTo(bundleRoots, "root event IDs must match");

        var liveLeaves   = liveTree.LeafEventIds.OrderBy(x => x).ToList();
        var bundleLeaves = bundleTree.LeafEventIds.OrderBy(x => x).ToList();
        liveLeaves.Should().BeEquivalentTo(bundleLeaves, "leaf event IDs must match");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
