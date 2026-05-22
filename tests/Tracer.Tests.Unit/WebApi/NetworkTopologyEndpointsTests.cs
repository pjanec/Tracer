using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Util;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class NetworkTopologyEndpointsTests : IAsyncDisposable
{
    private WebApiFixture? _webFixture;
    private ObserverFixture? _observerFixture;

    private sealed class TestBundleSentinel : IBundleModeMarker { }

    [Fact]
    public async Task NetworkTopology_LiveMode_Returns409()
    {
        _webFixture = await WebApiFixture.CreateAsync();
        var resp = await _webFixture.Client.GetAsync(
            "/api/topology/network?from=2026-01-01T00:00:00Z&to=2026-01-01T01:00:00Z");
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task NetworkTopology_BundleMode_Returns200()
    {
        _observerFixture = await ObserverFixture.CreateAsync(
            configureExtraServices: s => s.AddSingleton<IBundleModeMarker, TestBundleSentinel>());
        var resp = await _observerFixture.Client.GetAsync(
            "/api/topology/network?from=2026-01-01T00:00:00Z&to=2026-01-01T01:00:00Z");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public async ValueTask DisposeAsync()
    {
        if (_webFixture is not null) await _webFixture.DisposeAsync();
        if (_observerFixture is not null) await _observerFixture.DisposeAsync();
    }
}
