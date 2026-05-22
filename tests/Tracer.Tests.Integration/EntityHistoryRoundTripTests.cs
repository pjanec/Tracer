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

public sealed class EntityHistoryRoundTripTests : IAsyncLifetime
{
    private ObserverFixture _fixture = null!;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 5000;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private static EventRecord MakeSessionStart(string sessionId, DateTimeOffset at)
    {
        var payload = JsonSerializer.Serialize(new { sessionId, scenarioId = "EntityRoundTrip" });
        return new EventRecord
        {
            SequenceNumber = _nextId++,
            PublishWallclock = At(at),
            ReceiveWallclock = At(at),
            PublisherNode = new AgentId("node-obs"),
            SubscriberNode = new AgentId("node-obs"),
            Topic = new TopicName("system.session_start"),
            EventId = new EventId(_nextId++),
            TraceId = new TraceId(_nextId++),
            PayloadJson = payload,
        };
    }

    private static EventRecord MakeEntityEvent(string sessionId, string entityId, string topic, DateTimeOffset at)
    {
        var payload = JsonSerializer.Serialize(new { sessionId });
        return new EventRecord
        {
            SequenceNumber = _nextId++,
            PublishWallclock = At(at),
            ReceiveWallclock = At(at),
            PublisherNode = new AgentId("node-obs"),
            SubscriberNode = new AgentId("node-obs"),
            Topic = new TopicName(topic),
            EventId = new EventId(_nextId++),
            TraceId = new TraceId(_nextId++),
            PayloadJson = payload,
            EntityId = new EntityId(entityId),
        };
    }

    private static StateSampleRecord MakeSlowState(string entityId, string topic, DateTimeOffset at)
    {
        return new StateSampleRecord
        {
            SequenceNumber = _nextId++,
            PublishWallclock = At(at),
            ReceiveWallclock = At(at),
            PublisherNode = new AgentId("node-obs"),
            SubscriberNode = new AgentId("node-obs"),
            Topic = new TopicName(topic),
            InstanceKey = entityId,
            PayloadJson = "{}",
            Rate = StateSampleRate.Slow,
        };
    }

    public async Task InitializeAsync()
        => _fixture = await ObserverFixture.CreateAsync();

    public async Task DisposeAsync()
        => await _fixture.DisposeAsync();

    [Fact]
    public async Task EntityHistory_RoundTrip_EventsAndSlowState()
    {
        var sessionId = $"ert-session-{Guid.NewGuid():N}";
        const string entityId = "ent-X-rtt";
        const string eventTopic = "combat.hit";
        const string stateTopic = "pose";

        // Push session start event
        await _fixture.PushAsync(MakeSessionStart(sessionId, BaseTime));

        // Push 20 events for ent-X
        var entityEvents = new List<EventRecord>();
        for (int i = 0; i < 20; i++)
            entityEvents.Add(MakeEntityEvent(sessionId, entityId, eventTopic, BaseTime.AddSeconds(i)));
        await _fixture.PushAsync(entityEvents);

        // Push 5 slow-state rows for ent-X
        for (int i = 0; i < 5; i++)
            await _fixture.PushStateAsync(MakeSlowState(entityId, stateTopic, BaseTime.AddSeconds(i)));

        // SC11: GET /api/entities — entity appears, eventCount >= 20
        var listResp = await _fixture.Client.GetAsync(
            $"/api/entities?sessionId={sessionId}");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var listJson = await listResp.Content.ReadAsStringAsync();
        using var listDoc = JsonDocument.Parse(listJson);
        var entities = listDoc.RootElement.GetProperty("entities").EnumerateArray().ToList();
        entities.Should().NotBeEmpty();
        var entityEntry = entities.FirstOrDefault(e =>
            e.GetProperty("entityId").GetString() == entityId);
        entityEntry.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "entity ent-X-rtt should appear in the list");
        entityEntry.GetProperty("eventCount").GetInt64().Should().BeGreaterThanOrEqualTo(20);

        // SC11: GET /api/entities/{entityId}/events — 20 events returned
        var from = BaseTime.AddSeconds(-1).ToString("O");
        var to = BaseTime.AddSeconds(30).ToString("O");

        var eventsResp = await _fixture.Client.GetAsync(
            $"/api/entities/{entityId}/events?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}&limit=100");
        eventsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var eventsJson = await eventsResp.Content.ReadAsStringAsync();
        using var eventsDoc = JsonDocument.Parse(eventsJson);
        var eventsArray = eventsDoc.RootElement.GetProperty("events").EnumerateArray().ToList();
        eventsArray.Should().HaveCountGreaterThanOrEqualTo(20);

        // SC11: GET /api/entities/{entityId}/slow-state — byTopic["pose"] has 5 entries
        var slowResp = await _fixture.Client.GetAsync(
            $"/api/entities/{entityId}/slow-state?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");
        slowResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var slowJson = await slowResp.Content.ReadAsStringAsync();
        using var slowDoc = JsonDocument.Parse(slowJson);
        var byTopic = slowDoc.RootElement.GetProperty("byTopic");
        byTopic.TryGetProperty(stateTopic, out var poseArray).Should().BeTrue(
            $"byTopic should contain '{stateTopic}'");
        poseArray.EnumerateArray().Should().HaveCount(5);

        // Truncation test: limit=5 should return 5 events with truncated=true
        var truncResp = await _fixture.Client.GetAsync(
            $"/api/entities/{entityId}/events?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}&limit=5");
        truncResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var truncJson = await truncResp.Content.ReadAsStringAsync();
        using var truncDoc = JsonDocument.Parse(truncJson);
        truncDoc.RootElement.GetProperty("truncated").GetBoolean().Should().BeTrue();
        truncDoc.RootElement.GetProperty("events").EnumerateArray().Should().HaveCount(5);
    }
}
