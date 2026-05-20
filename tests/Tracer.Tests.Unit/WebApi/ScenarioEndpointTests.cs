using System.Net;
using System.Text.Json;
using FluentAssertions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.TestHarness.Observer;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class ScenarioEndpointTests : IAsyncDisposable
{
    private readonly ObserverFixture _fixture;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2025, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 200;

    private static EventRecord MakeNotable(
        string sessionId,
        string notableLabel,
        string nodeId = "node-alpha",
        DateTimeOffset? at = null)
    {
        var payload = JsonSerializer.Serialize(new { sessionId });
        return new EventRecord
        {
            SequenceNumber = _nextId++,
            PublishWallclock = At(at ?? BaseTime),
            ReceiveWallclock = At(at ?? BaseTime),
            PublisherNode = new AgentId(nodeId),
            SubscriberNode = new AgentId(nodeId),
            Topic = new TopicName("combat.hit"),
            EventId = new EventId(_nextId++),
            TraceId = new TraceId(_nextId++),
            PayloadJson = payload,
            NotableLabel = notableLabel,
            Severity = Severity.Warning,
        };
    }

    private static EventRecord MakeNonNotable(string sessionId, string nodeId = "node-alpha")
    {
        var payload = JsonSerializer.Serialize(new { sessionId });
        return new EventRecord
        {
            SequenceNumber = _nextId++,
            PublishWallclock = At(BaseTime),
            ReceiveWallclock = At(BaseTime),
            PublisherNode = new AgentId(nodeId),
            SubscriberNode = new AgentId(nodeId),
            Topic = new TopicName("test.boring"),
            EventId = new EventId(_nextId++),
            TraceId = new TraceId(_nextId++),
            PayloadJson = payload,
        };
    }

    private static EventRecord MakePhaseStarted(
        string sessionId,
        string phaseName,
        string nodeId = "node-alpha",
        DateTimeOffset? at = null)
    {
        var payload = JsonSerializer.Serialize(new { sessionId, phaseName });
        return new EventRecord
        {
            SequenceNumber = _nextId++,
            PublishWallclock = At(at ?? BaseTime),
            ReceiveWallclock = At(at ?? BaseTime),
            PublisherNode = new AgentId(nodeId),
            SubscriberNode = new AgentId(nodeId),
            Topic = new TopicName("scenario.phase_started"),
            EventId = new EventId(_nextId++),
            TraceId = new TraceId(_nextId++),
            PayloadJson = payload,
        };
    }

    private static EventRecord MakePhaseEnded(
        string sessionId,
        string phaseName,
        string nodeId = "node-alpha",
        DateTimeOffset? at = null)
    {
        var payload = JsonSerializer.Serialize(new { sessionId, phaseName });
        return new EventRecord
        {
            SequenceNumber = _nextId++,
            PublishWallclock = At(at ?? BaseTime.AddMinutes(5)),
            ReceiveWallclock = At(at ?? BaseTime.AddMinutes(5)),
            PublisherNode = new AgentId(nodeId),
            SubscriberNode = new AgentId(nodeId),
            Topic = new TopicName("scenario.phase_ended"),
            EventId = new EventId(_nextId++),
            TraceId = new TraceId(_nextId++),
            PayloadJson = payload,
        };
    }

    public ScenarioEndpointTests()
    {
        _fixture = ObserverFixture.CreateAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task GetNotables_ReturnsOnlyNotableEvents()
    {
        var sessionId = "session-notables-1";
        await _fixture.PushAsync(MakeNotable(sessionId, "CriticalHit"));
        await _fixture.PushAsync(MakeNonNotable(sessionId));

        var response = await _fixture.Client.GetAsync(
            $"/api/scenarios/{sessionId}/notables?limit=50");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var results = doc.RootElement.EnumerateArray().ToList();
        results.Should().HaveCount(1);
        results[0].GetProperty("notableLabel").GetString().Should().Be("CriticalHit");
    }

    [Fact]
    public async Task GetNotables_LimitOutOfRange_Returns400()
    {
        var response = await _fixture.Client.GetAsync(
            "/api/scenarios/any-session/notables?limit=1000");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetNotables_PaginationWithBeforeCursor()
    {
        var sessionId = "session-notables-paging";
        for (int i = 1; i <= 5; i++)
        {
            await _fixture.PushAsync(MakeNotable(
                sessionId, $"Hit{i}", at: BaseTime.AddSeconds(i)));
        }

        // Get all notables first
        var allResp = await _fixture.Client.GetAsync(
            $"/api/scenarios/{sessionId}/notables?limit=10");
        allResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var allJson = await allResp.Content.ReadAsStringAsync();
        using var allDoc = JsonDocument.Parse(allJson);
        var allItems = allDoc.RootElement.EnumerateArray().ToList();
        allItems.Should().HaveCountGreaterOrEqualTo(1);

        // Get first event ID and use as before cursor
        var firstEventId = allItems.Last().GetProperty("eventId").GetString()!;
        var pagedResp = await _fixture.Client.GetAsync(
            $"/api/scenarios/{sessionId}/notables?limit=10&before={firstEventId}");
        pagedResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPhases_PairsStartAndEndEvents()
    {
        var sessionId = "session-phases-paired";
        await _fixture.PushAsync(MakePhaseStarted(sessionId, "Phase1", at: BaseTime));
        await _fixture.PushAsync(MakePhaseEnded(sessionId, "Phase1", at: BaseTime.AddMinutes(5)));

        var response = await _fixture.Client.GetAsync(
            $"/api/scenarios/{sessionId}/phases");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var phases = doc.RootElement.EnumerateArray().ToList();
        phases.Should().HaveCount(1);
        var phase = phases[0];
        phase.GetProperty("phaseName").GetString().Should().Be("Phase1");
        phase.GetProperty("status").GetString().Should().Be("Completed");
    }

    [Fact]
    public async Task GetPhases_UnpairedStart_StatusActive()
    {
        var sessionId = "session-phases-active";
        await _fixture.PushAsync(MakePhaseStarted(sessionId, "OngoingPhase", at: BaseTime));

        var response = await _fixture.Client.GetAsync(
            $"/api/scenarios/{sessionId}/phases");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var phases = doc.RootElement.EnumerateArray().ToList();
        phases.Should().HaveCount(1);
        phases[0].GetProperty("status").GetString().Should().Be("Active");
    }

    [Fact]
    public async Task GetState_ReflectsCurrentPhaseAndAggregates()
    {
        var sessionId = "session-state";
        await _fixture.PushAsync(MakePhaseStarted(sessionId, "BattlePhase"));
        await _fixture.PushAsync(MakeNotable(sessionId, "Explosion"));
        await _fixture.PushAsync(MakeNonNotable(sessionId));

        var response = await _fixture.Client.GetAsync(
            $"/api/scenarios/{sessionId}/state");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("totalNotables").GetInt64().Should().BeGreaterOrEqualTo(1);
        doc.RootElement.GetProperty("totalEvents").GetInt64().Should().BeGreaterOrEqualTo(1);
    }

    public async ValueTask DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }
}
