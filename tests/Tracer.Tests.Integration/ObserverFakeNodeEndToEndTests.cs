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

public sealed class ObserverFakeNodeEndToEndTests : IAsyncLifetime
{
    private ObserverFixture _fixture = null!;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2025, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 1000;

    private static EventRecord MakeSessionStart(string sessionId, string nodeId = "node-alpha")
    {
        var payload = JsonSerializer.Serialize(new
        {
            sessionId,
            scenarioId = "TestScenario",
            label = "Test Run",
        });
        return new EventRecord
        {
            SequenceNumber = _nextId++,
            PublishWallclock = At(BaseTime),
            ReceiveWallclock = At(BaseTime),
            PublisherNode = new AgentId(nodeId),
            SubscriberNode = new AgentId(nodeId),
            Topic = new TopicName("system.session_start"),
            EventId = new EventId(_nextId++),
            TraceId = new TraceId(_nextId++),
            PayloadJson = payload,
        };
    }

    private static EventRecord MakeNotable(string sessionId, string label, string nodeId = "node-alpha")
    {
        var payload = JsonSerializer.Serialize(new { sessionId });
        return new EventRecord
        {
            SequenceNumber = _nextId++,
            PublishWallclock = At(BaseTime),
            ReceiveWallclock = At(BaseTime),
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

    private static EventRecord MakePhaseStarted(string sessionId, string phaseName, string nodeId = "node-alpha")
    {
        var payload = JsonSerializer.Serialize(new { sessionId, phaseName });
        return new EventRecord
        {
            SequenceNumber = _nextId++,
            PublishWallclock = At(BaseTime),
            ReceiveWallclock = At(BaseTime),
            PublisherNode = new AgentId(nodeId),
            SubscriberNode = new AgentId(nodeId),
            Topic = new TopicName("scenario.phase_started"),
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
    public async Task GetSessions_ReturnsActiveSession()
    {
        // SC2: Push session_start, verify GET /api/sessions returns it
        var sessionId = $"obs-session-{Guid.NewGuid():N}";
        await _fixture.PushAsync(MakeSessionStart(sessionId));

        var response = await _fixture.Client.GetAsync("/api/sessions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var sessions = doc.RootElement.EnumerateArray().ToList();
        sessions.Should().NotBeEmpty();

        var match = sessions.FirstOrDefault(s =>
            s.GetProperty("sessionId").GetString() == sessionId);
        match.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "the session we pushed must appear in the list");
        match.GetProperty("status").GetString().Should().Be("Active");
    }

    [Fact]
    public async Task GetScenarioNotables_ReturnsNotablesFromScenario()
    {
        // SC3: Push session_start + notable events; verify notables query returns them
        var sessionId = $"obs-notables-{Guid.NewGuid():N}";
        await _fixture.PushAsync(MakeSessionStart(sessionId));
        await _fixture.PushAsync(MakeNotable(sessionId, "Kill"));
        await _fixture.PushAsync(MakeNotable(sessionId, "Assist"));

        var response = await _fixture.Client.GetAsync(
            $"/api/scenario/notables?sessionId={sessionId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var notables = doc.RootElement.EnumerateArray().ToList();
        notables.Should().NotBeEmpty();
        notables.Should().OnlyContain(n =>
            n.GetProperty("notableLabel").GetString() != null);
    }

    [Fact]
    public async Task GetScenarioPhases_ReturnsActivePhaseName()
    {
        // SC4: Push phase_started with phaseName Alpha; verify it appears as Active
        var sessionId = $"obs-phases-{Guid.NewGuid():N}";
        await _fixture.PushAsync(MakeSessionStart(sessionId));
        await _fixture.PushAsync(MakePhaseStarted(sessionId, "Alpha"));

        var response = await _fixture.Client.GetAsync(
            $"/api/scenario/phases?sessionId={sessionId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var phases = doc.RootElement.EnumerateArray().ToList();
        phases.Should().HaveCount(1);
        phases[0].GetProperty("status").GetString().Should().Be("Active");
        phases[0].GetProperty("phaseName").GetString().Should().Be("Alpha");
    }
}

