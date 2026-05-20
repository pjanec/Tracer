using FluentAssertions;
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

    public async ValueTask DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }
}
