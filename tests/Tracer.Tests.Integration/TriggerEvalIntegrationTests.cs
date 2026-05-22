using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
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

namespace Tracer.Tests.Integration;

/// <summary>
/// Integration tests: push synthetic trigger_evaluated events into Observer,
/// query via HTTP, verify filters work correctly.
/// TRC-P8-018 backend integration scope.
/// </summary>
[Collection("TriggerEvalIntegration")]
public sealed class TriggerEvalIntegrationTests : IAsyncLifetime
{
    private ObserverFixture? _observer;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 77_000_000;

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromDateTimeOffset(dto);

    public async Task InitializeAsync()
    {
        _observer = await ObserverFixture.CreateAsync(
            configureExtraServices: services =>
                services.AddSingleton<TriggerEvalService>(),
            configureExtraApp: app =>
                TriggerEvalEndpoints.Map(app));

        // Push a session_start event so SessionQueryService.GetAsync("s1") finds a valid session.
        var sessionStartEvent = new EventRecord
        {
            SequenceNumber = 0,
            PublishWallclock = At(BaseTime.AddSeconds(-5)),
            ReceiveWallclock = At(BaseTime.AddSeconds(-5)),
            PublisherNode = new AgentId("node-trig"),
            SubscriberNode = new AgentId("node-trig"),
            Topic = new TopicName("system.session_start"),
            EventId = new EventId(999_000),
            TraceId = new TraceId(999_000),
            PayloadJson = "{\"sessionId\":\"s1\",\"scenarioId\":null,\"label\":\"Test Session\"}",
        };
        await _observer.PushAsync(sessionStartEvent);
    }

    public async Task DisposeAsync()
    {
        if (_observer is not null)
            await _observer.DisposeAsync();
    }

    private EventRecord MakeTriggerEvent(
        string triggerId,
        string result,
        DateTimeOffset? at = null)
    {
        var id = _nextId++;
        var payload =
            $"{{\"triggerId\":\"{triggerId}\",\"inputs\":{{\"v\":1}},\"result\":\"{result}\"}}";
        return new EventRecord
        {
            SequenceNumber = id,
            PublishWallclock = At(at ?? BaseTime),
            ReceiveWallclock = At(at ?? BaseTime),
            PublisherNode = new AgentId("node-trig"),
            SubscriberNode = new AgentId("node-trig"),
            Topic = new TopicName("scenario.trigger_evaluated"),
            EventId = new EventId(id),
            TraceId = new TraceId(id),
            PayloadJson = payload,
        };
    }

    private static string BuildTriggerUrl(
        string sessionId,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        string? triggerId = null,
        string? result = null)
    {
        var fromStr = Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
        var toStr   = Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
        var url = $"/api/scenario/triggers?sessionId={sessionId}&from={fromStr}&to={toStr}&limit={limit}";
        if (triggerId is not null) url += $"&triggerId={Uri.EscapeDataString(triggerId)}";
        if (result    is not null) url += $"&result={Uri.EscapeDataString(result)}";
        return url;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TriggerEval_HTTP_ReturnsEvents()
    {
        var suffix = _nextId.ToString();
        await _observer!.PushAsync(new[]
        {
            MakeTriggerEvent($"trigger-A-{suffix}", "fired"),
            MakeTriggerEvent($"trigger-A-{suffix}", "not-fired"),
            MakeTriggerEvent($"trigger-B-{suffix}", "fired"),
        });

        var url = BuildTriggerUrl(
            "s1",
            BaseTime.AddSeconds(-1),
            BaseTime.AddSeconds(60),
            100);

        var resp = await _observer.Client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<TriggerEvaluationListDto>(CamelCaseOptions);
        body.Should().NotBeNull();
        body!.Evaluations.Should().NotBeEmpty(
            "pushed trigger events must appear in the HTTP response");
    }

    [Fact]
    public async Task TriggerEval_HTTP_FilterByTriggerId()
    {
        var suffix = _nextId.ToString();
        await _observer!.PushAsync(new[]
        {
            MakeTriggerEvent($"trigger-X-{suffix}", "fired"),
            MakeTriggerEvent($"trigger-Y-{suffix}", "fired"),
        });

        var url = BuildTriggerUrl(
            "s1",
            BaseTime.AddSeconds(-1),
            BaseTime.AddSeconds(60),
            100,
            triggerId: $"trigger-X-{suffix}");

        var resp = await _observer.Client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<TriggerEvaluationListDto>(CamelCaseOptions);
        body!.Evaluations.Should().NotBeEmpty();
        body.Evaluations.Should().AllSatisfy(e =>
            e.TriggerId.Should().Contain($"trigger-X-{suffix}"),
            "filtering by triggerId must exclude other triggers");
    }

    [Fact]
    public async Task TriggerEval_HTTP_FilterByResult()
    {
        var suffix = _nextId.ToString();
        await _observer!.PushAsync(new[]
        {
            MakeTriggerEvent($"fired-trig-{suffix}", "fired"),
            MakeTriggerEvent($"fired-trig-{suffix}", "fired"),
            MakeTriggerEvent($"notfired-trig-{suffix}", "not-fired"),
        });

        var url = BuildTriggerUrl(
            "s1",
            BaseTime.AddSeconds(-1),
            BaseTime.AddSeconds(60),
            100,
            result: "Fired");

        var resp = await _observer.Client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<TriggerEvaluationListDto>(CamelCaseOptions);
        body!.Evaluations.Should().NotBeEmpty();
        body.Evaluations.Should().AllSatisfy(e =>
            e.Result.Should().Be("Fired"),
            "result=Fired filter must only return Fired evaluations");
    }
}
