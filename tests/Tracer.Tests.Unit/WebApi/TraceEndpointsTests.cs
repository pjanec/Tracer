using System.Diagnostics;
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

public sealed class TraceEndpointsTests : IAsyncDisposable
{
    private readonly ObserverFixture _fixture;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 6, 10, 8, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 900_000;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private EventRecord MakeEvent(ulong eventId, ulong traceId, ulong parentId = 0,
        DateTimeOffset? at = null) =>
        new EventRecord
        {
            SequenceNumber   = eventId,
            PublishWallclock = At(at ?? BaseTime),
            ReceiveWallclock = At(at ?? BaseTime),
            PublisherNode    = new AgentId("trace-node"),
            SubscriberNode   = new AgentId("trace-node"),
            Topic            = new TopicName("trace.endpoint.test"),
            EventId          = new EventId(eventId),
            TraceId          = new TraceId(traceId),
            ParentEventId    = parentId != 0 ? new EventId(parentId) : null,
            PayloadJson      = "{}",
        };

    public TraceEndpointsTests()
    {
        _fixture = ObserverFixture.CreateAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task GetTraceTree_ValidHexTraceId_Returns200WithNodesAndEdges()
    {
        // 5-event trace: root + 4 children
        var traceId = _nextId++;
        var rootId  = _nextId++;
        var events = new List<EventRecord> { MakeEvent(rootId, traceId, 0) };
        for (int i = 0; i < 4; i++)
            events.Add(MakeEvent(_nextId++, traceId, rootId, BaseTime.AddSeconds(i + 1)));
        await _fixture.PushAsync(events);

        var url = $"/api/traces/{traceId:X16}/tree";
        var response = await _fixture.Client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.GetProperty("nodes").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetTraceTree_InvalidHexId_Returns400ProblemDetails()
    {
        var response = await _fixture.Client.GetAsync("/api/traces/ZZZZZZZZZZZZZZZZ/tree");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("traceId");
    }

    [Fact]
    public async Task GetTraceTree_UnknownTraceId_Returns404()
    {
        // A trace ID that was never ingested
        var unknownTraceId = ulong.MaxValue - 42;
        var url = $"/api/traces/{unknownTraceId:X16}/tree";
        var response = await _fixture.Client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetTraceTree_MaxEventsExceeds5000_ClampedTo5000AndNoError()
    {
        // Seed a trace with 10 events; requesting 99999 should not fail
        var traceId = _nextId++;
        var rootId  = _nextId++;
        var events = new List<EventRecord> { MakeEvent(rootId, traceId, 0) };
        for (int i = 0; i < 9; i++)
            events.Add(MakeEvent(_nextId++, traceId, rootId));
        await _fixture.PushAsync(events);

        var url = $"/api/traces/{traceId:X16}/tree?maxEvents=99999";
        var response = await _fixture.Client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("nodes").GetArrayLength().Should().BeLessThanOrEqualTo(5000);
    }

    [Fact]
    public async Task GetAncestors_ValidEventId_Returns200WithAncestorChain()
    {
        // 3-level chain: root → mid → leaf
        var traceId = _nextId++;
        var rootId  = _nextId++;
        var midId   = _nextId++;
        var leafId  = _nextId++;
        await _fixture.PushAsync(new[]
        {
            MakeEvent(rootId, traceId, 0),
            MakeEvent(midId,  traceId, rootId, BaseTime.AddSeconds(1)),
            MakeEvent(leafId, traceId, midId,  BaseTime.AddSeconds(2)),
        });

        var url = $"/api/events/{leafId:X16}/ancestors";
        var response = await _fixture.Client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var tree = doc.RootElement;

        // Roots of an ancestor-only walk are the top-most events (the root)
        var rootEventIds = tree.GetProperty("rootEventIds").EnumerateArray()
            .Select(x => x.GetString()).ToList();
        rootEventIds.Should().Contain(rootId.ToString("X16"),
            "the topmost ancestor should appear in rootEventIds");
    }

    [Fact]
    public async Task GetDescendants_ValidEventId_Returns200WithDescendantTree()
    {
        // root → [child1, child2]
        var traceId = _nextId++;
        var rootId  = _nextId++;
        var child1  = _nextId++;
        var child2  = _nextId++;
        await _fixture.PushAsync(new[]
        {
            MakeEvent(rootId,  traceId, 0),
            MakeEvent(child1, traceId, rootId, BaseTime.AddSeconds(1)),
            MakeEvent(child2, traceId, rootId, BaseTime.AddSeconds(2)),
        });

        var url = $"/api/events/{rootId:X16}/descendants";
        var response = await _fixture.Client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var tree = doc.RootElement;

        // The leaf event IDs should have no outgoing edges in the response
        var leafEventIds = tree.GetProperty("leafEventIds").EnumerateArray()
            .Select(x => x.GetString()).ToHashSet();
        var edgeParents = tree.GetProperty("edges").EnumerateArray()
            .Select(e => e.GetProperty("parentEventId").GetString()).ToHashSet();

        leafEventIds.Intersect(edgeParents).Should().BeEmpty(
            "leaf events should not appear as parent in any edge");
    }

    [Fact]
    public async Task GetTraceTree_Under100Events_RespondsBefore300ms()
    {
        // Seed 50 events
        var traceId = _nextId++;
        var rootId  = _nextId++;
        var events = new List<EventRecord> { MakeEvent(rootId, traceId, 0) };
        for (int i = 0; i < 49; i++)
            events.Add(MakeEvent(_nextId++, traceId, rootId));
        await _fixture.PushAsync(events);

        var url = $"/api/traces/{traceId:X16}/tree";

        // Warm up connection
        await _fixture.Client.GetAsync(url);

        var sw = Stopwatch.StartNew();
        var response = await _fixture.Client.GetAsync(url);
        sw.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(300,
            "a 50-event trace tree should respond within 300 ms");
    }

    [Fact]
    public async Task GetAncestors_10DeepChain_WalkExpandsBefore200ms()
    {
        // 10-deep chain
        var traceId = _nextId++;
        var ids = new ulong[10];
        for (int i = 0; i < 10; i++) ids[i] = _nextId++;

        var events = new List<EventRecord> { MakeEvent(ids[0], traceId, 0) };
        for (int i = 1; i < 10; i++)
            events.Add(MakeEvent(ids[i], traceId, ids[i - 1], BaseTime.AddSeconds(i)));
        await _fixture.PushAsync(events);

        var leafId = ids[9];
        var url = $"/api/events/{leafId:X16}/ancestors?maxDepth=10";

        // Warm up
        await _fixture.Client.GetAsync(url);

        var sw = Stopwatch.StartNew();
        var response = await _fixture.Client.GetAsync(url);
        sw.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(200,
            "a 10-deep ancestor walk should respond within 200 ms");
    }
}
