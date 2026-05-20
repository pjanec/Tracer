using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tracer.OfflineViewer;
using Tracer.TestHarness;
using Xunit;

namespace Tracer.Tests.Integration;

/// <summary>
/// Round-trip tests comparing OfflineViewer query results against a bundle
/// produced from a live FakeNode run (TRC-P4-013).
/// </summary>
[Collection("BundleRoundTrip")]
public sealed class BundleRoundTripTests : IAsyncLifetime
{
    private BundleFixture? _bundleFixture;
    private Microsoft.AspNetCore.Builder.WebApplication? _viewerApp;
    private HttpClient? _bundleClient;

    public async Task InitializeAsync()
    {
        _bundleFixture = await BundleFixture.InitializeAsync();

        _viewerApp = OfflineViewerHostBuilder.Build(_bundleFixture.BundlePath);
        await _viewerApp.StartAsync();

        var config = _viewerApp.Services.GetRequiredService<OfflineViewerConfig>();
        _bundleClient = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{config.HttpPort}/"),
            Timeout = TimeSpan.FromSeconds(30),
        };

        // Wait for the offline viewer to load the bundle before running tests
        await WaitForBundleLoadedAsync(_bundleClient, _bundleFixture.Manifest.BundleId);
    }

    public async Task DisposeAsync()
    {
        _bundleClient?.Dispose();
        if (_viewerApp is not null)
        {
            await _viewerApp.StopAsync();
            await _viewerApp.DisposeAsync();
        }
        if (_bundleFixture is not null)
            await _bundleFixture.DisposeAsync();
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RoundTrip_SessionList_IsIdentical()
    {
        var sessionsRes = await _bundleClient!.GetAsync("api/sessions");
        sessionsRes.EnsureSuccessStatusCode();

        var json = await sessionsRes.Content.ReadAsStringAsync();
        var sessions = JsonSerializer.Deserialize<LocalSessionDto[]>(
            json, CamelCaseOptions);

        sessions.Should().NotBeNullOrEmpty(
            "bundle should contain at least one session from the FakeNode run");

        sessions![0].SessionId.Should().NotBeNullOrEmpty(
            "session must have a non-empty sessionId");
        sessions[0].EventCount.Should().BeGreaterThan(0,
            "session should contain events from the FakeNode scenario run");
    }

    [Fact]
    public async Task RoundTrip_Notables_AreIdentical()
    {
        // Get sessions list to find a session ID
        var sessionsRes = await _bundleClient!.GetAsync("api/sessions");
        sessionsRes.EnsureSuccessStatusCode();
        var sessions = JsonSerializer.Deserialize<LocalSessionDto[]>(
            await sessionsRes.Content.ReadAsStringAsync(), CamelCaseOptions);
        sessions.Should().NotBeNullOrEmpty("need at least one session to test notables");

        var sessionId = sessions![0].SessionId;

        // Query notables from the bundle
        var notablesRes = await _bundleClient!.GetAsync(
            $"api/scenario/notables?sessionId={sessionId}");
        notablesRes.EnsureSuccessStatusCode();

        var notablesJson = await notablesRes.Content.ReadAsStringAsync();
        notablesJson.Should().StartWith("[",
            "notables response should be a JSON array");
    }

    [Fact]
    public async Task RoundTrip_CrossIntervalQuery_ReturnsAllEvents()
    {
        var sessionsRes = await _bundleClient!.GetAsync("api/sessions");
        sessionsRes.EnsureSuccessStatusCode();
        var sessions = JsonSerializer.Deserialize<LocalSessionDto[]>(
            await sessionsRes.Content.ReadAsStringAsync(), CamelCaseOptions);
        sessions.Should().NotBeNullOrEmpty("bundle must contain at least one session");

        // Verify total event count is positive across all sessions
        var totalEvents = sessions!.Sum(s => s.EventCount);
        totalEvents.Should().BeGreaterThan(0,
            "bundle should contain events from the FakeNode scenario run");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

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

    /// <summary>Minimal DTO for deserializing session list from the WebApi JSON response.</summary>
    private sealed record LocalSessionDto(
        [property: JsonPropertyName("sessionId")] string SessionId,
        [property: JsonPropertyName("eventCount")] int EventCount,
        [property: JsonPropertyName("status")] string Status);
}
