using System.Text.Json;
using FluentAssertions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Contracts.Mapping;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class DtoMappingTests
{
    private static readonly WallclockTime Now =
        WallclockTime.FromUnixNanoseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L);

    private static EventRecord MakeEvent(
        ulong eventId = 1,
        ulong traceId = 1,
        string topic = "test.topic",
        string payloadJson = "{}",
        string? notableLabel = null) => new EventRecord
    {
        SequenceNumber = 1,
        PublishWallclock = Now,
        ReceiveWallclock = Now,
        PublisherNode = new AgentId("node-alpha"),
        SubscriberNode = new AgentId("node-alpha"),
        Topic = new TopicName(topic),
        EventId = new EventId(eventId),
        TraceId = new TraceId(traceId),
        ParentEventId = null,
        PayloadJson = payloadJson,
        NotableLabel = notableLabel,
        Severity = notableLabel is not null ? Severity.Warning : null,
    };

    [Fact]
    public void EventId_FormattedAs16CharUppercaseHex()
    {
        var hex = DtoMappers.ToHex(new EventId(255));
        hex.Should().Be("00000000000000FF");
        hex.Should().HaveLength(16);
    }

    [Fact]
    public void TraceId_FormattedAs16CharUppercaseHex()
    {
        var hex = DtoMappers.ToHex(new TraceId(255));
        hex.Should().Be("00000000000000FF");
        hex.Should().HaveLength(16);
    }

    [Fact]
    public void EventRecord_ToEventDto_AllFieldsMapped()
    {
        var ev = MakeEvent(eventId: 0xABCDEF, traceId: 0x123456, topic: "my.topic", payloadJson: @"{""x"":1}");
        var dto = DtoMappers.ToDto(ev);

        dto.EventId.Should().Be("0000000000ABCDEF");
        dto.TraceId.Should().Be("0000000000123456");
        dto.Topic.Should().Be("my.topic");
        dto.PublisherNode.Should().Be("node-alpha");
        dto.PayloadJson.Should().Be(@"{""x"":1}");
        dto.ParentEventId.Should().BeNull();
        dto.NotableLabel.Should().BeNull();
        dto.Severity.Should().BeNull();
    }

    [Fact]
    public void EventRecord_ToNotableEventDto_ExcludesSubscriberAndSequenceNumber()
    {
        var ev = MakeEvent(notableLabel: "CriticalHit");
        var dto = DtoMappers.ToNotableDto(ev);

        dto.EventId.Should().NotBeNullOrEmpty();
        dto.NotableLabel.Should().Be("CriticalHit");
        // NotableEventDto does not include subscriber or sequence number — only EventDto fields
        dto.Should().BeOfType<NotableEventDto>();
    }

    [Fact]
    public void NullableFields_SerializeAsMissingKeysNotNullLiterals()
    {
        var ev = MakeEvent(); // no notable, no severity, no parent
        var dto = DtoMappers.ToDto(ev);
        var json = JsonSerializer.Serialize(dto);

        // Fields with [JsonIgnore(Condition = WhenWritingNull)] should be absent, not "null"
        json.Should().NotContain("\"notableLabel\":null");
        json.Should().NotContain("\"severity\":null");
        json.Should().NotContain("\"parentEventId\":null");
    }

    [Fact]
    public void DateTimeOffset_RoundTripsThroughIso8601()
    {
        var ev = MakeEvent();
        var dto = DtoMappers.ToDto(ev);
        var json = JsonSerializer.Serialize(dto);

        // Should be parseable back as a DateTimeOffset
        using var doc = JsonDocument.Parse(json);
        var ts = doc.RootElement.GetProperty("OccurredAtUtc").GetDateTimeOffset();
        ts.Should().BeCloseTo(Now.ToDateTimeOffset(), precision: TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Severity_SerializesAsTitleCaseString()
    {
        var ev = MakeEvent(notableLabel: "Hit") with { Severity = Severity.Error };
        var dto = DtoMappers.ToDto(ev);
        var json = JsonSerializer.Serialize(dto);

        // Severity enum should serialize as "Error" not as an integer
        json.Should().Contain("Error");
    }

    [Fact]
    public void ParentEventId_IsNull_WhenParentIsEventIdNone()
    {
        var ev = MakeEvent() with { ParentEventId = EventId.None };
        var dto = DtoMappers.ToDto(ev);

        dto.ParentEventId.Should().BeNull();
    }

    [Fact]
    public void ParentEventId_IsPopulated_WhenParentIsSet()
    {
        var ev = MakeEvent() with { ParentEventId = new EventId(0xDEAD) };
        var dto = DtoMappers.ToDto(ev);

        dto.ParentEventId.Should().Be("000000000000DEAD");
    }
}
