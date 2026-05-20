using FluentAssertions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.WebApi.Streaming;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class SseFilterTests
{
    private static readonly WallclockTime Now =
        WallclockTime.FromUnixNanoseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L);

    private static ulong _nextId = 50000;

    private static EventRecord MakeEvent(
        string topic = "game.tick",
        string node = "node-a",
        ulong? traceId = null,
        string? entityId = null,
        string? playerId = null,
        Severity? severity = null,
        string? notableLabel = null,
        string? payloadJson = null)
    {
        var id = _nextId++;
        return new EventRecord
        {
            SequenceNumber = id,
            PublishWallclock = Now,
            ReceiveWallclock = Now,
            PublisherNode = new AgentId(node),
            SubscriberNode = new AgentId(node),
            Topic = new TopicName(topic),
            EventId = new EventId(id),
            TraceId = new TraceId(traceId ?? id),
            PayloadJson = payloadJson ?? "{}",
            EntityId = entityId is not null ? new EntityId(entityId) : null,
            OwningPlayerId = playerId,
            Severity = severity,
            NotableLabel = notableLabel,
        };
    }

    [Fact]
    public void Matches_EmptyFilter_AllEventsMatch()
    {
        var filter = new SseFilter();
        var ev = MakeEvent();

        filter.Matches(ev).Should().BeTrue();
    }

    [Fact]
    public void Matches_NotablesOnly_ExcludesEventsWithoutLabel()
    {
        var filter = new SseFilter { NotablesOnly = true };

        filter.Matches(MakeEvent(notableLabel: "CriticalHit")).Should().BeTrue();
        filter.Matches(MakeEvent(notableLabel: null)).Should().BeFalse();
    }

    [Fact]
    public void Matches_TopicFilter_ExcludesNonMatchingTopic()
    {
        var filter = new SseFilter { Topics = new HashSet<string> { "game.tick" } };

        filter.Matches(MakeEvent(topic: "game.tick")).Should().BeTrue();
        filter.Matches(MakeEvent(topic: "system.heartbeat")).Should().BeFalse();
    }

    [Fact]
    public void Matches_MultipleTopics_MatchesAnyListed()
    {
        var filter = new SseFilter { Topics = new HashSet<string> { "alpha.event", "beta.event" } };

        filter.Matches(MakeEvent(topic: "alpha.event")).Should().BeTrue("event on first topic should match");
        filter.Matches(MakeEvent(topic: "beta.event")).Should().BeTrue("event on second topic should match");
        filter.Matches(MakeEvent(topic: "gamma.event")).Should().BeFalse("event on unlisted topic should not match");
    }

    [Fact]
    public void Matches_NodeFilter_ExcludesNonMatchingNode()
    {
        var filter = new SseFilter { Nodes = new HashSet<string> { "node-a", "node-b" } };

        filter.Matches(MakeEvent(node: "node-a")).Should().BeTrue();
        filter.Matches(MakeEvent(node: "node-c")).Should().BeFalse();
    }

    [Fact]
    public void Matches_TraceIdFilter_ExcludesNonMatchingTrace()
    {
        ulong traceVal = 0xABCDEF1234567890UL;
        var hexString = traceVal.ToString("X16");
        var filter = new SseFilter { TraceId = hexString };

        filter.Matches(MakeEvent(traceId: traceVal)).Should().BeTrue();
        filter.Matches(MakeEvent(traceId: traceVal + 1)).Should().BeFalse();
    }

    [Fact]
    public void Matches_EntityIdFilter_ExcludesNonMatchingEntityId()
    {
        var filter = new SseFilter { EntityIds = new HashSet<string> { "entity-42" } };

        filter.Matches(MakeEvent(entityId: "entity-42")).Should().BeTrue();
        filter.Matches(MakeEvent(entityId: "entity-99")).Should().BeFalse();
        filter.Matches(MakeEvent(entityId: null)).Should().BeFalse();
    }

    [Fact]
    public void Matches_PlayerIdFilter_ExcludesNonMatchingPlayerId()
    {
        var filter = new SseFilter { PlayerIds = new HashSet<string> { "player-1" } };

        filter.Matches(MakeEvent(playerId: "player-1")).Should().BeTrue();
        filter.Matches(MakeEvent(playerId: "player-2")).Should().BeFalse();
        filter.Matches(MakeEvent(playerId: null)).Should().BeFalse();
    }

    [Fact]
    public void Matches_SeverityFilter_ExcludesNonMatchingSeverity()
    {
        var filter = new SseFilter { Severities = new HashSet<string> { "Warning" } };

        filter.Matches(MakeEvent(severity: Severity.Warning)).Should().BeTrue();
        filter.Matches(MakeEvent(severity: Severity.Error)).Should().BeFalse();
        filter.Matches(MakeEvent(severity: null)).Should().BeFalse();
    }

    [Fact]
    public void Matches_MultipleFilterTypesCompose_RequiresAllToMatch()
    {
        var filter = new SseFilter
        {
            Topics = new HashSet<string> { "game.tick" },
            NotablesOnly = true,
        };

        // Matches both
        filter.Matches(MakeEvent(topic: "game.tick", notableLabel: "Hit")).Should().BeTrue();
        // Wrong topic
        filter.Matches(MakeEvent(topic: "system.heartbeat", notableLabel: "Hit")).Should().BeFalse();
        // Not notable
        filter.Matches(MakeEvent(topic: "game.tick", notableLabel: null)).Should().BeFalse();
    }

    [Fact]
    public void SessionId_IsFilteredByPayloadJsonMatch()
    {
        // SessionId is matched against the "sessionId" field in event's PayloadJson
        var filter = new SseFilter { SessionId = "session-abc" };

        var evMatching = MakeEvent(payloadJson: "{\"sessionId\":\"session-abc\"}");
        var evNonMatching = MakeEvent(payloadJson: "{\"sessionId\":\"session-xyz\"}");
        var evNoSession = MakeEvent();  // no sessionId in payload

        filter.Matches(evMatching).Should().BeTrue("event with matching sessionId should pass");
        filter.Matches(evNonMatching).Should().BeFalse("event with different sessionId should be filtered");
        filter.Matches(evNoSession).Should().BeFalse("event without sessionId in payload should be filtered");
    }

    [Fact]
    public void Matches_NullArgument_ThrowsArgumentNullException()
    {
        var filter = new SseFilter();
        var act = () => filter.Matches(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
