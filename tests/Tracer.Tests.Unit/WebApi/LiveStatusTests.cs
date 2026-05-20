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

public sealed class LiveStatusTests : IAsyncDisposable
{
    private static readonly WallclockTime Now =
        WallclockTime.FromUnixNanoseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L);

    private static ulong _nextId = 5000;

    private static EventRecord MakeEvent() => new EventRecord
    {
        SequenceNumber = _nextId++,
        PublishWallclock = Now,
        ReceiveWallclock = Now,
        PublisherNode = new AgentId("node-status"),
        SubscriberNode = new AgentId("node-status"),
        Topic = new TopicName("test.status"),
        EventId = new EventId(_nextId++),
        TraceId = new TraceId(_nextId++),
        PayloadJson = "{}",
        NotableLabel = "StatusEvent",
        Severity = Severity.Info,
    };

    private readonly ObserverFixture _fixture;

    public LiveStatusTests()
    {
        _fixture = ObserverFixture.CreateAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task LiveStatus_ReflectsStateReporterCounters()
    {
        // Push events to increment counters
        await _fixture.PushAsync(MakeEvent());
        await _fixture.PushAsync(MakeEvent());

        var response = await _fixture.Client.GetAsync("/api/live/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var ingested = doc.RootElement.GetProperty("ingestedTotal").GetInt64();
        ingested.Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task IngestionHealthy_TrueWhenLastEventWithin60s()
    {
        // Push an event so LastEventUtc is recent
        await _fixture.PushAsync(MakeEvent());

        var response = await _fixture.Client.GetAsync("/api/live/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("ingestionHealthy").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task IngestionHealthy_FalseWhenNoEvents()
    {
        // Fresh fixture with no events pushed
        await using var freshFixture = await ObserverFixture.CreateAsync();

        var response = await freshFixture.Client.GetAsync("/api/live/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("ingestionHealthy").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ActiveSseClients_MatchesConnectionManagerCount()
    {
        // Initially 0
        var response1 = await _fixture.Client.GetAsync("/api/live/status");
        var json1 = await response1.Content.ReadAsStringAsync();
        using var doc1 = JsonDocument.Parse(json1);
        doc1.RootElement.GetProperty("activeSseClients").GetInt32().Should().Be(0);

        // Connect one SSE client
        var sseRequest = new HttpRequestMessage(HttpMethod.Get, "/api/live/notables");
        _ = _fixture.Client.SendAsync(sseRequest, HttpCompletionOption.ResponseHeadersRead);
        await Task.Delay(150); // let it register

        var response2 = await _fixture.Client.GetAsync("/api/live/status");
        var json2 = await response2.Content.ReadAsStringAsync();
        using var doc2 = JsonDocument.Parse(json2);
        doc2.RootElement.GetProperty("activeSseClients").GetInt32().Should().Be(1);
    }

    public async ValueTask DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }
}
