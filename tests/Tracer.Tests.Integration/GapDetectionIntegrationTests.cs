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
/// Integration tests: push events with deliberate sequence gaps, then verify
/// the gap detection endpoint identifies them.
/// TRC-P9-019 (SC-17).
/// </summary>
[Collection("GapDetectionIntegration")]
public sealed class GapDetectionIntegrationTests : IAsyncLifetime
{
    private ObserverFixture? _observer;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 7, 2, 10, 0, 0, TimeSpan.Zero);

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

        // Push a session start
        await _observer.PushAsync(MakeEvent(0, BaseTime.AddSeconds(-5)));

        // Push events with a deliberate gap:
        // sequences 1..5 present, then skip to 10..14 (gap at 6-9 = 4 missing)
        for (ulong i = 1; i <= 5; i++)
            await _observer.PushAsync(MakeEvent(i, BaseTime.AddSeconds((double)i)));

        for (ulong i = 10; i <= 14; i++)
            await _observer.PushAsync(MakeEvent(i, BaseTime.AddSeconds((double)i)));
    }

    public async Task DisposeAsync()
    {
        if (_observer is not null)
            await _observer.DisposeAsync();
    }

    private static EventRecord MakeEvent(ulong seq, DateTimeOffset at)
    {
        return new EventRecord
        {
            SequenceNumber = seq,
            PublishWallclock = At(at),
            ReceiveWallclock = At(at.AddMilliseconds(2)),
            PublisherNode = new AgentId("node-A"),
            SubscriberNode = new AgentId("node-B"),
            Topic = new TopicName("weapons.fire"),
            EventId = new EventId(seq + 1_000_000),
            TraceId = new TraceId(seq + 1_000_000),
            PayloadJson = "{}",
        };
    }

    [Fact]
    public async Task LossyNetwork_GapsEndpoint_ReturnsGaps()
    {
        var from = BaseTime.AddSeconds(-10);
        var to = BaseTime.AddSeconds(20);
        var qs = $"from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}"
                 + "&publisherNode=node-A&subscriberNode=node-B&topic=weapons.fire";

        var res = await _observer!.Client.GetAsync($"/api/gaps?{qs}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await res.Content.ReadAsStringAsync();
        var dto = JsonSerializer.Deserialize<GapResultDto>(json, CamelCaseOptions);
        dto.Should().NotBeNull();
        dto!.Gaps.Should().NotBeEmpty("we deliberately skipped sequence numbers 6-9");
        dto.Gaps.Should().Contain(g => g.MissingCount >= 4,
            "there should be at least 4 missing events in the gap");
    }

    [Fact]
    public async Task LiveMode_GapsEndpoint_Returns409WithoutBundleMarker()
    {
        await using var liveObserver = await ObserverFixture.CreateAsync();

        var from = BaseTime;
        var to = BaseTime.AddHours(1);
        var qs = $"from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}"
                 + "&publisherNode=node-A&subscriberNode=node-B&topic=weapons.fire";

        var res = await liveObserver.Client.GetAsync($"/api/gaps?{qs}");
        res.StatusCode.Should().Be(HttpStatusCode.Conflict, "live mode should return 409");
    }
}
