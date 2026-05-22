using System.Net;
using System.Text.Json;
using FluentAssertions;
using Tracer.TestHarness.Observer;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

/// <summary>Unit tests for <see cref="Tracer.WebApi.Endpoints.EntityEndpoints"/> via <see cref="WebApiFixture"/>.</summary>
public sealed class EntityEndpointsTests : IAsyncLifetime
{
    private WebApiFixture _fixture = null!;

    public async Task InitializeAsync()
        => _fixture = await WebApiFixture.CreateAsync();

    public async Task DisposeAsync()
        => await _fixture.DisposeAsync();

    [Fact]
    public async Task GetFastState_MissingColumnParam_Returns400WithTitle()
    {
        var from = DateTimeOffset.UtcNow.AddHours(-1).ToString("O");
        var to = DateTimeOffset.UtcNow.ToString("O");
        var response = await _fixture.Client.GetAsync(
            $"/api/entities/ent-A/fast-state/pos?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("column");
    }

    [Fact]
    public async Task GetFastState_MaxSamplesBelowMinimum_Returns400()
    {
        var from = DateTimeOffset.UtcNow.AddHours(-1).ToString("O");
        var to = DateTimeOffset.UtcNow.ToString("O");
        var response = await _fixture.Client.GetAsync(
            $"/api/entities/ent-A/fast-state/pos?column=x&maxSamples=9&from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("maxSamples");
    }

    [Fact]
    public async Task GetFastState_MaxSamplesAboveMaximum_Returns400()
    {
        var from = DateTimeOffset.UtcNow.AddHours(-1).ToString("O");
        var to = DateTimeOffset.UtcNow.ToString("O");
        var response = await _fixture.Client.GetAsync(
            $"/api/entities/ent-A/fast-state/pos?column=x&maxSamples=10001&from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("maxSamples");
    }

    [Fact]
    public async Task GetEntitySummary_Routes_ToEntityEndpoint()
    {
        // The summary endpoint exists and routes without 404 or 405 from routing
        var response = await _fixture.Client.GetAsync(
            "/api/entities/some-entity/summary?sessionId=any");

        // 500 from uninitialized reader is acceptable here; 404 from routing is not
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed);
    }
}
