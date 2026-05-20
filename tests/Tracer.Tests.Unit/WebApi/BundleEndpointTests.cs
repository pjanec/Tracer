using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracer.Aggregator;
using Tracer.Aggregator.Configuration;
using Tracer.Aggregator.Progress;
using Tracer.Bundle.Format;
using Tracer.Core.Time;
using Tracer.WebApi.Bundles;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Endpoints;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class BundleEndpointTests : IAsyncLifetime
{
    private BundleWebFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await BundleWebFixture.CreateAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // SC1: POST /api/bundles/build with sessionId → 202 Accepted
    [Fact]
    public async Task Build_WithSessionId_Returns202Accepted()
    {
        var request = new BundleBuildRequestDto { SessionId = "test-session-abc" };
        var response = await _fixture.Client.PostAsJsonAsync("/api/bundles/build", request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = await response.Content.ReadFromJsonAsync<BundleBuildAcceptedDto>();
        accepted.Should().NotBeNull();
        accepted!.BundleId.Should().NotBeNullOrWhiteSpace();
    }

    // SC2: POST /api/bundles/build with TimeRange → 202 Accepted
    [Fact]
    public async Task Build_WithTimeRange_Returns202Accepted()
    {
        var request = new BundleBuildRequestDto
        {
            TimeRange = new TimeRangeDto
            {
                StartUtc = DateTimeOffset.UtcNow.AddHours(-1),
                EndUtc = DateTimeOffset.UtcNow,
            }
        };
        var response = await _fixture.Client.PostAsJsonAsync("/api/bundles/build", request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = await response.Content.ReadFromJsonAsync<BundleBuildAcceptedDto>();
        accepted!.BundleId.Should().NotBeNullOrWhiteSpace();
    }

    // SC3: POST → build completes; GET status shows Completed
    [Fact]
    public async Task GetStatus_AfterBuild_ShowsCompleted()
    {
        var request = new BundleBuildRequestDto { SessionId = "status-test-session" };
        var postResponse = await _fixture.Client.PostAsJsonAsync("/api/bundles/build", request);
        var accepted = await postResponse.Content.ReadFromJsonAsync<BundleBuildAcceptedDto>();

        var bundleId = accepted!.BundleId;
        BundleBuildStatusDto? status = null;
        for (int i = 0; i < 50; i++)
        {
            var r = await _fixture.Client.GetAsync($"/api/bundles/{bundleId}/status");
            status = await r.Content.ReadFromJsonAsync<BundleBuildStatusDto>();
            if (status?.State is "Completed" or "Failed") break;
            await Task.Delay(100);
        }

        status.Should().NotBeNull();
        status!.State.Should().Be("Completed");
        status.CompletedAtUtc.Should().NotBeNull();
    }

    // SC4: List bundles returns empty when nothing built
    [Fact]
    public async Task ListBundles_Initially_ReturnsEmptyList()
    {
        var fixture = await BundleWebFixture.CreateAsync();
        await using var _ = fixture;

        var response = await fixture.Client.GetAsync("/api/bundles");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<BundleListDto>();
        list.Should().NotBeNull();
        list!.Bundles.Should().BeEmpty();
    }

    // SC5: After build completes, list includes the bundle
    [Fact]
    public async Task ListBundles_AfterCompletion_IncludesBuildEntry()
    {
        var request = new BundleBuildRequestDto { SessionId = "list-test-session" };
        var postResponse = await _fixture.Client.PostAsJsonAsync("/api/bundles/build", request);
        var accepted = await postResponse.Content.ReadFromJsonAsync<BundleBuildAcceptedDto>();

        await WaitForCompletionAsync(_fixture.Client, accepted!.BundleId);

        var listResponse = await _fixture.Client.GetAsync("/api/bundles");
        var list = await listResponse.Content.ReadFromJsonAsync<BundleListDto>();
        list!.Bundles.Should().Contain(e => e.BundleId == accepted.BundleId);
    }

    // SC6: GET /api/bundles/{id} returns manifest DTO after completion
    [Fact]
    public async Task GetManifest_AfterCompletion_ReturnsManifestDto()
    {
        var request = new BundleBuildRequestDto { SessionId = "manifest-test-session" };
        var postResponse = await _fixture.Client.PostAsJsonAsync("/api/bundles/build", request);
        var accepted = await postResponse.Content.ReadFromJsonAsync<BundleBuildAcceptedDto>();
        await WaitForCompletionAsync(_fixture.Client, accepted!.BundleId);

        var manifestResponse = await _fixture.Client.GetAsync($"/api/bundles/{accepted.BundleId}");
        manifestResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var manifest = await manifestResponse.Content.ReadFromJsonAsync<BundleManifestDto>();
        manifest.Should().NotBeNull();
        manifest!.BundleId.Should().NotBeNullOrWhiteSpace();
        manifest.Statistics.Should().NotBeNull();
    }

    // SC7: GET /api/bundles/{id}/download returns a zip containing manifest.json
    [Fact]
    public async Task Download_AfterCompletion_ReturnsZipWithManifest()
    {
        var request = new BundleBuildRequestDto { SessionId = "download-test-session" };
        var postResponse = await _fixture.Client.PostAsJsonAsync("/api/bundles/build", request);
        var accepted = await postResponse.Content.ReadFromJsonAsync<BundleBuildAcceptedDto>();
        await WaitForCompletionAsync(_fixture.Client, accepted!.BundleId);

        var downloadResponse = await _fixture.Client.GetAsync($"/api/bundles/{accepted.BundleId}/download");
        downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        downloadResponse.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");

        var zipBytes = await downloadResponse.Content.ReadAsByteArrayAsync();
        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        archive.Entries.Should().Contain(e => e.Name == BundleLayout.ManifestFile);
    }

    // SC8: DELETE /api/bundles/{id} returns 204 and removes from list
    [Fact]
    public async Task Delete_AfterCompletion_Returns204AndRemovesBundle()
    {
        var request = new BundleBuildRequestDto { SessionId = "delete-test-session" };
        var postResponse = await _fixture.Client.PostAsJsonAsync("/api/bundles/build", request);
        var accepted = await postResponse.Content.ReadFromJsonAsync<BundleBuildAcceptedDto>();
        await WaitForCompletionAsync(_fixture.Client, accepted!.BundleId);

        var deleteResponse = await _fixture.Client.DeleteAsync($"/api/bundles/{accepted.BundleId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var manifestResponse = await _fixture.Client.GetAsync($"/api/bundles/{accepted.BundleId}");
        manifestResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task WaitForCompletionAsync(HttpClient client, string bundleId, int maxAttempts = 100)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            var r = await client.GetAsync($"/api/bundles/{bundleId}/status");
            var status = await r.Content.ReadFromJsonAsync<BundleBuildStatusDto>();
            if (status?.State is "Completed" or "Failed") return;
            await Task.Delay(100);
        }
    }

    // ── Inner test fixture ─────────────────────────────────────────────────

    private sealed class BundleWebFixture : IAsyncDisposable
    {
        public HttpClient Client { get; private set; } = null!;
        public string BundlesRoot { get; private set; } = null!;
        private WebApplication _app = null!;
        private string _tempDir = null!;

        private BundleWebFixture() { }

        public static async Task<BundleWebFixture> CreateAsync()
        {
            var fixture = new BundleWebFixture();
            fixture._tempDir = Path.Combine(Path.GetTempPath(), $"bundle-unit-{Guid.NewGuid():N}");
            fixture.BundlesRoot = Path.Combine(fixture._tempDir, "bundles");
            Directory.CreateDirectory(fixture.BundlesRoot);

            var builder = WebApplication.CreateBuilder([]);
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();

            builder.Services.AddSingleton<BundleCatalog>(sp =>
                new BundleCatalog(fixture.BundlesRoot, sp.GetRequiredService<ILogger<BundleCatalog>>()));
            builder.Services.AddSingleton<IAggregationOrchestrator, FakeAggregationOrchestrator>();
            builder.Services.AddSingleton<BundleBuildService>();

            fixture._app = builder.Build();
            BundleEndpoints.Map(fixture._app);

            await fixture._app.StartAsync();
            fixture.Client = fixture._app.GetTestClient();
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ── Fake orchestrator ──────────────────────────────────────────────────

    private sealed class FakeAggregationOrchestrator : IAggregationOrchestrator
    {
        public async Task<AggregationResult> RunAsync(
            AggregationRequest request,
            IAggregationProgressReporter? progress = null,
            CancellationToken ct = default)
        {
            Directory.CreateDirectory(request.OutputPath);

            // Write a placeholder events db
            await File.WriteAllBytesAsync(
                Path.Combine(request.OutputPath, BundleLayout.EventsDb), [], ct);

            var bundleId = Ulid.NewUlid().ToString();
            var now = DateTimeOffset.UtcNow;

            var manifest = new BundleManifest
            {
                BundleId = bundleId,
                SchemaVersion = 1,
                CreatedAtUtc = now,
                TracerVersion = "1.0-test",
                Writer = new BundleWriterInfo { Tool = "fake-tool", Version = "0.0.0", Host = "test-host" },
                TimeRange = new BundleTimeRange
                {
                    StartUtc = now.AddHours(-1),
                    EndUtc = now,
                },
                SessionContext = new BundleSessionContext
                {
                    SessionId = request.SessionId ?? "fake-session",
                    ScenarioId = "fake-scenario",
                    Label = request.LabelOverride,
                },
                ParticipatingNodes = [],
                FastStateScope = request.FastStateScope.ToString(),
                FastStateEntities = [],
                Statistics = new BundleStatistics
                {
                    TotalEvents = 42,
                    TotalSlowStateSamples = 0,
                    TotalFastStateRows = 0,
                    UncompressedBytes = 1024,
                },
                Files = [],
            };

            var manifestPath = Path.Combine(request.OutputPath, BundleLayout.ManifestFile);
            var json = JsonSerializer.Serialize(manifest, BundleManifest.SerializerOptions);
            await File.WriteAllTextAsync(manifestPath, json, ct);

            var timeRange = request.TimeRange
                ?? new TimeRange(WallclockTime.Zero, WallclockTime.Zero);

            return new AggregationResult
            {
                BundleId = bundleId,
                OutputPath = request.OutputPath,
                TimeRange = timeRange,
                Statistics = manifest.Statistics,
                Duration = TimeSpan.FromMilliseconds(1),
                SourceIntervalsUsed = 1,
            };
        }
    }
}
