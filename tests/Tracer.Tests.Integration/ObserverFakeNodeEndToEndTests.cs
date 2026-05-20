using Xunit;

namespace Tracer.Tests.Integration;

public sealed class ObserverFakeNodeEndToEndTests
{
    [Fact(Skip = "Deferred to TRC-P3-009")]
    public Task GetSessions_ReturnsActiveSession()
        => Task.CompletedTask;

    [Fact(Skip = "Deferred to TRC-P3-009")]
    public Task GetScenarioNotables_ReturnsNotablesFromScenario()
        => Task.CompletedTask;

    [Fact(Skip = "Deferred to TRC-P3-009")]
    public Task GetScenarioPhases_ReturnsActivePhaseName()
        => Task.CompletedTask;
}
