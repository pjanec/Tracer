using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracer.OfflineViewer;
using Tracer.TestHarness;
using Xunit;

namespace Tracer.Tests.Integration;

[Collection("OfflineViewerSmoke")]
public sealed class OfflineViewerSmokeTests : IAsyncLifetime
{
    private BundleFixture? _bundleFixture;

    public async Task InitializeAsync()
    {
        _bundleFixture = await BundleFixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_bundleFixture is not null)
            await _bundleFixture.DisposeAsync();
    }

    [Fact]
    public async Task OfflineViewer_StartsAndServesBundle()
    {
        var bundlePath = _bundleFixture!.BundlePath;

        // Build and start the offline viewer
        var app = OfflineViewerHostBuilder.Build(bundlePath);
        var config = app.Services.GetRequiredService<OfflineViewerConfig>();

        await app.StartAsync();
        try
        {
            using var http = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{config.HttpPort}/")
            };

            // Poll /api/bundle/current until bundle is loaded (up to 10 seconds)
            var expectedBundleId = _bundleFixture!.Manifest.BundleId;
            string? actualBundleId = null;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < deadline)
            {
                try
                {
                    var res = await http.GetAsync("api/bundle/current");
                    if (res.IsSuccessStatusCode)
                    {
                        var json = await res.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.ValueKind != JsonValueKind.Null &&
                            doc.RootElement.TryGetProperty("bundleId", out var idEl))
                        {
                            actualBundleId = idEl.GetString();
                            break;
                        }
                    }
                }
                catch { /* retry */ }
                await Task.Delay(200);
            }

            Assert.Equal(expectedBundleId, actualBundleId);

            // GET /api/sessions must return non-empty list
            var sessionsRes = await http.GetAsync("api/sessions");
            sessionsRes.EnsureSuccessStatusCode();
            var sessionsJson = await sessionsRes.Content.ReadAsStringAsync();
            using var sessionsDoc = JsonDocument.Parse(sessionsJson);
            Assert.Equal(JsonValueKind.Array, sessionsDoc.RootElement.ValueKind);
            Assert.True(sessionsDoc.RootElement.GetArrayLength() > 0,
                "Expected at least one session in the bundle");
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task OfflineViewer_ExitsCleanlyOnSigint()
    {
        // Tests that the hosted service starts and stops cleanly (simulates SIGINT via StopAsync)
        var app = OfflineViewerHostBuilder.Build(null);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await app.StartAsync(cts.Token);
        // Simulate shutdown — should complete without exception
        await app.StopAsync(cts.Token);
        await app.DisposeAsync();
        // If we reach here without exception, the lifecycle was clean
    }
}
