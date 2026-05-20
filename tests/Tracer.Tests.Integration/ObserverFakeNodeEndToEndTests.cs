using Xunit;

namespace Tracer.Tests.Integration;

public sealed class ObserverFakeNodeEndToEndTests
{
    [Fact(Skip = "Deferred to TRC-P3-009")]
    public Task Observer_ReceivesFakeNodeEvents_PersistsToStorage()
        => Task.CompletedTask;

    [Fact(Skip = "Deferred to TRC-P3-009")]
    public Task Observer_QueryApi_ReturnsIngestedEvents()
        => Task.CompletedTask;

    [Fact(Skip = "Deferred to TRC-P3-009")]
    public Task Observer_HealthEndpoint_Returns200_WhenLive()
        => Task.CompletedTask;
}
