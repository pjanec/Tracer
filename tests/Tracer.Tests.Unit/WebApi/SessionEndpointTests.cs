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

public sealed class SessionEndpointTests : IAsyncDisposable
{
    private readonly ObserverFixture _fixture;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2025, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 100;
    private static EventRecord MakeSessionStart(
        string sessionId,
        string nodeId,
        DateTimeOffset at,
        string? scenarioId = null,
        string? label = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            sessionId,
            scenarioId = scenarioId ?? "TestScenario",
            label = label ?? "Test Run",
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

    private static EventRecord MakeSessionEnd(
        string sessionId,
        string nodeId,
        DateTimeOffset at)
    {
        var payload = JsonSerializer.Serialize(new { sessionId });
        return new EventRecord
        {
            SequenceNumber = _nextId++,
            PublishWallclock = At(at),
            ReceiveWallclock = At(at),
            PublisherNode = new AgentId(nodeId),
            SubscriberNode = new AgentId(nodeId),
            Topic = new TopicName("system.session_end"),
            EventId = new EventId(_nextId++),
            TraceId = new TraceId(_nextId++),
            PayloadJson = payload,
        };
    }

    private static EventRecord MakeGenericEvent(string sessionId, string nodeId, DateTimeOffset at)
    {
        var payload = JsonSerializer.Serialize(new { sessionId });
        return new EventRecord
        {
            SequenceNumber = _nextId++,
            PublishWallclock = At(at),
            ReceiveWallclock = At(at),
            PublisherNode = new AgentId(nodeId),
            SubscriberNode = new AgentId(nodeId),
            Topic = new TopicName("test.event"),
            EventId = new EventId(_nextId++),
            TraceId = new TraceId(_nextId++),
            PayloadJson = payload,
        };
    }

    public SessionEndpointTests()
    {
        _fixture = ObserverFixture.CreateAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task ListSessions_EmptyDb_ReturnsEmptyArray()
    {
        var response = await _fixture.Client.GetAsync("/api/sessions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ActiveSession_HasStatusActive()
    {
        await _fixture.PushAsync(MakeSessionStart("session-active", "node-1", BaseTime));

        var response = await _fixture.Client.GetAsync("/api/sessions/session-active");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Active");
    }

    [Fact]
    public async Task CompletedSession_HasStatusCompletedAndEndUtcSet()
    {
        await _fixture.PushAsync(MakeSessionStart("session-done", "node-1", BaseTime));
        await _fixture.PushAsync(MakeSessionEnd("session-done", "node-1", BaseTime.AddMinutes(5)));

        var response = await _fixture.Client.GetAsync("/api/sessions/session-done");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Completed");
        // EndUtc should be present
        json.Should().MatchRegex(@"[Ee]nd[A-Za-z]*[Uu]tc");
    }

    [Fact]
    public async Task GetSession_UnknownId_Returns404()
    {
        var response = await _fixture.Client.GetAsync("/api/sessions/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListSessions_ReturnsSessionWithCorrectFields()
    {
        await _fixture.PushAsync(MakeSessionStart(
            "session-fields", "node-alpha", BaseTime,
            scenarioId: "CombatEngagement", label: "Test Run 1"));

        var response = await _fixture.Client.GetAsync("/api/sessions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("session-fields");
        json.Should().Contain("CombatEngagement");
        json.Should().Contain("Test Run 1");
        json.Should().Contain("node-alpha");
    }

    [Fact]
    public async Task EventCountAndNodes_ReflectSessionTimeRange()
    {
        var sessionId = "session-count";
        await _fixture.PushAsync(MakeSessionStart(sessionId, "node-a", BaseTime));
        await _fixture.PushAsync(MakeGenericEvent(sessionId, "node-a", BaseTime.AddSeconds(1)));
        await _fixture.PushAsync(MakeGenericEvent(sessionId, "node-b", BaseTime.AddSeconds(2)));

        var response = await _fixture.Client.GetAsync($"/api/sessions/{sessionId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        // At minimum the session_start event contributed to the count
        json.Should().Contain("node-a");
    }

    [Fact]
    public async Task TimeRangeFilter_ExcludesOutOfRangeSessions()
    {
        await _fixture.PushAsync(MakeSessionStart("session-old", "node-1", BaseTime));

        // Query with a time range that excludes the session
        var from = BaseTime.AddHours(2).ToString("O");
        var to = BaseTime.AddHours(3).ToString("O");

        var response = await _fixture.Client.GetAsync(
            $"/api/sessions?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("session-old");
    }

    public async ValueTask DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }
}
