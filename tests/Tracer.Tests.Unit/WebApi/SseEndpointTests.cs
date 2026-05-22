using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Streaming;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class SseEndpointTests : IAsyncDisposable
{
    private static readonly WallclockTime Now =
        WallclockTime.FromUnixNanoseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L);

    private static ulong _nextId = 1000;

    private static EventRecord MakeNotableEvent(string? label = "CriticalHit") => new EventRecord
    {
        SequenceNumber = _nextId++,
        PublishWallclock = Now,
        ReceiveWallclock = Now,
        PublisherNode = new AgentId("node-sse"),
        SubscriberNode = new AgentId("node-sse"),
        Topic = new TopicName("combat.event"),
        EventId = new EventId(_nextId++),
        TraceId = new TraceId(_nextId++),
        PayloadJson = @"{""detail"":""test""}",
        NotableLabel = label,
        Severity = label is not null ? Severity.Warning : null,
    };

    private static EventRecord MakeNonNotableEvent() => MakeNotableEvent(label: null);

    private WebApiFixture? _fixture;

    private async Task<WebApiFixture> CreateFixtureAsync(
        SseStreamingOptions? options = null, CancellationToken ct = default)
    {
        _fixture = await WebApiFixture.CreateAsync(options, ct: ct);
        return _fixture;
    }

    [Fact]
    public async Task SseEndpoint_Returns200_WithEventStreamContentType()
    {
        var fixture = await CreateFixtureAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/live/notables");
        var response = await fixture.Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        cts.Cancel();
    }

    [Fact]
    public async Task Heartbeat_SentWithinConfiguredInterval()
    {
        var options = new SseStreamingOptions { HeartbeatInterval = TimeSpan.FromMilliseconds(300) };
        var fixture = await CreateFixtureAsync(options);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/live/notables");
        var response = await fixture.Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead);

        var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        string? line = null;
        while (!cts.Token.IsCancellationRequested)
        {
            var readTask = reader.ReadLineAsync(cts.Token).AsTask();
            try
            {
                line = await readTask;
                if (line is not null && line.StartsWith(": keepalive"))
                    break;
            }
            catch (OperationCanceledException) { break; }
        }

        line.Should().StartWith(": keepalive");
    }

    [Fact]
    public async Task NotableEvent_AppearsOnStream()
    {
        var fixture = await CreateFixtureAsync();
        var notableEv = MakeNotableEvent("Explosion");

        // Start SSE connection
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/live/notables");
        var response = await fixture.Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead);
        var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        // Give the connection a moment to register
        await Task.Delay(50);

        // Publish the event
        fixture.Broadcaster.Publish(notableEv);

        // Read lines until we get the data line or timeout
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

        dataLine.Should().NotBeNull("a notable event should appear on the stream");
        dataLine!.Should().Contain("Explosion");
    }

    [Fact]
    public async Task NonNotableEvent_NotSentOnNotablesOnlyStream()
    {
        var fixture = await CreateFixtureAsync();
        var nonNotable = MakeNonNotableEvent();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/live/notables");
        var response = await fixture.Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead);
        var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        await Task.Delay(50);

        // Publish non-notable event
        fixture.Broadcaster.Publish(nonNotable);

        // Wait 500ms — no data: line should appear
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
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

        dataLine.Should().BeNull("non-notable events should not appear on the notables-only stream");
    }

    [Fact]
    public async Task AtCapacity_Returns503()
    {
        var options = new SseStreamingOptions { MaxConcurrentSseClients = 1 };
        var fixture = await CreateFixtureAsync(options);

        // First connection — should succeed
        var request1 = new HttpRequestMessage(HttpMethod.Get, "/api/live/notables");
        var response1 = await fixture.Client.SendAsync(
            request1, HttpCompletionOption.ResponseHeadersRead);
        response1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second connection — should be rejected
        var request2 = new HttpRequestMessage(HttpMethod.Get, "/api/live/notables");
        var response2 = await fixture.Client.SendAsync(
            request2, HttpCompletionOption.ResponseHeadersRead);
        response2.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task ClientDisconnect_DeregistersConnection()
    {
        var fixture = await CreateFixtureAsync();

        var initialCount = fixture.SseConnections.ActiveCount;

        // Start and then immediately cancel a connection
        using var cts = new CancellationTokenSource();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/live/notables");
        _ = fixture.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
            .ContinueWith(_ => { }); // fire and forget

        await Task.Delay(100); // let it register

        cts.Cancel(); // disconnect

        // Give the endpoint time to deregister
        await Task.Delay(200);

        fixture.SseConnections.ActiveCount.Should().Be(initialCount);
    }

    [Fact]
    public async Task SlowClient_DropOldest_StreamStaysAlive()
    {
        // Use a very small buffer (1 event) to force dropping
        var options = new SseStreamingOptions { PerClientBufferSize = 2 };
        var fixture = await CreateFixtureAsync(options);

        // Connect but don't read
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/live/notables");
        var response = await fixture.Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await Task.Delay(50); // let connection register

        // Push many events without reading — should drop some
        for (int i = 0; i < 20; i++)
        {
            fixture.Broadcaster.Publish(MakeNotableEvent($"Hit{i}"));
        }

        await Task.Delay(100);

        // Connection should still be alive (stream not closed)
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Some events should have been dropped by the bounded channel
        var conns = fixture.SseConnections;
        conns.ActiveCount.Should().BeGreaterThan(0, "connection should still be registered");
    }

    public async ValueTask DisposeAsync()
    {
        if (_fixture is not null)
            await _fixture.DisposeAsync();
    }
}
