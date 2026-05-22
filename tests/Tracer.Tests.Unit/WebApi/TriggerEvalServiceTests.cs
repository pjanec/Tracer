using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Queries;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class TriggerEvalServiceTests : IAsyncLifetime
{
    private ObserverFixture _fixture = null!;
    private TriggerEvalService _svc = null!;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 9_000_000;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private static WallclockTime FromTime => At(BaseTime.AddSeconds(-1));
    private static WallclockTime ToTime   => At(BaseTime.AddSeconds(60));

    private EventRecord MakeTriggerEvalEvent(
        string triggerId = "trigger-1",
        string result = "fired",
        DateTimeOffset? at = null,
        string? triggerLabel = null,
        ulong? nextEventId = null,
        string? reason = null,
        string inputs = """{"speed":12}""")
    {
        var id = _nextId++;
        var nextEventIdJson = nextEventId.HasValue
            ? $", \"nextEventId\": \"{nextEventId.Value:X16}\""
            : "";
        var reasonJson = reason is not null
            ? $", \"reason\": \"{reason}\""
            : "";
        var labelJson = triggerLabel is not null
            ? $", \"triggerLabel\": \"{triggerLabel}\""
            : "";
        var payload = $"{{\"triggerId\":\"{triggerId}\"{labelJson},\"inputs\":{inputs},\"result\":\"{result}\"{nextEventIdJson}{reasonJson}}}";

        return new EventRecord
        {
            SequenceNumber = id,
            PublishWallclock = At(at ?? BaseTime),
            ReceiveWallclock = At(at ?? BaseTime),
            PublisherNode = new AgentId("node-a"),
            SubscriberNode = new AgentId("node-a"),
            Topic = new TopicName("scenario.trigger_evaluated"),
            EventId = new EventId(id),
            TraceId = new TraceId(id),
            PayloadJson = payload,
        };
    }

    private EventRecord MakeOtherEvent(string topic = "game.tick")
    {
        var id = _nextId++;
        return new EventRecord
        {
            SequenceNumber = id,
            PublishWallclock = At(BaseTime),
            ReceiveWallclock = At(BaseTime),
            PublisherNode = new AgentId("node-a"),
            SubscriberNode = new AgentId("node-a"),
            Topic = new TopicName(topic),
            EventId = new EventId(id),
            TraceId = new TraceId(id),
            PayloadJson = "{}",
        };
    }

    public async Task InitializeAsync()
    {
        _fixture = await ObserverFixture.CreateAsync(
            configureExtraServices: services => services.AddSingleton<TriggerEvalService>());
        _svc = _fixture.App.Services.GetRequiredService<TriggerEvalService>();
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // SC-1
    [Fact]
    public async Task ListAsync_OnlyReturnsTriggerEvaluatedEvents()
    {
        var events = new List<EventRecord>
        {
            MakeTriggerEvalEvent(),
            MakeTriggerEvalEvent(),
            MakeOtherEvent("game.tick"),
            MakeOtherEvent("system.session_start"),
        };
        await _fixture.PushAsync(events);

        var result = await _svc.ListAsync(
            "s1", FromTime, ToTime,
            triggerIdFilter: null, resultFilter: null, limit: 100,
            CancellationToken.None);

        result.Evaluations.Should().HaveCount(2);
        result.Evaluations.Should().AllSatisfy(e =>
            e.TriggerId.Should().NotBeNullOrEmpty());
    }

    // SC-2
    [Fact]
    public async Task ListAsync_FilterByTriggerId()
    {
        var suffix = _nextId.ToString();
        var events = new List<EventRecord>
        {
            MakeTriggerEvalEvent(triggerId: $"trigger-A-{suffix}"),
            MakeTriggerEvalEvent(triggerId: $"trigger-A-{suffix}"),
            MakeTriggerEvalEvent(triggerId: $"trigger-B-{suffix}"),
        };
        await _fixture.PushAsync(events);

        var result = await _svc.ListAsync(
            "s1", FromTime, ToTime,
            triggerIdFilter: $"trigger-A-{suffix}", resultFilter: null, limit: 100,
            CancellationToken.None);

        result.Evaluations.Should().HaveCountGreaterThanOrEqualTo(2);
        result.Evaluations.Should().AllSatisfy(e =>
            e.TriggerId.Should().Be($"trigger-A-{suffix}"));
        result.Evaluations.Should().NotContain(e => e.TriggerId == $"trigger-B-{suffix}");
    }

    // SC-3
    [Fact]
    public async Task ListAsync_FilterByResult_Fired()
    {
        var suffix = _nextId.ToString();
        var events = new List<EventRecord>
        {
            MakeTriggerEvalEvent(triggerId: $"fired-{suffix}", result: "fired"),
            MakeTriggerEvalEvent(triggerId: $"fired-{suffix}", result: "fired"),
            MakeTriggerEvalEvent(triggerId: $"notfired-{suffix}", result: "not-fired"),
        };
        await _fixture.PushAsync(events);

        var result = await _svc.ListAsync(
            "s1", FromTime, ToTime,
            triggerIdFilter: null, resultFilter: TriggerResult.Fired, limit: 100,
            CancellationToken.None);

        result.Evaluations.Should().NotBeEmpty();
        result.Evaluations.Should().AllSatisfy(e =>
            e.Result.Should().Be(TriggerResult.Fired));
    }

    // SC-4
    [Fact]
    public async Task ListAsync_TimeRangeRespected()
    {
        var beforeRange = BaseTime.AddSeconds(-5);
        var inRange = BaseTime.AddSeconds(5);

        var suffix = _nextId.ToString();
        var events = new List<EventRecord>
        {
            MakeTriggerEvalEvent(triggerId: $"before-{suffix}", at: beforeRange),
            MakeTriggerEvalEvent(triggerId: $"inrange-{suffix}", at: inRange),
        };
        await _fixture.PushAsync(events);

        // Query only the range [BaseTime, BaseTime+30s)
        var from = At(BaseTime);
        var to   = At(BaseTime.AddSeconds(30));

        var result = await _svc.ListAsync(
            "s1", from, to,
            triggerIdFilter: null, resultFilter: null, limit: 100,
            CancellationToken.None);

        result.Evaluations.Should().Contain(e => e.TriggerId == $"inrange-{suffix}");
        result.Evaluations.Should().NotContain(e => e.TriggerId == $"before-{suffix}");
    }

    // SC-5
    [Fact]
    public async Task ParseEvaluation_ExtractsAllPayloadFields()
    {
        var id = _nextId++;
        var payload = """{"triggerId":"t1","triggerLabel":"My Trigger","inputs":{"speed":12},"result":"fired","nextEventId":"00000000000000FF"}""";
        var ev = new EventRecord
        {
            SequenceNumber = id,
            PublishWallclock = At(BaseTime),
            ReceiveWallclock = At(BaseTime),
            PublisherNode = new AgentId("node-a"),
            SubscriberNode = new AgentId("node-a"),
            Topic = new TopicName("scenario.trigger_evaluated"),
            EventId = new EventId(id),
            TraceId = new TraceId(id),
            PayloadJson = payload,
        };
        await _fixture.PushAsync(ev);

        var result = await _svc.ListAsync(
            "s1", FromTime, ToTime,
            triggerIdFilter: "t1", resultFilter: null, limit: 100,
            CancellationToken.None);

        var eval = result.Evaluations.Should().ContainSingle(e => e.TriggerId == "t1").Subject;
        eval.TriggerId.Should().Be("t1");
        eval.TriggerLabel.Should().Be("My Trigger");
        eval.Result.Should().Be(TriggerResult.Fired);
        eval.Inputs.Should().Contain("speed");
        eval.NextEventId.Should().NotBeNull();
        eval.NextEventId!.Value.Value.Should().Be(255UL);
    }

    // SC-6
    [Fact]
    public async Task ParseEvaluation_NotFiredResult()
    {
        var suffix = _nextId.ToString();
        await _fixture.PushAsync(MakeTriggerEvalEvent(triggerId: $"notfired-sc6-{suffix}", result: "not-fired"));

        var result = await _svc.ListAsync(
            "s1", FromTime, ToTime,
            triggerIdFilter: $"notfired-sc6-{suffix}", resultFilter: null, limit: 100,
            CancellationToken.None);

        result.Evaluations.Should().ContainSingle()
            .Which.Result.Should().Be(TriggerResult.NotFired);
    }

    // SC-7
    [Fact]
    public async Task ParseEvaluation_MalformedPayload_ReturnsDegradedResult()
    {
        var id = _nextId++;
        var ev = new EventRecord
        {
            SequenceNumber = id,
            PublishWallclock = At(BaseTime),
            ReceiveWallclock = At(BaseTime),
            PublisherNode = new AgentId("node-a"),
            SubscriberNode = new AgentId("node-a"),
            Topic = new TopicName("scenario.trigger_evaluated"),
            EventId = new EventId(id),
            TraceId = new TraceId(id),
            PayloadJson = "not-json",
        };
        await _fixture.PushAsync(ev);

        // Should not throw
        var act = async () => await _svc.ListAsync(
            "s1", FromTime, ToTime,
            triggerIdFilter: null, resultFilter: null, limit: 100,
            CancellationToken.None);

        var result = await act.Should().NotThrowAsync();
        var malformed = result.Subject.Evaluations.Should().Contain(e =>
            e.TriggerId == "(malformed payload)").Subject;
        malformed.Inputs.Should().Be("not-json");
        malformed.Result.Should().Be(TriggerResult.NotFired);
    }

    // SC-8
    [Fact]
    public async Task ListAsync_EmptyResult_NoException()
    {
        // No scenario.trigger_evaluated events pushed — just other topics
        await _fixture.PushAsync(MakeOtherEvent("game.tick"));

        var act = async () => await _svc.ListAsync(
            "s1", FromTime, ToTime,
            triggerIdFilter: null, resultFilter: null, limit: 100,
            CancellationToken.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.Evaluations.Should().BeEmpty();
    }

    // SC-9
    [Fact]
    public async Task ListAsync_LimitRespected()
    {
        var events = Enumerable.Range(0, 50)
            .Select(_ => MakeTriggerEvalEvent())
            .ToList();
        await _fixture.PushAsync(events);

        var result = await _svc.ListAsync(
            "s1", FromTime, ToTime,
            triggerIdFilter: null, resultFilter: null, limit: 5,
            CancellationToken.None);

        result.Evaluations.Count.Should().Be(5);
    }
}
