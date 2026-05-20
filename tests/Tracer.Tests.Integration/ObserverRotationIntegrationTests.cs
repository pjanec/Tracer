using Xunit;

namespace Tracer.Tests.Integration;

public sealed class ObserverRotationIntegrationTests
{
    [Fact(Skip = "Deferred to TRC-P3-009")]
    public Task FirstInterval_FinalizedWithReady_AfterRotation()
        => Task.CompletedTask;

    [Fact(Skip = "Deferred to TRC-P3-009")]
    public Task SecondInterval_QueriesReturnCurrentIntervalEvents()
        => Task.CompletedTask;

    [Fact(Skip = "Deferred to TRC-P3-009")]
    public Task Queries_DuringRotation_SucceedAfterBriefBlock()
        => Task.CompletedTask;

    [Fact(Skip = "Deferred to TRC-P3-009")]
    public Task MultipleNodes_EventsFromAllNodesIngested()
        => Task.CompletedTask;
}
