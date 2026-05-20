using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tracer.Agent.Lifecycle;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB.MultiInterval;
using Tracer.TestHarness.Observer;
using Xunit;

namespace Tracer.Tests.Integration;

/// <summary>
/// Verifies that <see cref="LiveMultiIntervalReader"/> serves queries across multiple intervals.
/// </summary>
public sealed class LiveMultiIntervalQueryTests : IAsyncLifetime
{
    private ObserverFixture _fixture = null!;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 9_000;

    private EventRecord MakeSessionStart(string sessionId, string nodeId = "node-a")
    {
        var payload = JsonSerializer.Serialize(new
        {
            sessionId,
            scenarioId = "live-multi",
            label = "Live Multi Interval Test",
        });
        return new EventRecord
        {
            SequenceNumber = _nextId++,
            PublishWallclock = At(BaseTime),
            ReceiveWallclock = At(BaseTime),
            PublisherNode = new AgentId(nodeId),
            SubscriberNode = new AgentId(nodeId),
            Topic = new TopicName("system.session_start"),
            EventId = new EventId(_nextId++),
            TraceId = new TraceId(_nextId++),
            PayloadJson = payload,
        };
    }

    public async Task InitializeAsync()
    {
        _fixture = await ObserverFixture.CreateAsync(
            new ObserverFixtureOptions
            {
                IntervalDuration = TimeSpan.FromMinutes(1),
            });
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    /// <summary>Sessions pushed across three intervals are all returned by the query API.</summary>
    [Fact]
    public async Task QuerySpansThreeIntervals_AllSessionsReturned()
    {
        var session1 = $"lmq-iv1-{Guid.NewGuid():N}";
        var session2 = $"lmq-iv2-{Guid.NewGuid():N}";
        var session3 = $"lmq-iv3-{Guid.NewGuid():N}";

        // Interval 1
        await _fixture.PushAsync([MakeSessionStart(session1)]);
        await _fixture.ForceRotationAsync();

        // Interval 2
        await _fixture.PushAsync([MakeSessionStart(session2)]);
        await _fixture.ForceRotationAsync();

        // Interval 3 (active)
        await _fixture.PushAsync([MakeSessionStart(session3)]);
        await Task.Delay(50); // allow flush

        var response = await _fixture.Client.GetAsync("/api/sessions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var sessionIds = doc.RootElement.EnumerateArray()
            .Select(s => s.GetProperty("sessionId").GetString())
            .ToHashSet();

        sessionIds.Should().Contain(session1, "session from interval 1 must be included");
        sessionIds.Should().Contain(session2, "session from interval 2 must be included");
        sessionIds.Should().Contain(session3, "session from active interval must be included");
    }

    /// <summary>After a rotation, events pushed in the new interval appear in queries.</summary>
    [Fact]
    public async Task AfterRotation_NewIntervalEventsIncluded()
    {
        var sessionBefore = $"lmq-before-{Guid.NewGuid():N}";
        var sessionAfter  = $"lmq-after-{Guid.NewGuid():N}";

        // Push to interval 1, rotate
        await _fixture.PushAsync([MakeSessionStart(sessionBefore)]);
        await _fixture.ForceRotationAsync();

        // Push to interval 2 (now active)
        await _fixture.PushAsync([MakeSessionStart(sessionAfter)]);
        await Task.Delay(50);

        var response = await _fixture.Client.GetAsync("/api/sessions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var sessionIds = doc.RootElement.EnumerateArray()
            .Select(s => s.GetProperty("sessionId").GetString())
            .ToHashSet();

        sessionIds.Should().Contain(sessionBefore, "session from completed interval must still be visible");
        sessionIds.Should().Contain(sessionAfter, "session from new active interval must appear");
    }

    /// <summary>After an interval is evicted from the tracker, its events are excluded from queries.</summary>
    [Fact]
    public async Task AfterEviction_EvictedIntervalEventsExcluded()
    {
        var sessionEvicted = $"lmq-evict-{Guid.NewGuid():N}";
        var sessionKept    = $"lmq-kept-{Guid.NewGuid():N}";

        var rotator = _fixture.App.Services.GetRequiredService<IntervalRotator>();
        var tracker = _fixture.App.Services.GetRequiredService<IntervalSetTracker>();

        // Push session to interval 1, record directory before rotating away
        await _fixture.PushAsync([MakeSessionStart(sessionEvicted)]);
        var interval1Dir = rotator.CurrentDirectory!;
        await _fixture.ForceRotationAsync();

        // Push session to interval 2 (current active)
        await _fixture.PushAsync([MakeSessionStart(sessionKept)]);
        await Task.Delay(50);

        // Verify both sessions are visible before eviction
        var beforeEviction = await _fixture.Client.GetAsync("/api/sessions");
        var jsonBefore = await beforeEviction.Content.ReadAsStringAsync();
        using var docBefore = JsonDocument.Parse(jsonBefore);
        var idsBefore = docBefore.RootElement.EnumerateArray()
            .Select(s => s.GetProperty("sessionId").GetString())
            .ToHashSet();
        idsBefore.Should().Contain(sessionEvicted, "session must be visible before eviction");
        idsBefore.Should().Contain(sessionKept);

        // Evict interval 1 from the tracker
        await tracker.OnIntervalEvictedAsync(interval1Dir, default);
        await Task.Delay(100); // allow pool rebuild

        // After eviction, session from interval 1 must no longer appear
        var afterEviction = await _fixture.Client.GetAsync("/api/sessions");
        afterEviction.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonAfter = await afterEviction.Content.ReadAsStringAsync();
        using var docAfter = JsonDocument.Parse(jsonAfter);
        var idsAfter = docAfter.RootElement.EnumerateArray()
            .Select(s => s.GetProperty("sessionId").GetString())
            .ToHashSet();

        idsAfter.Should().NotContain(sessionEvicted,
            "session from evicted interval must be excluded from queries");
        idsAfter.Should().Contain(sessionKept,
            "session from non-evicted active interval must still be visible");
    }
}
