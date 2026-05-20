using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tracer.Storage.DuckDB.MultiInterval;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Lifecycle;
using Tracer.WebApi.Queries;
using Xunit;

namespace Tracer.Tests.Unit.Observer;

/// <summary>
/// Verifies that the <see cref="ObserverHostBuilder"/> DI container
/// wires query services to <see cref="LiveMultiIntervalReader"/>.
/// </summary>
public sealed class ObserverDiTests : IAsyncLifetime
{
    private ObserverFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = await ObserverFixture.CreateAsync();
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    /// <summary>
    /// All query services should resolve and accept <see cref="LiveMultiIntervalReader"/>;
    /// no <see cref="ReadOnlyConnectionPool"/> should exist in the container.
    /// </summary>
    [Fact]
    public void QueryServices_UseLiveMultiIntervalReader_NotSinglePool()
    {
        var services = _fixture.App.Services;

        // LiveMultiIntervalReader must be registered
        var reader = services.GetService<LiveMultiIntervalReader>();
        reader.Should().NotBeNull("LiveMultiIntervalReader must be registered as a singleton");

        // All four query services must be resolvable (no missing dependency exceptions)
        services.GetService<SessionQueryService>().Should().NotBeNull();
        services.GetService<ScenarioQueryService>().Should().NotBeNull();
        services.GetService<TopologyQueryService>().Should().NotBeNull();
        services.GetService<EventLookupService>().Should().NotBeNull();

        // ReadOnlyConnectionPool must NOT be registered
        var pool = services.GetService<ReadOnlyConnectionPool>();
        pool.Should().BeNull("ReadOnlyConnectionPool must have been removed from the DI container");
    }
}
