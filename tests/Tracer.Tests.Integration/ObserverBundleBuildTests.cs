using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracer.Adapters.Mock.Storage;
using Tracer.Aggregator;
using Tracer.Bundle.Format;
using Tracer.Core.Abstractions;
using Tracer.TestHarness;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Bundles;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Endpoints;
using Xunit;

namespace Tracer.Tests.Integration;

public sealed class ObserverBundleBuildTests : IAsyncLifetime
{
    private AggregationFixture _nasFixture = null!;
    private ObserverFixture _observer = null!;
    private string _bundlesRoot = null!;

    public async Task InitializeAsync()
    {
        _nasFixture = await AggregationFixture.InitializeAsync();
        _bundlesRoot = Path.Combine(Path.GetTempPath(), $"obs-bundles-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_bundlesRoot);

        var nasRoot = _nasFixture.NasRoot;
        var bundlesRoot = _bundlesRoot;

        _observer = await ObserverFixture.CreateAsync(
            configureExtraServices: services =>
            {
                services.AddSingleton<BundleCatalog>(sp =>
                    new BundleCatalog(bundlesRoot, sp.GetRequiredService<ILogger<BundleCatalog>>()));
                services.AddSingleton<ITelemetryStorageReader>(sp =>
                    new LocalFileSystemStorageReader(nasRoot,
                        sp.GetRequiredService<ILogger<LocalFileSystemStorageReader>>()));
                services.AddSingleton<IAggregationOrchestrator>(sp =>
                    new AggregationOrchestrator(
                        sp.GetRequiredService<ITelemetryStorageReader>(),
                        sp.GetRequiredService<ILogger<AggregationOrchestrator>>()));
                services.AddSingleton<BundleBuildService>();
            },
            configureExtraApp: app => BundleEndpoints.Map(app));
    }

    public async Task DisposeAsync()
    {
        await _observer.DisposeAsync();
        await _nasFixture.DisposeAsync();
        try { Directory.Delete(_bundlesRoot, recursive: true); } catch { /* best effort */ }
    }

    // SC1: POST /api/bundles/build with real NAS time range → 202 Accepted, completes
    [Fact]
    public async Task PostBuild_WithNasTimeRange_Returns202AndCompletes()
    {
        var nasRange = _nasFixture.NasTimeRange;
        var request = new BundleBuildRequestDto
        {
            TimeRange = new TimeRangeDto
            {
                StartUtc = nasRange.StartUtc.ToDateTimeOffset(),
                EndUtc = nasRange.EndUtc.ToDateTimeOffset(),
            }
        };

        var postResponse = await _observer.Client.PostAsJsonAsync("/api/bundles/build", request);
        postResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var accepted = await postResponse.Content.ReadFromJsonAsync<BundleBuildAcceptedDto>();
        accepted.Should().NotBeNull();
        accepted!.BundleId.Should().NotBeNullOrWhiteSpace();

        var status = await PollUntilDoneAsync(accepted.BundleId, timeoutSeconds: 60);
        status.State.Should().Be("Completed", $"build failed with: {status.Error}");
        status.CompletedAtUtc.Should().NotBeNull();
    }

    // SC2: After build, GET status shows Completed with OutputPath
    [Fact]
    public async Task GetStatus_AfterRealBuild_ShowsCompletedWithPath()
    {
        var bundleId = await StartBuildAsync();
        var status = await PollUntilDoneAsync(bundleId, timeoutSeconds: 60);

        status.State.Should().Be("Completed");
        status.OutputPath.Should().NotBeNullOrWhiteSpace();
        (Directory.Exists(status.OutputPath) || File.Exists(status.OutputPath!)).Should().BeTrue();
    }

    // SC3: After build, GET /api/bundles/{id} returns a manifest DTO with real events
    [Fact]
    public async Task GetManifest_AfterRealBuild_ReturnsParsedManifestWithEvents()
    {
        var bundleId = await StartBuildAsync();
        await PollUntilDoneAsync(bundleId, timeoutSeconds: 60);

        var manifestResponse = await _observer.Client.GetAsync($"/api/bundles/{bundleId}");
        manifestResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var manifest = await manifestResponse.Content.ReadFromJsonAsync<BundleManifestDto>();
        manifest.Should().NotBeNull();
        manifest!.Statistics.TotalEvents.Should().BeGreaterThan(0);
        manifest.TracerVersion.Should().NotBeNullOrWhiteSpace();
    }

    // SC4: GET /api/bundles returns the built bundle in the list
    [Fact]
    public async Task ListBundles_AfterRealBuild_IncludesEntry()
    {
        var bundleId = await StartBuildAsync();
        await PollUntilDoneAsync(bundleId, timeoutSeconds: 60);

        var listResponse = await _observer.Client.GetAsync("/api/bundles");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await listResponse.Content.ReadFromJsonAsync<BundleListDto>();
        list!.Bundles.Should().Contain(e => e.BundleId == bundleId);
        list.Bundles.Should().Contain(e => e.SizeBytes > 0);
    }

    // SC5: Download returns a zip containing manifest.json
    [Fact]
    public async Task Download_AfterRealBuild_ReturnsZipWithManifest()
    {
        var bundleId = await StartBuildAsync();
        await PollUntilDoneAsync(bundleId, timeoutSeconds: 60);

        var downloadResponse = await _observer.Client.GetAsync($"/api/bundles/{bundleId}/download");
        downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        downloadResponse.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");

        var zipBytes = await downloadResponse.Content.ReadAsByteArrayAsync();
        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        archive.Entries.Should().Contain(e => e.Name == BundleLayout.ManifestFile);
    }

    // SC6: DELETE removes the bundle from the catalog and disk
    [Fact]
    public async Task Delete_AfterRealBuild_Returns204AndRemovesEntry()
    {
        var bundleId = await StartBuildAsync();
        var status = await PollUntilDoneAsync(bundleId, timeoutSeconds: 60);
        var outputPath = status.OutputPath;

        var deleteResponse = await _observer.Client.DeleteAsync($"/api/bundles/{bundleId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var manifestResponse = await _observer.Client.GetAsync($"/api/bundles/{bundleId}");
        manifestResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        if (outputPath is not null)
        {
            Directory.Exists(outputPath).Should().BeFalse();
            File.Exists(outputPath).Should().BeFalse();
        }
    }

    // ── TRC-P4-013: Additional required test methods (spec-mandated names) ──

    [Fact]
    public async Task PostBundleBuild_ReturnsAcceptedWithBundleId()
    {
        var nasRange = _nasFixture.NasTimeRange;
        var request = new BundleBuildRequestDto
        {
            TimeRange = new TimeRangeDto
            {
                StartUtc = nasRange.StartUtc.ToDateTimeOffset(),
                EndUtc = nasRange.EndUtc.ToDateTimeOffset(),
            }
        };

        var postResponse = await _observer.Client.PostAsJsonAsync("/api/bundles/build", request);
        postResponse.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "POST /api/bundles/build must return 202 Accepted");

        var accepted = await postResponse.Content.ReadFromJsonAsync<BundleBuildAcceptedDto>();
        accepted.Should().NotBeNull();
        accepted!.BundleId.Should().NotBeNullOrWhiteSpace(
            "response body must contain a non-empty bundleId");
    }

    [Fact]
    public async Task GetStatus_AfterBuild_ShowsCompleted()
    {
        var bundleId = await StartBuildAsync();
        var status = await PollUntilDoneAsync(bundleId, timeoutSeconds: 60);

        status.State.Should().Be("Completed",
            "bundle build must reach Completed state within the timeout");
    }

    [Fact]
    public async Task GetDownload_ReturnsValidZip()
    {
        var bundleId = await StartBuildAsync();
        await PollUntilDoneAsync(bundleId, timeoutSeconds: 60);

        var downloadResponse = await _observer.Client.GetAsync($"/api/bundles/{bundleId}/download");
        downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "download endpoint must return 200 OK");
        downloadResponse.Content.Headers.ContentType!.MediaType.Should().Be("application/zip",
            "response must have application/zip content type");

        var zipBytes = await downloadResponse.Content.ReadAsByteArrayAsync();
        using var archive = new System.IO.Compression.ZipArchive(
            new MemoryStream(zipBytes), System.IO.Compression.ZipArchiveMode.Read);
        archive.Entries.Should().Contain(e => e.Name == BundleLayout.ManifestFile,
            "zip archive must contain manifest.json at the root");
    }

    [Fact]
    public async Task DeleteBundle_RemovesFromDisk()
    {
        var bundleId = await StartBuildAsync();
        var status = await PollUntilDoneAsync(bundleId, timeoutSeconds: 60);
        var outputPath = status.OutputPath;

        var deleteResponse = await _observer.Client.DeleteAsync($"/api/bundles/{bundleId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "DELETE must return 204 No Content");

        if (outputPath is not null)
        {
            Directory.Exists(outputPath).Should().BeFalse(
                "bundle directory should be removed from disk after deletion");
            File.Exists(outputPath).Should().BeFalse(
                "bundle zip should be removed from disk after deletion");
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<string> StartBuildAsync()
    {
        var nasRange = _nasFixture.NasTimeRange;
        var request = new BundleBuildRequestDto
        {
            TimeRange = new TimeRangeDto
            {
                StartUtc = nasRange.StartUtc.ToDateTimeOffset(),
                EndUtc = nasRange.EndUtc.ToDateTimeOffset(),
            }
        };
        var postResponse = await _observer.Client.PostAsJsonAsync("/api/bundles/build", request);
        postResponse.EnsureSuccessStatusCode();
        var accepted = await postResponse.Content.ReadFromJsonAsync<BundleBuildAcceptedDto>();
        return accepted!.BundleId;
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
}
