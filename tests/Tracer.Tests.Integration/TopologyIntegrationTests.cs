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
/// Integration tests: push events from multiple nodes across multiple topics,
/// query the network topology endpoint, and verify node and edge counts.
/// TRC-P9-019 (SC-18).
/// </summary>
[Collection("TopologyIntegration")]
public sealed class TopologyIntegrationTests : IAsyncLifetime
{
    private ObserverFixture? _observer;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class TestBundleSentinel : IBundleModeMarker { }

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromDateTimeOffset(dto);

    // Multi-node topology:
    //   node-A --[weapons.fire]--> node-B  (100 messages)
    //   node-A --[weapons.fire]--> node-C  (80 messages)
    //   node-B --[sensor.telemetry]--> node-C  (50 messages)
    // Expected: 3 nodes, 3 edges
    private const int NodeCount = 3;
    private const int EdgeCount = 3;

    public async Task InitializeAsync()
    {
        _observer = await ObserverFixture.CreateAsync(
            configureExtraServices: services =>
                services.AddSingleton<IBundleModeMarker>(_ => new TestBundleSentinel()));

        for (ulong i = 1; i <= 100; i++)
            await _observer.PushAsync(MakeEvent("weapons.fire", "node-A", "node-B", i, BaseTime.AddSeconds((double)i)));

        for (ulong i = 1; i <= 80; i++)
            await _observer.PushAsync(MakeEvent("weapons.fire", "node-A", "node-C", 10_000 + i, BaseTime.AddSeconds((double)i)));

        for (ulong i = 1; i <= 50; i++)
            await _observer.PushAsync(MakeEvent("sensor.telemetry", "node-B", "node-C", 20_000 + i, BaseTime.AddSeconds((double)i)));
    }

    public async Task DisposeAsync()
    {
        if (_observer is not null)
            await _observer.DisposeAsync();
    }

    private static EventRecord MakeEvent(
        string topic, string publisherNode, string subscriberNode,
        ulong seq, DateTimeOffset at)
    {
        return new EventRecord
        {
            SequenceNumber = seq,
            PublishWallclock = At(at),
            ReceiveWallclock = At(at.AddMilliseconds(5)),
            PublisherNode = new AgentId(publisherNode),
            SubscriberNode = new AgentId(subscriberNode),
            Topic = new TopicName(topic),
            EventId = new EventId(seq + 2_000_000),
            TraceId = new TraceId(seq + 2_000_000),
            PayloadJson = "{}",
        };
    }

    [Fact]
    public async Task MultiNode_TopologyEndpoint_ReturnsCorrectNodeAndEdgeCounts()
    {
        var from = BaseTime;
        var to = BaseTime.AddSeconds(110);
        var qs = $"from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";

        var res = await _observer!.Client.GetAsync($"/api/topology/network?{qs}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await res.Content.ReadAsStringAsync();
        var dto = JsonSerializer.Deserialize<NetworkTopologyDto>(json, CamelCaseOptions);
        dto.Should().NotBeNull();
        dto!.Nodes.Should().HaveCount(NodeCount,
            $"we pushed events involving {NodeCount} distinct nodes");
        dto.Edges.Should().HaveCount(EdgeCount,
            $"we pushed events on {EdgeCount} distinct publisher→subscriber→topic paths");
    }

    [Fact]
    public async Task MultiNode_TopologyEndpoint_EdgeMessageCountsMatch()
    {
        var from = BaseTime;
        var to = BaseTime.AddSeconds(110);
        var qs = $"from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";

        var res = await _observer!.Client.GetAsync($"/api/topology/network?{qs}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await res.Content.ReadAsStringAsync();
        var dto = JsonSerializer.Deserialize<NetworkTopologyDto>(json, CamelCaseOptions);

        var aToB = dto!.Edges.FirstOrDefault(e =>
            e.PublisherNode == "node-A" && e.SubscriberNode == "node-B" && e.Topic == "weapons.fire");
        aToB.Should().NotBeNull();
        aToB!.MessageCount.Should().Be(100);

        var aToC = dto.Edges.FirstOrDefault(e =>
            e.PublisherNode == "node-A" && e.SubscriberNode == "node-C" && e.Topic == "weapons.fire");
        aToC.Should().NotBeNull();
        aToC!.MessageCount.Should().Be(80);
    }

    [Fact]
    public async Task LiveMode_TopologyEndpoint_Returns409WithoutBundleMarker()
    {
        await using var liveObserver = await ObserverFixture.CreateAsync();

        var from = BaseTime;
        var to = BaseTime.AddHours(1);
        var qs = $"from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";

        var res = await liveObserver.Client.GetAsync($"/api/topology/network?{qs}");
        res.StatusCode.Should().Be(HttpStatusCode.Conflict, "live mode should return 409");
    }
}
