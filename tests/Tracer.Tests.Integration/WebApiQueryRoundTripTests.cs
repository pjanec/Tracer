using System.Net;
using System.Text.Json;
using FluentAssertions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.TestHarness.Observer;
using Xunit;

namespace Tracer.Tests.Integration;

public sealed class WebApiQueryRoundTripTests : IAsyncLifetime
{
    private ObserverFixture _fixture = null!;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2025, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 3000;

    private static EventRecord MakeSessionStart(string sessionId, string nodeId, DateTimeOffset at)
    {
        var payload = JsonSerializer.Serialize(new
        {
            sessionId,
            scenarioId = "RoundTripScenario",
            label = "Round Trip Test",
        });
        return new EventRecord
        {
            SequenceNumber = _nextId++,
            PublishWallclock = At(at),
            ReceiveWallclock = At(at),
            PublisherNode = new AgentId(nodeId),
            SubscriberNode = new AgentId(nodeId),
            Topic = new TopicName("system.session_start"),
            EventId = new EventId(_nextId++),
            TraceId = new TraceId(_nextId++),
            PayloadJson = payload,
        };
    }

    private static EventRecord MakeNotable(string sessionId, string label, string nodeId = "node-alpha", DateTimeOffset? at = null)
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
            NotableLabel = label,
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
            Topic = new TopicName("debug.tick"),
            EventId = new EventId(_nextId++),
            TraceId = new TraceId(_nextId++),
            PayloadJson = payload,
        };
    }

    private static EventRecord MakePhaseStarted(string sessionId, string phaseName, DateTimeOffset? at = null)
    {
        var payload = JsonSerializer.Serialize(new { sessionId, phaseName });
        return new EventRecord
        {
            SequenceNumber = _nextId++,
            PublishWallclock = At(at ?? BaseTime),
            ReceiveWallclock = At(at ?? BaseTime),
            PublisherNode = new AgentId("node-alpha"),
            SubscriberNode = new AgentId("node-alpha"),
            Topic = new TopicName("scenario.phase_started"),
            EventId = new EventId(_nextId++),
            TraceId = new TraceId(_nextId++),
            PayloadJson = payload,
        };
    }

    private static EventRecord MakePhaseEnded(string sessionId, string phaseName, DateTimeOffset? at = null)
    {
        var payload = JsonSerializer.Serialize(new { sessionId, phaseName });
        return new EventRecord
        {
            SequenceNumber = _nextId++,
            PublishWallclock = At(at ?? BaseTime),
            ReceiveWallclock = At(at ?? BaseTime),
            PublisherNode = new AgentId("node-alpha"),
            SubscriberNode = new AgentId("node-alpha"),
            Topic = new TopicName("scenario.phase_ended"),
            EventId = new EventId(_nextId++),
            TraceId = new TraceId(_nextId++),
            PayloadJson = payload,
        };
    }

    public async Task InitializeAsync() =>
        _fixture = await ObserverFixture.CreateAsync();

    public async Task DisposeAsync() =>
        await _fixture.DisposeAsync();

    [Fact]
    public async Task GetSessions_AfterIngestion_ReturnsCorrectSessions()
    {
        // SC2: two session_start events; result must be ordered descending by startUtc
        var sessionIdA = $"rt-session-a-{Guid.NewGuid():N}";
        var sessionIdB = $"rt-session-b-{Guid.NewGuid():N}";
        var earlier = BaseTime.AddHours(-2);
        var later = BaseTime;

        await _fixture.PushAsync(MakeSessionStart(sessionIdA, "node-alpha", earlier));
        await _fixture.PushAsync(MakeSessionStart(sessionIdB, "node-alpha", later));

        var response = await _fixture.Client.GetAsync("/api/sessions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var sessions = doc.RootElement.EnumerateArray().ToList();
        sessions.Should().HaveCountGreaterOrEqualTo(2);

        // Find our two sessions in the list
        var sA = sessions.First(s => s.GetProperty("sessionId").GetString() == sessionIdA);
        var sB = sessions.First(s => s.GetProperty("sessionId").GetString() == sessionIdB);

        var startA = DateTimeOffset.Parse(sA.GetProperty("startUtc").GetString()!);
        var startB = DateTimeOffset.Parse(sB.GetProperty("startUtc").GetString()!);

        // B (later) must appear before A (earlier) in the results list
        sessions.IndexOf(sB).Should().BeLessThan(sessions.IndexOf(sA));
        startB.Should().BeAfter(startA);
    }

    [Fact]
    public async Task GetSession_ById_ReturnsMatchingDto()
    {
        // SC3: GET /api/sessions/{id} returns exact dto fields
        var sessionId = $"rt-byid-{Guid.NewGuid():N}";
        await _fixture.PushAsync(MakeSessionStart(sessionId, "node-alpha", BaseTime));

        var response = await _fixture.Client.GetAsync($"/api/sessions/{sessionId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("sessionId").GetString().Should().Be(sessionId);
        doc.RootElement.GetProperty("scenarioId").GetString().Should().Be("RoundTripScenario");
        doc.RootElement.GetProperty("label").GetString().Should().Be("Round Trip Test");
        doc.RootElement.GetProperty("status").GetString().Should().Be("Active");
    }

    [Fact]
    public async Task GetScenarioNotables_ReturnsOnlyNotableEvents_WithCorrectFields()
    {
        // SC4: push labeled + unlabeled; only labeled should return
        var sessionId = $"rt-notables-{Guid.NewGuid():N}";
        await _fixture.PushAsync(MakeNotable(sessionId, "Kill"));
        await _fixture.PushAsync(MakeNotable(sessionId, "Assist"));
        await _fixture.PushAsync(MakeNonNotable(sessionId));

        var response = await _fixture.Client.GetAsync(
            $"/api/scenario/notables?sessionId={sessionId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var notables = doc.RootElement.EnumerateArray().ToList();
        notables.Should().HaveCount(2);
        notables.Should().OnlyContain(n =>
            n.GetProperty("notableLabel").GetString() != null);
    }

    [Fact]
    public async Task GetScenarioNotables_BeforeCursor_ReturnsSubset()
    {
        // SC5: push 10 notables with ascending timestamps; with limit=3 + before=midpoint, return exactly 3
        var sessionId = $"rt-cursor-{Guid.NewGuid():N}";
        var events = new List<EventRecord>();
        for (int i = 0; i < 10; i++)
            events.Add(MakeNotable(sessionId, $"Event{i}", at: BaseTime.AddSeconds(i)));
        await _fixture.PushAsync(events);

        // Get all
        var allResp = await _fixture.Client.GetAsync(
            $"/api/scenario/notables?sessionId={sessionId}&limit=100");
        allResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var allJson = await allResp.Content.ReadAsStringAsync();
        using var allDoc = JsonDocument.Parse(allJson);
        var allItems = allDoc.RootElement.EnumerateArray().ToList();
        allItems.Should().HaveCount(10);

        // Service returns descending — index 4 is the midpoint
        var midEventId = allItems[4].GetProperty("eventId").GetString()!;
        var midTime = DateTimeOffset.Parse(allItems[4].GetProperty("occurredAtUtc").GetString()!);

        var pagedResp = await _fixture.Client.GetAsync(
            $"/api/scenario/notables?sessionId={sessionId}&limit=3&before={midEventId}");
        pagedResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var pagedJson = await pagedResp.Content.ReadAsStringAsync();
        using var pagedDoc = JsonDocument.Parse(pagedJson);
        var paged = pagedDoc.RootElement.EnumerateArray().ToList();
        paged.Should().HaveCount(3);
        paged.Should().OnlyContain(n =>
            DateTimeOffset.Parse(n.GetProperty("occurredAtUtc").GetString()!) < midTime);
    }

    [Fact]
    public async Task GetScenarioPhases_PairsStartAndEnd()
    {
        // SC6: paired phase → Completed; unpaired phase → Active
        var sessionId = $"rt-phases-{Guid.NewGuid():N}";
        await _fixture.PushAsync(MakePhaseStarted(sessionId, "PhaseA", BaseTime));
        await _fixture.PushAsync(MakePhaseEnded(sessionId, "PhaseA", BaseTime.AddMinutes(5)));
        await _fixture.PushAsync(MakePhaseStarted(sessionId, "PhaseB", BaseTime.AddMinutes(6)));

        var response = await _fixture.Client.GetAsync(
            $"/api/scenario/phases?sessionId={sessionId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var phases = doc.RootElement.EnumerateArray().ToList();
        phases.Should().HaveCount(2);

        var phaseA = phases.First(p => p.GetProperty("phaseName").GetString() == "PhaseA");
        phaseA.GetProperty("status").GetString().Should().Be("Completed");
        phaseA.TryGetProperty("endedAtUtc", out var endProp).Should().BeTrue();
        endProp.ValueKind.Should().NotBe(JsonValueKind.Null);

        var phaseB = phases.First(p => p.GetProperty("phaseName").GetString() == "PhaseB");
        phaseB.GetProperty("status").GetString().Should().Be("Active");
    }

    [Fact]
    public async Task GetEvent_ById_ReturnsCorrectEventDto()
    {
        // SC7: push event with known fields; GET /api/events/{eventId} returns correct traceId, severity, occurredAtUtc
        var ev = new EventRecord
        {
            SequenceNumber = _nextId++,
            PublishWallclock = At(BaseTime),
            ReceiveWallclock = At(BaseTime),
            PublisherNode = new AgentId("node-alpha"),
            SubscriberNode = new AgentId("node-alpha"),
            Topic = new TopicName("combat.hit"),
            EventId = new EventId(_nextId++),
            TraceId = new TraceId(42),
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { sessionId = $"rt-event-{Guid.NewGuid():N}" }),
            NotableLabel = "TestLabel",
            Severity = Severity.Warning,
        };
        await _fixture.PushAsync(ev);

        var eventIdHex = ev.EventId.Value.ToString("X16");
        var response = await _fixture.Client.GetAsync($"/api/events/{eventIdHex}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("eventId").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("topic").GetString().Should().Be("combat.hit");
        doc.RootElement.GetProperty("traceId").GetString().Should().Be("000000000000002A",
            "TraceId(42) must serialise as 16-char uppercase hex");
        doc.RootElement.GetProperty("severity").GetString().Should().Be("Warning");

        var occurredAt = DateTimeOffset.Parse(
            doc.RootElement.GetProperty("occurredAtUtc").GetString()!);
        Math.Abs((occurredAt - BaseTime).TotalMilliseconds).Should().BeLessThan(1,
            "occurredAtUtc should round-trip through WallclockTime within 1ms");
    }

    [Fact]
    public async Task GetEvent_UnknownId_Returns404()
    {
        // SC8: valid 16-char hex with no matching row → 404
        var unknownHex = "DEADBEEF01020304";
        var response = await _fixture.Client.GetAsync($"/api/events/{unknownHex}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetTopology_AfterIngestion_ReturnsNodeInfo()
    {
        // SC9: push exactly 3 events from alpha and 5 from beta; verify per-node eventsPublished and firstSeenUtc
        var session = $"rt-topo-{Guid.NewGuid():N}";

        for (int i = 0; i < 3; i++)
            await _fixture.PushAsync(MakeNotable(session, "Evt", "rt-topo-alpha"));
        for (int i = 0; i < 5; i++)
            await _fixture.PushAsync(MakeNotable(session, "Evt", "rt-topo-beta"));

        var response = await _fixture.Client.GetAsync("/api/topology");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var nodes = doc.RootElement.GetProperty("nodes").EnumerateArray().ToList();

        var nodeIds = nodes.Select(n => n.GetProperty("nodeId").GetString()).ToHashSet();
        nodeIds.Should().Contain("rt-topo-alpha");
        nodeIds.Should().Contain("rt-topo-beta");

        var alpha = nodes.First(n => n.GetProperty("nodeId").GetString() == "rt-topo-alpha");
        var beta = nodes.First(n => n.GetProperty("nodeId").GetString() == "rt-topo-beta");

        alpha.GetProperty("eventsPublished").GetInt64().Should().Be(3,
            "exactly 3 events were pushed from rt-topo-alpha");
        beta.GetProperty("eventsPublished").GetInt64().Should().Be(5,
            "exactly 5 events were pushed from rt-topo-beta");

        alpha.GetProperty("firstSeenUtc").GetDateTimeOffset().Should().NotBe(DateTimeOffset.MinValue,
            "firstSeenUtc must be a real timestamp for rt-topo-alpha");
        beta.GetProperty("firstSeenUtc").GetDateTimeOffset().Should().NotBe(DateTimeOffset.MinValue,
            "firstSeenUtc must be a real timestamp for rt-topo-beta");
    }
}

