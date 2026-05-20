using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.TestHarness.Observer;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

/// <summary>Tests for GET /api/live/events SSE endpoint.</summary>
public sealed class LiveEventStreamEndpointsTests : IAsyncDisposable
{
    private readonly WebApiFixture _fixture;

    private static readonly WallclockTime Now =
        WallclockTime.FromUnixNanoseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L);

    private static ulong _nextId = 90000;

    private static EventRecord MakeEvent(string topic = "live.event", string? notableLabel = null)
    {
        var id = _nextId++;
        return new EventRecord
        {
            SequenceNumber = id,
            PublishWallclock = Now,
            ReceiveWallclock = Now,
            PublisherNode = new AgentId("node-live"),
            SubscriberNode = new AgentId("node-live"),
            Topic = new TopicName(topic),
            EventId = new EventId(id),
            TraceId = new TraceId(id),
            PayloadJson = "{}",
            NotableLabel = notableLabel,
        };
    }

    public LiveEventStreamEndpointsTests()
    {
        _fixture = WebApiFixture.CreateAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task GetLiveEvents_ContentTypeIsTextEventStream()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/live/events");
        var response = await _fixture.Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");
    }

    [Fact]
    public async Task LiveEventsEndpoint_AllEventsPassUnfilteredByDefault()
    {
        var ev = MakeEvent("live.all.event");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/live/events");
        var response = await _fixture.Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead);
        var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        await Task.Delay(50);
        _fixture.Broadcaster.Publish(ev);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        string? dataLine = null;
        while (!cts.IsCancellationRequested)
        {
            try
            {
                var line = await reader.ReadLineAsync(cts.Token).AsTask();
                if (line is not null && line.StartsWith("data:"))
                {
                    dataLine = line;
                    break;
                }
            }
            catch (OperationCanceledException) { break; }
        }

        dataLine.Should().NotBeNull("published event should appear on unfiltered /api/live/events");
    }

    [Fact]
    public async Task GetLiveEvents_WithTopicFilter_OnlyMatchingEventsDelivered()
    {
        var matchTopic = $"live.topic.match.{_nextId}";
        var noMatchTopic = $"live.topic.other.{_nextId + 1}";
        var matchEv = MakeEvent(matchTopic);
        var noMatchEv = MakeEvent(noMatchTopic);

        var url = $"/api/live/events?topic={Uri.EscapeDataString(matchTopic)}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await _fixture.Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead);
        var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        await Task.Delay(50);
        _fixture.Broadcaster.Publish(noMatchEv);   // should not appear
        _fixture.Broadcaster.Publish(matchEv);     // should appear

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        string? dataLine = null;
        while (!cts.IsCancellationRequested)
        {
            try
            {
                var line = await reader.ReadLineAsync(cts.Token).AsTask();
                if (line is not null && line.StartsWith("data:"))
                {
                    dataLine = line;
                    break;
                }
            }
            catch (OperationCanceledException) { break; }
        }

        dataLine.Should().NotBeNull("matching event should appear");
        dataLine!.Should().Contain(matchTopic);
        dataLine.Should().NotContain(noMatchTopic);
    }

    [Fact]
    public async Task GetLiveEvents_XAccelBufferingNoCache_HeadersPresent()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/live/events");
        var response = await _fixture.Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        response.Headers.TryGetValues("X-Accel-Buffering", out var xAccel);
        xAccel.Should().ContainSingle(v => v == "no",
            "X-Accel-Buffering: no is required for SSE through reverse proxies");

        response.Headers.TryGetValues("Cache-Control", out var cacheControl);
        cacheControl.Should().ContainSingle(v => v.Contains("no-cache"),
            "Cache-Control: no-cache is required for SSE");
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();
}
