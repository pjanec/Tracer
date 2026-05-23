using FluentAssertions;
using System.Text.Json;
using Tracer.TestHarness.Observer;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class HealthEndpointTests : IAsyncDisposable
{
    private readonly WebApiFixture _fixture;

    public HealthEndpointTests()
    {
        _fixture = WebApiFixture.CreateAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task GetHealth_Returns200_WithOkStatus()
    {
        var response = await _fixture.Client.GetAsync("/api/health");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("ok");
    }

    [Fact]
    public async Task GetHealth_DoesNotRequireDuckDb()
    {
        // This fixture has no DuckDB; if this request succeeds, health is independent of storage
        var response = await _fixture.Client.GetAsync("/api/health");
        response.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task GetHealth_ResponseContainsTransportFields()
    {
        var response = await _fixture.Client.GetAsync("/api/health");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("sharedMemoryDropped", out _)
            .Should().BeTrue("response should include sharedMemoryDropped field");
        root.TryGetProperty("ingestChannelDepth", out _)
            .Should().BeTrue("response should include ingestChannelDepth field");
    }

    public async ValueTask DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }
}
