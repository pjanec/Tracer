using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Endpoints;
using Tracer.WebApi.Queries;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class TriggerEvalEndpointsTests : IAsyncLifetime
{
    private ObserverFixture _fixture = null!;
    private SessionQueryService _sessions = null!;
    private TriggerEvalService _triggerEvalSvc = null!;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 8_000_000;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    public async Task InitializeAsync()
    {
        _fixture = await ObserverFixture.CreateAsync(
            configureExtraServices: services => services.AddSingleton<TriggerEvalService>());
        _sessions = _fixture.App.Services.GetRequiredService<SessionQueryService>();
        _triggerEvalSvc = _fixture.App.Services.GetRequiredService<TriggerEvalService>();
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private EventRecord MakeSessionStartEvent(string sessionId, DateTimeOffset at)
    {
        var id = _nextId++;
        var payload = $"{{\"sessionId\":\"{sessionId}\",\"scenarioId\":\"TestScenario\"}}";
        return new EventRecord
        {
            SequenceNumber = id,
            PublishWallclock = At(at),
            ReceiveWallclock = At(at),
            PublisherNode = new AgentId("node-a"),
            SubscriberNode = new AgentId("node-a"),
            Topic = new TopicName("system.session_start"),
            EventId = new EventId(id),
            TraceId = new TraceId(id),
            PayloadJson = payload,
        };
    }

    private EventRecord MakeTriggerEvalEvent(string triggerId = "trigger-1", string result = "fired", DateTimeOffset? at = null)
    {
        var id = _nextId++;
        var payload = $"{{\"triggerId\":\"{triggerId}\",\"inputs\":{{\"speed\":10}},\"result\":\"{result}\"}}";
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

    // SC-1
    [Fact]
    public async Task GET_ValidSessionId_Returns200()
    {
        var sessionId = $"session-sc1-{_nextId}";
        await _fixture.PushAsync(new[]
        {
            MakeSessionStartEvent(sessionId, BaseTime.AddSeconds(-1)),
            MakeTriggerEvalEvent(),
        });

        var httpResult = await TriggerEvalEndpoints.HandleAsync(
            sessionId, _sessions, _triggerEvalSvc, CancellationToken.None);

        var ok = httpResult.Result.Should().BeOfType<Ok<TriggerEvaluationListDto>>().Subject;
        ok.StatusCode.Should().Be(200);
        ok.Value!.Evaluations.Should().NotBeEmpty();
    }

    // SC-2
    [Fact]
    public async Task GET_UnknownSessionId_Returns404()
    {
        var httpResult = await TriggerEvalEndpoints.HandleAsync(
            "session-does-not-exist-xyz-999", _sessions, _triggerEvalSvc, CancellationToken.None);

        httpResult.Result.Should().BeOfType<NotFound>();
    }

    // SC-3
    [Fact]
    public async Task GET_InvalidResultParam_ReturnsAll()
    {
        var sessionId = $"session-sc3-{_nextId}";
        await _fixture.PushAsync(new[]
        {
            MakeSessionStartEvent(sessionId, BaseTime.AddSeconds(-1)),
            MakeTriggerEvalEvent(result: "fired"),
            MakeTriggerEvalEvent(result: "not-fired"),
        });

        var httpResult = await TriggerEvalEndpoints.HandleAsync(
            sessionId, _sessions, _triggerEvalSvc, CancellationToken.None,
            result: "garbage");

        var ok = httpResult.Result.Should().BeOfType<Ok<TriggerEvaluationListDto>>().Subject;
        ok.StatusCode.Should().Be(200);
        // All results returned (no filter applied)
        ok.Value!.Evaluations.Should().HaveCountGreaterThanOrEqualTo(2);
        ok.Value.Evaluations.Should().Contain(e => e.Result == "Fired");
        ok.Value.Evaluations.Should().Contain(e => e.Result == "NotFired");
    }

    // SC-4
    [Fact]
    public async Task GET_LimitClamped_ToMaximum()
    {
        var sessionId = $"session-sc4-{_nextId}";
        await _fixture.PushAsync(new[]
        {
            MakeSessionStartEvent(sessionId, BaseTime.AddSeconds(-1)),
            MakeTriggerEvalEvent(),
        });

        // limit=99999 should be clamped to 5000 (no exception)
        var httpResult = await TriggerEvalEndpoints.HandleAsync(
            sessionId, _sessions, _triggerEvalSvc, CancellationToken.None,
            limit: 99999);

        var ok = httpResult.Result.Should().BeOfType<Ok<TriggerEvaluationListDto>>().Subject;
        ok.StatusCode.Should().Be(200);
    }

    // SC-5
    [Fact]
    public void TriggerEvaluationDto_NextEventId_FormattedAsHex16()
    {
        var evaluation = new TriggerEvaluation
        {
            EventId = new Tracer.Core.Identity.EventId(1),
            EvaluatedAtUtc = DateTimeOffset.UtcNow,
            PublisherNode = "node-a",
            TraceId = new Tracer.Core.Identity.TraceId(1),
            TriggerId = "t1",
            Inputs = "{}",
            Result = TriggerResult.Fired,
            NextEventId = new Tracer.Core.Identity.EventId(255UL),
        };

        var dto = TriggerEvalDtoMapper.Map(evaluation);

        dto.NextEventId.Should().Be("00000000000000FF");
    }

    // SC-6
    [Fact]
    public void TriggerEvaluationDto_NullNextEventId_SerializedAsNull()
    {
        var evaluation = new TriggerEvaluation
        {
            EventId = new Tracer.Core.Identity.EventId(1),
            EvaluatedAtUtc = DateTimeOffset.UtcNow,
            PublisherNode = "node-a",
            TraceId = new Tracer.Core.Identity.TraceId(1),
            TriggerId = "t1",
            Inputs = "{}",
            Result = TriggerResult.Fired,
            NextEventId = null,
        };

        var dto = TriggerEvalDtoMapper.Map(evaluation);

        dto.NextEventId.Should().BeNull();
    }

    // SC-7
    [Fact]
    public async Task DI_TriggerEvalService_Resolves()
    {
        // TriggerEvalService was added in InitializeAsync via configureExtraServices
        // Verify it resolves without exception
        var svc = _fixture.App.Services.GetRequiredService<TriggerEvalService>();
        svc.Should().NotBeNull().And.BeOfType<TriggerEvalService>();
        await Task.CompletedTask;
    }
}
