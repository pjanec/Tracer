using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Streaming;
using Xunit;

namespace Tracer.Tests.Integration;

public sealed class LiveStreamingTests : IAsyncLifetime
{
    private ObserverFixture _fixture = null!;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2025, 4, 1, 8, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 5000;

    private static EventRecord MakeNotable(string sessionId, string nodeId = "sse-node")
    {
        var payload = JsonSerializer.Serialize(new { sessionId });
        return new EventRecord
        {
            SequenceNumber = _nextId++,
            PublishWallclock = At(BaseTime),
            ReceiveWallclock = At(BaseTime),
            PublisherNode = new AgentId(nodeId),
            SubscriberNode = new AgentId(nodeId),
            Topic = new TopicName("combat.event"),
            EventId = new EventId(_nextId++),
            TraceId = new TraceId(_nextId++),
            PayloadJson = payload,
            NotableLabel = "TestHit",
            Severity = Severity.Warning,
        };
    }

    public async Task InitializeAsync() =>
        _fixture = await ObserverFixture.CreateAsync(
            sseOptions: new SseStreamingOptions
            {
                HeartbeatInterval = TimeSpan.FromSeconds(1),
                MaxConcurrentSseClients = 50,
                PerClientBufferSize = 20,
            });

    public async Task DisposeAsync() =>
        await _fixture.DisposeAsync();

    [Fact]
    public async Task PushNotableEvents_AppearOnStreamInOrder()
    {
        // SC2: 5 notables → assert 5 data: lines within 5 seconds
        var sessionId = $"sse-order-{Guid.NewGuid():N}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/live/notables?sessionId={sessionId}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var response = await _fixture.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        var lines = new ConcurrentBag<string>();

        var readTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cts.Token);
                if (line is null) break;
                if (line.StartsWith("data: ")) lines.Add(line);
            }
        }, cts.Token);

        // Push 5 notable events
        for (int i = 0; i < 5; i++)
            await _fixture.PushAsync(MakeNotable(sessionId));

        // Wait for all 5 data lines
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (lines.Count < 5 && DateTime.UtcNow < deadline)
            await Task.Delay(50, CancellationToken.None);

        cts.Cancel();
        try { await readTask; } catch (OperationCanceledException) { }

        lines.Count.Should().Be(5, "all 5 notable events must appear on the stream");
    }

    [Fact]
    public async Task ClientReconnect_ReceivesNewEventsAfterReconnect()
    {
        // SC3: connect, receive, disconnect, reconnect, push new event, assert it arrives
        var sessionId = $"sse-reconnect-{Guid.NewGuid():N}";

        // First connection
        using (var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var req1 = new HttpRequestMessage(HttpMethod.Get, $"/api/live/notables?sessionId={sessionId}");
            req1.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            var resp1 = await _fixture.Client.SendAsync(req1, HttpCompletionOption.ResponseHeadersRead, cts1.Token);
            resp1.StatusCode.Should().Be(HttpStatusCode.OK);

            // Push one event
            await _fixture.PushAsync(MakeNotable(sessionId));

            // Read one data line
            var stream1 = await resp1.Content.ReadAsStreamAsync(cts1.Token);
            using var reader1 = new StreamReader(stream1);
            string? dataLine = null;
            var deadline1 = DateTime.UtcNow.AddSeconds(3);
            while (dataLine is null && DateTime.UtcNow < deadline1)
            {
                try
                {
                    var line = await reader1.ReadLineAsync(cts1.Token);
                    if (line?.StartsWith("data: ") == true) dataLine = line;
                }
                catch { break; }
            }
            dataLine.Should().NotBeNull("first event should arrive on first connection");

            // Disconnect
            cts1.Cancel();
        }

        // Wait briefly for the connection to be deregistered
        await Task.Delay(100, CancellationToken.None);

        // Second connection
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var req2 = new HttpRequestMessage(HttpMethod.Get, $"/api/live/notables?sessionId={sessionId}");
        req2.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var resp2 = await _fixture.Client.SendAsync(req2, HttpCompletionOption.ResponseHeadersRead, cts2.Token);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);

        var stream2 = await resp2.Content.ReadAsStreamAsync(cts2.Token);
        using var reader2 = new StreamReader(stream2);

        // Push second event after reconnect
        await _fixture.PushAsync(MakeNotable(sessionId));

        string? dataLine2 = null;
        var deadline2 = DateTime.UtcNow.AddSeconds(3);
        while (dataLine2 is null && DateTime.UtcNow < deadline2)
        {
            try
            {
                var line = await reader2.ReadLineAsync(cts2.Token);
                if (line?.StartsWith("data: ") == true) dataLine2 = line;
            }
            catch { break; }
        }
        cts2.Cancel();

        dataLine2.Should().NotBeNull("second event should arrive after reconnect");
    }

    [Fact]
    public async Task SlowClient_DropsCountedButStreamRemainsAlive()
    {
        // SC4: connect but don't read, enqueue 50 events directly (tight loop), verify drops > 0
        var sessionId = $"sse-slowclient-{Guid.NewGuid():N}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/live/notables?sessionId={sessionId}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var response = await _fixture.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Wait for the SseConnection to be registered in the manager
        SseConnection? conn = null;
        var regDeadline = DateTime.UtcNow.AddSeconds(2);
        while (conn is null && DateTime.UtcNow < regDeadline)
        {
            conn = _fixture.SseConnections.Connections.FirstOrDefault();
            if (conn is null) await Task.Delay(10, CancellationToken.None);
        }
        conn.Should().NotBeNull("SSE connection should be registered after HTTP request");

        // Enqueue 50 events in a tight synchronous loop — faster than the async SSE write loop
        // can drain, so items pile up in the bounded channel (capacity 20) and drop count rises.
        for (int i = 0; i < 50; i++)
            conn!.Enqueue(MakeNotable(sessionId));

        // Brief pause to let the SSE write loop process whatever it can
        await Task.Delay(50, CancellationToken.None);

        // Verify drops were counted
        conn!.DropCount.Should().BeGreaterThan(0,
            "enqueuing 50 events synchronously into a buffer of 20 must cause at least 30 drops");

        // Verify stream is still alive — push one more via PushAsync and read it
        var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        await _fixture.PushAsync(MakeNotable(sessionId));

        string? finalLine = null;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (finalLine is null && DateTime.UtcNow < deadline)
        {
            try
            {
                var line = await reader.ReadLineAsync(cts.Token);
                if (line?.StartsWith("data: ") == true) finalLine = line;
            }
            catch { break; }
        }
        cts.Cancel();

        finalLine.Should().NotBeNull("stream must still deliver events after drops");
    }

    [Fact]
    public async Task MultipleNodes_AllEventsAppearInUnifiedStream()
    {
        // SC5: 10 notables from node-alpha + 10 from node-beta concurrently, assert all 20 arrive
        var sessionId = $"sse-multinodes-{Guid.NewGuid():N}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/live/notables?sessionId={sessionId}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var response = await _fixture.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        var lines = new ConcurrentBag<string>();

        var readTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cts.Token);
                if (line is null) break;
                if (line.StartsWith("data: ")) lines.Add(line);
            }
        }, cts.Token);

        // Push 10 from each node concurrently
        var alphaTask = Task.Run(async () =>
        {
            for (int i = 0; i < 10; i++)
                await _fixture.PushAsync(MakeNotable(sessionId, "node-alpha"));
        });
        var betaTask = Task.Run(async () =>
        {
            for (int i = 0; i < 10; i++)
                await _fixture.PushAsync(MakeNotable(sessionId, "node-beta"));
        });
        await Task.WhenAll(alphaTask, betaTask);

        // Wait for all 20 events
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (lines.Count < 20 && DateTime.UtcNow < deadline)
            await Task.Delay(50, CancellationToken.None);

        cts.Cancel();
        try { await readTask; } catch (OperationCanceledException) { }

        lines.Count.Should().Be(20, "all 20 events from both nodes must appear on the unified stream");
    }

    [Fact]
    public async Task SessionFilter_ExcludesEventsFromOtherSession()
    {
        // SC6: connect filtered to sessionId=A; push events for A and B; only A's events arrive
        var sessionIdA = $"sse-filter-a-{Guid.NewGuid():N}";
        var sessionIdB = $"sse-filter-b-{Guid.NewGuid():N}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/live/notables?sessionId={sessionIdA}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var response = await _fixture.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        var dataLines = new ConcurrentBag<string>();

        var readTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cts.Token);
                if (line is null) break;
                if (line.StartsWith("data: ")) dataLines.Add(line);
            }
        }, cts.Token);

        // Push 3 events for A and 3 events for B
        for (int i = 0; i < 3; i++)
            await _fixture.PushAsync(MakeNotable(sessionIdA));
        for (int i = 0; i < 3; i++)
            await _fixture.PushAsync(MakeNotable(sessionIdB));

        // Wait for A's events
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (dataLines.Count < 3 && DateTime.UtcNow < deadline)
            await Task.Delay(50, CancellationToken.None);

        cts.Cancel();
        try { await readTask; } catch (OperationCanceledException) { }

        dataLines.Count.Should().Be(3, "only session A's events should pass the filter");
        foreach (var line in dataLines)
            line.Should().Contain(sessionIdA, "each event should belong to session A");
    }

    [Fact]
    public async Task Heartbeat_ReceivedWithinConfiguredInterval()
    {
        // SC7: with HeartbeatInterval=1s, assert ": keepalive" arrives within 1500ms
        var sessionId = $"sse-heartbeat-{Guid.NewGuid():N}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/live/notables?sessionId={sessionId}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var response = await _fixture.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        string? keepaliveLine = null;
        var deadline = DateTime.UtcNow.AddMilliseconds(1500);
        while (keepaliveLine is null && DateTime.UtcNow < deadline)
        {
            try
            {
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                readCts.CancelAfter(TimeSpan.FromMilliseconds(200));
                var line = await reader.ReadLineAsync(readCts.Token);
                if (line?.StartsWith(": keepalive") == true) keepaliveLine = line;
            }
            catch (OperationCanceledException) { /* timeout on individual read, keep looping */ }
        }
        cts.Cancel();

        keepaliveLine.Should().NotBeNull("a keepalive heartbeat should be received within 1500ms with 1s interval");
    }
}

