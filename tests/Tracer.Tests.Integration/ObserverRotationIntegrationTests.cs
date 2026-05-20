using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tracer.Adapters.Mock;
using Tracer.Agent.Storage;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.TestHarness.Observer;
using Xunit;

namespace Tracer.Tests.Integration;

public sealed class ObserverRotationIntegrationTests : IAsyncLifetime
{
    private ObserverFixture _fixture = null!;
    private SimulatedClock _clock = null!;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2025, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 2000;

    private static EventRecord MakeEvent(string nodeId = "node-alpha")
    {
        var payload = "{}";
        return new EventRecord
        {
            SequenceNumber = _nextId++,
            PublishWallclock = At(BaseTime),
            ReceiveWallclock = At(BaseTime),
            PublisherNode = new AgentId(nodeId),
            SubscriberNode = new AgentId(nodeId),
            Topic = new TopicName("test.event"),
            EventId = new EventId(_nextId++),
            TraceId = new TraceId(_nextId++),
            PayloadJson = payload,
        };
    }

    private static IEnumerable<EventRecord> MakeEvents(int count, string nodeId = "node-alpha") =>
        Enumerable.Range(0, count).Select(_ => MakeEvent(nodeId));

    public async Task InitializeAsync()
    {
        _clock = new SimulatedClock(WallclockTime.FromUnixNanoseconds(
            BaseTime.ToUnixTimeMilliseconds() * 1_000_000L));
        _fixture = await ObserverFixture.CreateAsync(
            new ObserverFixtureOptions
            {
                IntervalDuration = TimeSpan.FromMinutes(1),
                Clock = _clock,
            });
    }

    public async Task DisposeAsync() =>
        await _fixture.DisposeAsync();

    [Fact]
    public async Task FirstInterval_FinalizedWithReady_AfterRotation()
    {
        // SC6: push 100 events, rotate, verify manifest has ScheduledRotation and _ready exists
        await _fixture.PushAsync(MakeEvents(100));

        var rotator = _fixture.App.Services.GetRequiredService<Tracer.Agent.Lifecycle.IntervalRotator>();
        var oldDirectory = rotator.CurrentDirectory!;

        await _fixture.ForceRotationAsync();

        // Manifest should exist and have correct reason
        var manifest = await ManifestWriter.ReadAsync(oldDirectory.ManifestPath, CancellationToken.None);
        manifest.Should().NotBeNull();
        manifest!.FinalizationReason.Should().Be(ManifestFinalizationReason.ScheduledRotation);

        // _ready sentinel should exist
        oldDirectory.IsReady.Should().BeTrue();
    }

    [Fact]
    public async Task SecondInterval_QueriesReturnCurrentIntervalEvents()
    {
        // SC7: push 100 events, rotate, then push 100 session_start events in interval 2;
        // /api/sessions must expose the unique sessionId from interval 2
        await _fixture.PushAsync(MakeEvents(100));
        await _fixture.ForceRotationAsync();

        var sessionId = $"session-interval2-{Guid.NewGuid():N}";
        var sessionEvents = Enumerable.Range(0, 100).Select(_ => new EventRecord
        {
            SequenceNumber = _nextId++,
            PublishWallclock = At(BaseTime),
            ReceiveWallclock = At(BaseTime),
            PublisherNode = new AgentId("node-interval2"),
            SubscriberNode = new AgentId("node-interval2"),
            Topic = new TopicName("system.session_start"),
            EventId = new EventId(_nextId++),
            TraceId = new TraceId(_nextId++),
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { sessionId }),
        }).ToList();

        await _fixture.PushAsync(sessionEvents);
        await Task.Delay(100, CancellationToken.None);

        var response = await _fixture.Client.GetAsync("/api/sessions");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var sessions = doc.RootElement.EnumerateArray().ToList();
        sessions.Should().Contain(s => s.GetProperty("sessionId").GetString() == sessionId,
            "a session_start event pushed in interval 2 must appear in /api/sessions");
    }

    [Fact]
    public async Task Queries_DuringRotation_SucceedAfterBriefBlock()
    {
        // SC8: concurrent query + rotation, no 500s
        await _fixture.PushAsync(MakeEvents(100));

        // Run rotation and query concurrently
        var rotateTask = _fixture.ForceRotationAsync();
        var queryTask = _fixture.Client.GetAsync("/api/sessions");

        await Task.WhenAll(rotateTask, queryTask);

        var queryResponse = await queryTask;
        queryResponse.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task MultipleNodes_EventsFromAllNodesIngested()
    {
        // SC9: push 50 events from node-alpha + 50 from node-beta, verify topology shows 2 nodes
        var alphaEvents = MakeEvents(50, "node-alpha").ToList();
        var betaEvents = MakeEvents(50, "node-beta").ToList();

        await _fixture.PushAsync(alphaEvents);
        await _fixture.PushAsync(betaEvents);

        var response = await _fixture.Client.GetAsync("/api/topology");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var nodes = doc.RootElement.GetProperty("nodes").EnumerateArray().ToList();
        nodes.Should().HaveCountGreaterOrEqualTo(2);

        var nodeIds = nodes.Select(n => n.GetProperty("nodeId").GetString()).ToHashSet();
        nodeIds.Should().Contain("node-alpha");
        nodeIds.Should().Contain("node-beta");

        // Verify each node shows the correct event count
        var alpha = nodes.First(n => n.GetProperty("nodeId").GetString() == "node-alpha");
        var beta = nodes.First(n => n.GetProperty("nodeId").GetString() == "node-beta");
        alpha.GetProperty("eventsPublished").GetInt64().Should().Be(50);
        beta.GetProperty("eventsPublished").GetInt64().Should().Be(50);
    }
}

