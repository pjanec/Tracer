using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Adapters.Mock.Storage;
using Tracer.Aggregator;
using Tracer.Core.Abstractions;
using Tracer.Observer.Configuration;
using Tracer.OfflineViewer;
using Tracer.Storage.Annotations;
using Tracer.TestHarness;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Bundles;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Endpoints;
using Xunit;

namespace Tracer.Tests.Integration;

/// <summary>
/// Integration tests: create annotations in Observer, build bundle, verify in OfflineViewer.
/// TRC-P8-018 SC-13 and SC-14.
/// </summary>
[Collection("AnnotationsRoundTrip")]
public sealed class AnnotationsRoundTripTests : IAsyncLifetime
{
    private AggregationFixture? _nasFixture;
    private ObserverFixture? _observer;
    private WebApplication? _viewerApp;
    private HttpClient? _viewerClient;
    private string _bundlesRoot = "";

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task InitializeAsync()
    {
        // 1. Set up NAS data source for bundle building
        _nasFixture = await AggregationFixture.InitializeAsync();
        _bundlesRoot = Path.Combine(Path.GetTempPath(), $"annot-rt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_bundlesRoot);

        var nasRoot = _nasFixture.NasRoot;
        var bundlesRoot = _bundlesRoot;

        // 2. Set up Observer with annotation store + bundle build capability
        _observer = await ObserverFixture.CreateAsync(
            configureExtraServices: services =>
            {
                // Annotation store (SQLite)
                services.AddSingleton<IAnnotationStore>(sp =>
                {
                    var cfg = sp.GetRequiredService<ObserverConfig>();
                    var path = Path.Combine(cfg.DataRoot, "annotations.db");
                    var store = new SqliteAnnotationStore(
                        path, NullLogger<SqliteAnnotationStore>.Instance);
                    store.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
                    return store;
                });

                // Bundle catalog and storage reader
                services.AddSingleton<BundleCatalog>(sp =>
                    new BundleCatalog(bundlesRoot, sp.GetRequiredService<ILogger<BundleCatalog>>()));
                services.AddSingleton<ITelemetryStorageReader>(sp =>
                    new LocalFileSystemStorageReader(
                        nasRoot, sp.GetRequiredService<ILogger<LocalFileSystemStorageReader>>()));

                // Aggregation orchestrator — wired with annotation store
                services.AddSingleton<IAggregationOrchestrator>(sp =>
                    new AggregationOrchestrator(
                        sp.GetRequiredService<ITelemetryStorageReader>(),
                        sp.GetRequiredService<ILogger<AggregationOrchestrator>>(),
                        sp.GetRequiredService<IAnnotationStore>()));

                services.AddSingleton<BundleBuildService>();
            },
            configureExtraApp: app =>
            {
                AnnotationEndpoints.Map(app);
                BundleEndpoints.Map(app);
            });

        // 3. Create 3 annotations via Observer HTTP API (sessionId = "" → exported via TimeRange build)
        var a1 = new CreateAnnotationDto { SessionId = "", Kind = "Event", EventId = "0000000000000001", Body = "First annotation" };
        var a2 = new CreateAnnotationDto { SessionId = "", Kind = "Event", EventId = "0000000000000002", Body = "Second annotation" };
        var a3 = new CreateAnnotationDto { SessionId = "", Kind = "Event", EventId = "0000000000000003", Body = "Third annotation" };

        (await _observer.Client.PostAsJsonAsync("/api/annotations", a1)).EnsureSuccessStatusCode();
        (await _observer.Client.PostAsJsonAsync("/api/annotations", a2)).EnsureSuccessStatusCode();
        (await _observer.Client.PostAsJsonAsync("/api/annotations", a3)).EnsureSuccessStatusCode();

        // 4. Trigger bundle build using NAS time range (SessionId stays null → "" used for export)
        var nasRange = _nasFixture.NasTimeRange;
        var buildResp = await _observer.Client.PostAsJsonAsync("/api/bundles/build",
            new BundleBuildRequestDto
            {
                TimeRange = new TimeRangeDto
                {
                    StartUtc = nasRange.StartUtc.ToDateTimeOffset(),
                    EndUtc = nasRange.EndUtc.ToDateTimeOffset(),
                }
            });
        buildResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = await buildResp.Content.ReadFromJsonAsync<BundleBuildAcceptedDto>();
        accepted.Should().NotBeNull();

        var status = await PollBuildUntilDoneAsync(accepted!.BundleId, timeoutSeconds: 90);
        status.State.Should().Be("Completed",
            $"Bundle build failed: {status.Error ?? "(no error)"}");

        // 5. Open OfflineViewer on completed bundle
        _viewerApp = OfflineViewerHostBuilder.Build(status.OutputPath);
        await _viewerApp.StartAsync();

        var viewerCfg = _viewerApp.Services.GetRequiredService<OfflineViewerConfig>();
        _viewerClient = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{viewerCfg.HttpPort}/"),
            Timeout = TimeSpan.FromSeconds(30),
        };

        await WaitForBundleLoadedAsync(_viewerClient, accepted.BundleId);
    }

    public async Task DisposeAsync()
    {
        _viewerClient?.Dispose();
        if (_viewerApp is not null)
        {
            await _viewerApp.StopAsync();
            await _viewerApp.DisposeAsync();
        }
        if (_observer is not null)
            await _observer.DisposeAsync();
        if (_nasFixture is not null)
            await _nasFixture.DisposeAsync();
        try { if (Directory.Exists(_bundlesRoot)) Directory.Delete(_bundlesRoot, recursive: true); } catch { }
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>SC-13: 3 annotations created live survive bundle build and appear in offline viewer.</summary>
    [Fact]
    public async Task AnnotationsRoundTrip_LiveToBundleToOffline()
    {
        var resp = await _viewerClient!.GetAsync("api/annotations");
        resp.EnsureSuccessStatusCode();

        var annotations = await resp.Content.ReadFromJsonAsync<AnnotationDto[]>(CamelCaseOptions);
        annotations.Should().NotBeNull();
        annotations!.Should().HaveCount(3,
            "3 annotations created in observer must survive the round-trip to offline viewer");

        annotations!.Select(a => a.Body).Should().BeEquivalentTo(
            new[] { "First annotation", "Second annotation", "Third annotation" },
            opts => opts.WithoutStrictOrdering());
    }

    /// <summary>SC-14: POST to annotations in bundle mode returns 405.</summary>
    [Fact]
    public async Task AnnotationsRoundTrip_BundleMode_PostReturns405()
    {
        var dto = new CreateAnnotationDto
        {
            SessionId = "",
            Kind = "Event",
            EventId = "0000000000000099",
            Body = "Should be rejected"
        };
        var resp = await _viewerClient!.PostAsJsonAsync("api/annotations", dto);
        ((int)resp.StatusCode).Should().Be(405,
            "bundle mode must reject annotation writes with HTTP 405");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<BundleBuildStatusDto> PollBuildUntilDoneAsync(
        string bundleId, int timeoutSeconds)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var r = await _observer!.Client.GetAsync($"/api/bundles/{bundleId}/status");
            if (r.IsSuccessStatusCode)
            {
                var status = await r.Content.ReadFromJsonAsync<BundleBuildStatusDto>();
                if (status?.State is "Completed" or "Failed")
                    return status;
            }
            await Task.Delay(500);
        }
        throw new TimeoutException($"Bundle {bundleId} did not complete within {timeoutSeconds}s");
    }

    private static async Task WaitForBundleLoadedAsync(HttpClient client, string bundleId, int timeoutSeconds = 15)
    {
        // The OfflineViewer is started with the bundle path as InitialBundlePath.
        // The manifest BundleId is different from the service bundleId (directory name),
        // so we just wait for any non-null bundle to appear.
        _ = bundleId; // parameter kept for API clarity
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var res = await client.GetAsync("api/bundle/current");
                if (res.IsSuccessStatusCode)
                {
                    var json = await res.Content.ReadAsStringAsync();
                    // Response is non-null when a bundle is loaded (not "null")
                    if (!string.Equals(json.Trim(), "null", StringComparison.OrdinalIgnoreCase)
                        && json.Contains("bundleId", StringComparison.OrdinalIgnoreCase))
                        return;
                }
            }
            catch { /* retry */ }
            await Task.Delay(200);
        }
        throw new TimeoutException($"OfflineViewer did not load a bundle within {timeoutSeconds}s");
    }
}
