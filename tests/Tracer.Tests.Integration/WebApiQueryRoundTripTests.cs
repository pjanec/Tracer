using Xunit;

namespace Tracer.Tests.Integration;

public class WebApiQueryRoundTripTests
{
    [Fact(Skip = "Deferred to TRC-P3-010")]
    public Task GetSessions_AfterIngestion_ReturnsCorrectSessions() => Task.CompletedTask;

    [Fact(Skip = "Deferred to TRC-P3-010")]
    public Task GetSession_ById_ReturnsMatchingDto() => Task.CompletedTask;

    [Fact(Skip = "Deferred to TRC-P3-010")]
    public Task GetTopology_ReflectsPublishingNodes() => Task.CompletedTask;

    [Fact(Skip = "Deferred to TRC-P3-010")]
    public Task GetNotables_AcrossIntervalRotation() => Task.CompletedTask;

    [Fact(Skip = "Deferred to TRC-P3-010")]
    public Task GetPhases_WithMultipleScenarioPhases() => Task.CompletedTask;

    [Fact(Skip = "Deferred to TRC-P3-010")]
    public Task GetState_ReflectsLiveAggregates() => Task.CompletedTask;

    [Fact(Skip = "Deferred to TRC-P3-010")]
    public Task GetEvent_ById_ReturnsCorrectDto() => Task.CompletedTask;

    [Fact(Skip = "Deferred to TRC-P3-010")]
    public Task TimeRangeFilter_ExcludesOutOfRangeSessions() => Task.CompletedTask;

    [Fact(Skip = "Deferred to TRC-P3-010")]
    public Task MultiInterval_QueriesSpanBothIntervals() => Task.CompletedTask;
}
