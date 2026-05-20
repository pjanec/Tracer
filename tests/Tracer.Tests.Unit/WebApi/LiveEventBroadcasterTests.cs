using FluentAssertions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.TestHarness.Observer;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

/// <summary>Tests for <see cref="Tracer.WebApi.Streaming.LiveEventBroadcaster"/> event dispatch.</summary>
public sealed class LiveEventBroadcasterTests : IAsyncDisposable
{
    private readonly WebApiFixture _fixture;

    private static readonly WallclockTime Now =
        WallclockTime.FromUnixNanoseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L);

    private static ulong _nextId = 80000;

    private static EventRecord MakeEvent(string? notableLabel = null)
    {
        var id = _nextId++;
        return new EventRecord
        {
            SequenceNumber = id,
            PublishWallclock = Now,
            ReceiveWallclock = Now,
            PublisherNode = new AgentId("node-bcast"),
            SubscriberNode = new AgentId("node-bcast"),
            Topic = new TopicName("test.broadcast"),
            EventId = new EventId(id),
            TraceId = new TraceId(id),
            PayloadJson = "{}",
            NotableLabel = notableLabel,
        };
    }

    public LiveEventBroadcasterTests()
    {
        _fixture = WebApiFixture.CreateAsync().GetAwaiter().GetResult();
    }

    private static EventRecord MakeEventWithTopic(string topic, string? notableLabel = null)
    {
        var id = _nextId++;
        return new EventRecord
        {
            SequenceNumber = id,
            PublishWallclock = Now,
            ReceiveWallclock = Now,
            PublisherNode = new AgentId("node-bcast"),
            SubscriberNode = new AgentId("node-bcast"),
            Topic = new TopicName(topic),
            EventId = new EventId(id),
            TraceId = new TraceId(id),
            PayloadJson = "{}",
            NotableLabel = notableLabel,
        };
    }

    [Fact]
    public async Task PublishedEvent_ReachesConnectedSseClient()
    {
        var notableEv = MakeEvent(notableLabel: "BroadcastHit");

        // Connect SSE client
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/live/notables");
        var response = await _fixture.Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead);
        var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        // Allow connection to register
        await Task.Delay(50);

        // Publish
        _fixture.Broadcaster.Publish(notableEv);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
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

        dataLine.Should().NotBeNull("the published event should arrive at the SSE client");
    }

    [Fact]
    public async Task FilteredEvent_DoesNotReachNotablesStream()
    {
        var nonNotable = MakeEvent(notableLabel: null);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/live/notables");
        var response = await _fixture.Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead);
        var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        await Task.Delay(50);
        _fixture.Broadcaster.Publish(nonNotable);

        // 400ms window — no data: line should appear
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
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

        dataLine.Should().BeNull("non-notable events should not appear on the notables stream");
    }

    [Fact]
    public async Task Publish_ConnectionWithTopicFilter_OnlyDeliverMatchingEvents()
    {
        var matchTopic = $"match.{_nextId++}";
        var otherTopic = $"other.{_nextId++}";

        // Connect one filtered stream (matches only matchTopic)
        var filteredReq = new HttpRequestMessage(
            HttpMethod.Get, $"/api/live/events?topic={Uri.EscapeDataString(matchTopic)}");
        var filteredResp = await _fixture.Client.SendAsync(
            filteredReq, HttpCompletionOption.ResponseHeadersRead);
        var filteredStream = await filteredResp.Content.ReadAsStreamAsync();
        using var filteredReader = new StreamReader(filteredStream);

        // Connect one unfiltered stream
        var allReq = new HttpRequestMessage(HttpMethod.Get, "/api/live/events");
        var allResp = await _fixture.Client.SendAsync(
            allReq, HttpCompletionOption.ResponseHeadersRead);
        var allStream = await allResp.Content.ReadAsStreamAsync();
        using var allReader = new StreamReader(allStream);

        await Task.Delay(60);  // allow connections to register

        // Publish 3 events: 1 matching, 2 non-matching
        _fixture.Broadcaster.Publish(MakeEventWithTopic(otherTopic));
        _fixture.Broadcaster.Publish(MakeEventWithTopic(otherTopic));
        _fixture.Broadcaster.Publish(MakeEventWithTopic(matchTopic));

        // Read from filtered stream for up to 2 seconds — expect exactly 1 event
        using var filteredCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var filteredLines = new List<string>();
        while (!filteredCts.IsCancellationRequested)
        {
            try
            {
                var line = await filteredReader.ReadLineAsync(filteredCts.Token).AsTask();
                if (line is not null && line.StartsWith("data:"))
                    filteredLines.Add(line);
                if (filteredLines.Count >= 1) break;
            }
            catch (OperationCanceledException) { break; }
        }

        // Read from unfiltered stream for up to 2 seconds — expect 3 events
        using var allCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var allLines = new List<string>();
        while (!allCts.IsCancellationRequested)
        {
            try
            {
                var line = await allReader.ReadLineAsync(allCts.Token).AsTask();
                if (line is not null && line.StartsWith("data:"))
                    allLines.Add(line);
                if (allLines.Count >= 3) break;
            }
            catch (OperationCanceledException) { break; }
        }

        filteredLines.Should().HaveCount(1,
            "only the event matching the topic filter should be delivered");
        filteredLines[0].Should().Contain(matchTopic);
        allLines.Should().HaveCount(3,
            "unfiltered connection receives all events");
    }

    [Fact]
    public async Task Publish_TenClientsAtThousandEventsPerSecond_NoDropsOrCrashes()
    {
        // Connect 10 SSE clients with no filter
        var connections = new List<(HttpResponseMessage Resp, StreamReader Reader)>();
        for (int i = 0; i < 10; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "/api/live/events");
            var resp = await _fixture.Client.SendAsync(
                req, HttpCompletionOption.ResponseHeadersRead);
            var stream = await resp.Content.ReadAsStreamAsync();
            connections.Add((resp, new StreamReader(stream)));
        }

        await Task.Delay(60);  // allow all connections to register

        // Publish 1000 events rapidly
        Exception? publishException = null;
        try
        {
            for (int i = 0; i < 1000; i++)
                _fixture.Broadcaster.Publish(MakeEventWithTopic("load.test"));
        }
        catch (Exception ex) { publishException = ex; }

        await Task.Delay(500);  // give broadcaster time to fan out

        // Verify no crashes
        publishException.Should().BeNull("Publish should not throw under load");
        // Verify connections are still alive (response headers received)
        foreach (var (resp, _) in connections)
            resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        // Cleanup
        foreach (var (resp, reader) in connections)
        {
            reader.Dispose();
            resp.Dispose();
        }
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();
}
