using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Adapters.DDS;
using Tracer.Adapters.DDS.Configuration;
using Tracer.Adapters.Mock;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Adapters.DDS;

public sealed class DdsSampleTranslatorTests
{
    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeSample : IDdsSample
    {
        private readonly object _payload;
        public DateTimeOffset SourceTimestamp { get; }
        public ulong SequenceNumber { get; }

        public FakeSample(object payload, ulong seq = 1, DateTimeOffset? ts = null)
        {
            _payload = payload;
            SequenceNumber = seq;
            SourceTimestamp = ts ?? new DateTimeOffset(2026, 5, 19, 14, 0, 0, TimeSpan.Zero);
        }

        public object GetPayload() => _payload;
    }

    /// <summary>Minimal event payload with trace fields.</summary>
    private sealed class FakeEventPayload
    {
        public ulong traceId { get; set; } = 42UL;
        public ulong eventId { get; set; } = 7UL;
        public ulong parentEventId { get; set; } = 0UL;
        public string entityId { get; set; } = "entity-1";
        public string? notableLabel { get; set; }
    }

    private sealed class FakeSlowStatePayload
    {
        public string instanceKey { get; set; } = "inst-1";
    }

    private sealed class FakeFastStatePayload
    {
        public string instanceKey { get; set; } = "inst-fast-1";
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static DdsSampleTranslator BuildTranslator(
        DdsTopicRegistry? registry = null,
        string publisherNode = "pub-node",
        IClock? clock = null)
    {
        registry ??= new DdsTopicRegistry(new[]
        {
            new DdsTopicMetadata
            {
                TopicName = "topic.event",
                SampleType = typeof(FakeEventPayload),
                Kind = DdsTopicKind.Event,
                EntityIdField = "entityId",
            },
            new DdsTopicMetadata
            {
                TopicName = "topic.slow",
                SampleType = typeof(FakeSlowStatePayload),
                Kind = DdsTopicKind.SlowState,
                EntityIdField = null,
                InstanceKeyField = "instanceKey",
            },
            new DdsTopicMetadata
            {
                TopicName = "topic.fast",
                SampleType = typeof(FakeFastStatePayload),
                Kind = DdsTopicKind.FastState,
                EntityIdField = null,
                InstanceKeyField = "instanceKey",
            },
        });

        var config = new DdsAdapterConfig
        {
            PublisherNodeId = publisherNode,
            Topics = new[]
            {
                new DdsTopicSubscription { TopicName = "topic.event", SampleTypeName = "FakeEventPayload" },
                new DdsTopicSubscription { TopicName = "topic.slow", SampleTypeName = "FakeSlowStatePayload" },
                new DdsTopicSubscription { TopicName = "topic.fast", SampleTypeName = "FakeFastStatePayload" },
            },
            Participant = new CycloneDdsParticipantConfig { DomainId = 0 },
        };

        clock ??= new SimulatedClock(WallclockTime.Zero);

        var extractor = new DdsTraceContextExtractor();
        return new DdsSampleTranslator(
            extractor, registry, config, clock,
            NullLogger<DdsSampleTranslator>.Instance);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Translate_EventTopic_ReturnsEventRecord()
    {
        var translator = BuildTranslator();
        var sample = new FakeSample(new FakeEventPayload());
        var topicSub = new DdsTopicSubscription { TopicName = "topic.event", SampleTypeName = "FakeEventPayload" };

        var result = translator.Translate(sample, topicSub);

        result.Should().BeOfType<EventRecord>();
    }

    [Fact]
    public void Translate_EventTopic_ExtractsTraceContext()
    {
        var translator = BuildTranslator();
        var payload = new FakeEventPayload { traceId = 99UL, eventId = 55UL, parentEventId = 0UL };
        var sample = new FakeSample(payload);
        var topicSub = new DdsTopicSubscription { TopicName = "topic.event", SampleTypeName = "FakeEventPayload" };

        var result = (EventRecord)translator.Translate(sample, topicSub)!;

        result.TraceId.Value.Should().Be(99UL);
        result.EventId.Value.Should().Be(55UL);
        result.ParentEventId.Should().BeNull();
    }

    [Fact]
    public void Translate_EventTopic_NonZeroParentEventId_Propagated()
    {
        var translator = BuildTranslator();
        var payload = new FakeEventPayload { traceId = 1UL, eventId = 2UL, parentEventId = 3UL };
        var sample = new FakeSample(payload);
        var topicSub = new DdsTopicSubscription { TopicName = "topic.event", SampleTypeName = "FakeEventPayload" };

        var result = (EventRecord)translator.Translate(sample, topicSub)!;

        result.ParentEventId.Should().NotBeNull();
        result.ParentEventId!.Value.Value.Should().Be(3UL);
    }

    [Fact]
    public void Translate_EventTopic_ExtractsEntityId()
    {
        var translator = BuildTranslator();
        var payload = new FakeEventPayload { entityId = "my-entity" };
        var sample = new FakeSample(payload);
        var topicSub = new DdsTopicSubscription { TopicName = "topic.event", SampleTypeName = "FakeEventPayload" };

        var result = (EventRecord)translator.Translate(sample, topicSub)!;

        result.EntityId.Should().NotBeNull();
        result.EntityId!.Value.Value.Should().Be("my-entity");
    }

    [Fact]
    public void Translate_EventTopic_PublisherAndSubscriberAreLoopback()
    {
        var translator = BuildTranslator(publisherNode: "blue-cmd-01");
        var sample = new FakeSample(new FakeEventPayload());
        var topicSub = new DdsTopicSubscription { TopicName = "topic.event", SampleTypeName = "FakeEventPayload" };

        var result = translator.Translate(sample, topicSub)!;

        result.PublisherNode.Value.Should().Be("blue-cmd-01");
        result.SubscriberNode.Value.Should().Be("blue-cmd-01");
    }

    [Fact]
    public void Translate_SlowStateTopic_ReturnsStateSampleRecordWithSlowRate()
    {
        var translator = BuildTranslator();
        var sample = new FakeSample(new FakeSlowStatePayload());
        var topicSub = new DdsTopicSubscription { TopicName = "topic.slow", SampleTypeName = "FakeSlowStatePayload" };

        var result = translator.Translate(sample, topicSub);

        result.Should().BeOfType<StateSampleRecord>()
              .Which.Rate.Should().Be(StateSampleRate.Slow);
    }

    [Fact]
    public void Translate_FastStateTopic_ReturnsStateSampleRecordWithFastRate()
    {
        var translator = BuildTranslator();
        var sample = new FakeSample(new FakeFastStatePayload());
        var topicSub = new DdsTopicSubscription { TopicName = "topic.fast", SampleTypeName = "FakeFastStatePayload" };

        var result = translator.Translate(sample, topicSub);

        result.Should().BeOfType<StateSampleRecord>()
              .Which.Rate.Should().Be(StateSampleRate.Fast);
    }

    [Fact]
    public void Translate_UnknownTopic_ReturnsNull()
    {
        var translator = BuildTranslator();
        var sample = new FakeSample(new FakeEventPayload());
        var topicSub = new DdsTopicSubscription { TopicName = "topic.unknown", SampleTypeName = "X" };

        var result = translator.Translate(sample, topicSub);

        result.Should().BeNull();
    }

    [Fact]
    public void Translate_PublishWallclock_MappedFromSourceTimestamp()
    {
        var ts = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var translator = BuildTranslator();
        var sample = new FakeSample(new FakeEventPayload(), ts: ts);
        var topicSub = new DdsTopicSubscription { TopicName = "topic.event", SampleTypeName = "FakeEventPayload" };

        var result = translator.Translate(sample, topicSub)!;

        result.PublishWallclock.ToDateTimeOffset().Should().BeCloseTo(ts, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void Translate_SequenceNumber_CopiedFromSample()
    {
        var translator = BuildTranslator();
        var sample = new FakeSample(new FakeEventPayload(), seq: 77UL);
        var topicSub = new DdsTopicSubscription { TopicName = "topic.event", SampleTypeName = "FakeEventPayload" };

        var result = translator.Translate(sample, topicSub)!;

        result.SequenceNumber.Should().Be(77UL);
    }

    [Fact]
    public void Translate_TopicName_PropagatedToRecord()
    {
        var translator = BuildTranslator();
        var sample = new FakeSample(new FakeEventPayload());
        var topicSub = new DdsTopicSubscription { TopicName = "topic.event", SampleTypeName = "FakeEventPayload" };

        var result = translator.Translate(sample, topicSub)!;

        result.Topic.Value.Should().Be("topic.event");
    }
}
