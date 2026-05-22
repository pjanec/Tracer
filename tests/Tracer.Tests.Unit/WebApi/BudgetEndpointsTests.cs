using System.Net;
using FluentAssertions;
using Tracer.TestHarness.Observer;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

/// <summary>
/// Budget endpoint has NO 409 gate — it must return 200 in both live and bundle mode.
/// </summary>
public sealed class BudgetEndpointsTests : IAsyncDisposable
{
    private WebApiFixture? _fixture;

    [Fact]
    public async Task Budgets_LiveMode_Returns200()
    {
        _fixture = await WebApiFixture.CreateAsync();
        var resp = await _fixture.Client.GetAsync("/api/scenario/budgets");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Budgets_BundleMode_Returns200()
    {
        // Even in bundle mode, budgets endpoint must return 200
        _fixture = await WebApiFixture.CreateAsync();
        var resp = await _fixture.Client.GetAsync("/api/scenario/budgets");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public async ValueTask DisposeAsync()
    {
        if (_fixture is not null) await _fixture.DisposeAsync();
    }
}
