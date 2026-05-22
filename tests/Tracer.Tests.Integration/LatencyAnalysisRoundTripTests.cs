using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Util;
using Xunit;

namespace Tracer.Tests.Integration;

/// <summary>
/// Integration tests: push synthetic events with per-subscriber latency variations,
/// query Phase 9 latency endpoints, verify distribution and outlier detection.
/// TRC-P9-019 (SC-15 and SC-16).
/// </summary>
[Collection("LatencyAnalysisIntegration")]
public sealed class LatencyAnalysisRoundTripTests : IAsyncLifetime
{
    private ObserverFixture? _observer;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class TestBundleSentinel : IBundleModeMarker { }

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromDateTimeOffset(dto);

    public async Task InitializeAsync()
    {
        _observer = await ObserverFixture.CreateAsync(
            configureExtraServices: services =>
                services.AddSingleton<IBundleModeMarker>(_ => new TestBundleSentinel()));

        // Push session_start
        await _observer.PushAsync(MakeSessionStart());

        // Push events from publisher node-A to two subscriber nodes (node-B healthy, node-C degraded)
        var rng = new Random(42);
        for (var i = 0; i < 200; i++)
        {
            var publishAt = BaseTime.AddSeconds(i * 5);
            var seqNum = (ulong)(i + 1);

            // node-B: healthy latency 1-3ms
            var latencyB = 1.0 + rng.NextDouble() * 2.0;
            await _observer.PushAsync(MakeLatencyEvent(
                "weapons.fire", "node-A", "node-B",
                seqNum, publishAt, publishAt.AddMilliseconds(latencyB)));

            // node-C: degraded latency 20-40ms
            var latencyC = 20.0 + rng.NextDouble() * 20.0;
            await _observer.PushAsync(MakeLatencyEvent(
                "weapons.fire", "node-A", "node-C",
                seqNum, publishAt, publishAt.AddMilliseconds(latencyC)));
        }
    }

    public async Task DisposeAsync()
    {
        if (_observer is not null)
            await _observer.DisposeAsync();
    }

    private EventRecord MakeSessionStart()
    {
        return new EventRecord
        {
            SequenceNumber = 0,
            PublishWallclock = At(BaseTime.AddSeconds(-5)),
            ReceiveWallclock = At(BaseTime.AddSeconds(-5)),
            PublisherNode = new AgentId("node-A"),
            SubscriberNode = new AgentId("node-A"),
            Topic = new TopicName("system.session_start"),
            EventId = new EventId(900_000),
            TraceId = new TraceId(900_000),
            PayloadJson = "{\"sessionId\":\"s1\",\"scenarioId\":null,\"label\":\"Latency Test\"}",
        };
    }

    private static EventRecord MakeLatencyEvent(
        string topic,
        string publisherNode,
        string subscriberNode,
        ulong seq,
        DateTimeOffset publishAt,
        DateTimeOffset receiveAt)
    {
        var id = seq * 1000 + (ulong)(subscriberNode == "node-B" ? 1 : 2);
        return new EventRecord
        {
            SequenceNumber = seq,
            PublishWallclock = At(publishAt),
            ReceiveWallclock = At(receiveAt),
            PublisherNode = new AgentId(publisherNode),
            SubscriberNode = new AgentId(subscriberNode),
            Topic = new TopicName(topic),
            EventId = new EventId(id),
            TraceId = new TraceId(id),
            PayloadJson = "{}",
        };
    }

    private static string BuildLatencyDistUrl(
        DateTimeOffset from, DateTimeOffset to,
        string? topic = null,
        string? publisherNode = null,
        string? subscriberNode = null)
    {
        var qs = $"from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";
        if (topic is not null) qs += $"&topic={Uri.EscapeDataString(topic)}";
        if (publisherNode is not null) qs += $"&publisherNode={Uri.EscapeDataString(publisherNode)}";
        if (subscriberNode is not null) qs += $"&subscriberNode={Uri.EscapeDataString(subscriberNode)}";
        return $"/api/latency/distribution?{qs}";
    }

    [Fact]
    public async Task HealthyNetwork_DistributionEndpoint_P99LessThan5ms()
    {
        var from = BaseTime;
        var to = BaseTime.AddSeconds(200 * 5 + 10);

        var res = await _observer!.Client.GetAsync(
            BuildLatencyDistUrl(from, to, "weapons.fire", "node-A", "node-B"));
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await res.Content.ReadAsStringAsync();
        var dto = JsonSerializer.Deserialize<LatencyDistributionDto>(json, CamelCaseOptions);
        dto.Should().NotBeNull();
        dto!.SampleCount.Should().BeGreaterThan(0, "we pushed 200 events for node-B");
        dto.P99Ms.Should().BeLessThan(5.0, "healthy link has latency 1-3ms");
    }

    [Fact]
    public async Task DegradedNetwork_DistributionEndpoint_P99GreaterThan15ms()
    {
        var from = BaseTime;
        var to = BaseTime.AddSeconds(200 * 5 + 10);

        var res = await _observer!.Client.GetAsync(
            BuildLatencyDistUrl(from, to, "weapons.fire", "node-A", "node-C"));
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await res.Content.ReadAsStringAsync();
        var dto = JsonSerializer.Deserialize<LatencyDistributionDto>(json, CamelCaseOptions);
        dto.Should().NotBeNull();
        dto!.SampleCount.Should().BeGreaterThan(0, "we pushed 200 events for node-C");
        dto.P99Ms.Should().BeGreaterThan(15.0, "degraded link has latency 20-40ms");
    }

    [Fact]
    public async Task PairsEndpoint_ReturnsBothSubscriberLegs()
    {
        var from = BaseTime;
        var to = BaseTime.AddSeconds(200 * 5 + 10);

        var qs = $"from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}&minSamples=1";
        var res = await _observer!.Client.GetAsync($"/api/latency/pairs?{qs}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await res.Content.ReadAsStringAsync();
        var pairs = JsonSerializer.Deserialize<LatencyPairSummaryDto[]>(json, CamelCaseOptions);
        pairs.Should().NotBeNull();
        pairs!.Select(p => p.SubscriberNode).Should().Contain("node-B");
        pairs!.Select(p => p.SubscriberNode).Should().Contain("node-C");
    }

    [Fact]
    public async Task LiveMode_DistributionEndpoint_Returns409WithoutBundleMarker()
    {
        // Create a separate Observer fixture WITHOUT the bundle mode marker
        await using var liveObserver = await ObserverFixture.CreateAsync();

        var from = BaseTime;
        var to = BaseTime.AddHours(1);
        var res = await liveObserver.Client.GetAsync(BuildLatencyDistUrl(from, to));
        res.StatusCode.Should().Be(HttpStatusCode.Conflict, "live mode should return 409");
    }
}
