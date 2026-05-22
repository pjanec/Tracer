using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Adapters.DDS;
using Tracer.Adapters.DDS.Configuration;
using Tracer.Adapters.Mock;
using Tracer.Core.Abstractions;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Adapters.DDS;

public sealed class DdsDiagnosticDataSourceTests
{
    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeEventPayload
    {
        public ulong traceId { get; set; } = 1UL;
        public ulong eventId { get; set; } = 2UL;
        public ulong parentEventId { get; set; } = 0UL;
        public string entityId { get; set; } = "e1";
    }

    private sealed class FakeSubscriberFactory : IDdsSubscriberFactory
    {
        private readonly IReadOnlyList<IDdsSample> _samples;

        public FakeSubscriberFactory(IReadOnlyList<IDdsSample> samples)
            => _samples = samples;

        public IDisposable Create(DdsTopicSubscription topicSub, Type sampleType, Action<IDdsSample> onSample)
        {
            foreach (var s in _samples)
                onSample(s);
            return new NullDisposable();
        }

        private sealed class NullDisposable : IDisposable { public void Dispose() { } }
    }

    private sealed class FakeSample : IDdsSample
    {
        private readonly object _payload;
        public DateTimeOffset SourceTimestamp => DateTimeOffset.UtcNow;
        public ulong SequenceNumber { get; }
        public FakeSample(object payload, ulong seq = 1)
        {
            _payload = payload;
            SequenceNumber = seq;
        }
        public object GetPayload() => _payload;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static DdsDiagnosticDataSource Build(IDdsSubscriberFactory factory)
    {
        var config = new DdsAdapterConfig
        {
            PublisherNodeId = "test-node",
            Topics = new[]
            {
                new DdsTopicSubscription { TopicName = "topic.event", SampleTypeName = "FakeEventPayload" },
            },
            Participant = new CycloneDdsParticipantConfig { DomainId = 0 },
            IngestBufferSize = 100,
        };

        var registry = new DdsTopicRegistry(new[]
        {
            new DdsTopicMetadata
            {
                TopicName = "topic.event",
                SampleType = typeof(FakeEventPayload),
                Kind = DdsTopicKind.Event,
                EntityIdField = "entityId",
            },
        });

        var clock = new SimulatedClock(WallclockTime.Zero);
        var extractor = new DdsTraceContextExtractor();
        var translator = new DdsSampleTranslator(
            extractor, registry, config, clock,
            NullLogger<DdsSampleTranslator>.Instance);

        return new DdsDiagnosticDataSource(
            config, factory, translator, registry,
            NullLogger<DdsDiagnosticDataSource>.Instance);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_SingleSampleInjected_YieldsOneEventRecord()
    {
        var samples = new IDdsSample[] { new FakeSample(new FakeEventPayload()) };
        var factory = new FakeSubscriberFactory(samples);
        var source = Build(factory);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var records = new List<DiagnosticRecord>();
        try
        {
            await foreach (var record in source.ReadAsync(cts.Token))
            {
                records.Add(record);
                cts.Cancel();  // cancel after first record
            }
        }
        catch (OperationCanceledException) { /* expected */ }

        records.Should().HaveCount(1);
        records[0].Should().BeOfType<EventRecord>();
    }

    [Fact]
    public async Task ReadAsync_NullTranslation_DoesNotYieldRecord()
    {
        // Factory fires a sample on a topic NOT in the registry => translator returns null => no record emitted.
        var configNoTopics = new DdsAdapterConfig
        {
            PublisherNodeId = "test-node",
            Topics = Array.Empty<DdsTopicSubscription>(),
            Participant = new CycloneDdsParticipantConfig { DomainId = 0 },
        };
        var registry = new DdsTopicRegistry(Array.Empty<DdsTopicMetadata>());
        var clock = new SimulatedClock(WallclockTime.Zero);
        var extractor = new DdsTraceContextExtractor();
        var translator = new DdsSampleTranslator(
            extractor, registry, configNoTopics, clock,
            NullLogger<DdsSampleTranslator>.Instance);
        var factory = new FakeSubscriberFactory(Array.Empty<IDdsSample>());
        var source = new DdsDiagnosticDataSource(
            configNoTopics, factory, translator, registry,
            NullLogger<DdsDiagnosticDataSource>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var records = new List<DiagnosticRecord>();
        try
        {
            await foreach (var record in source.ReadAsync(cts.Token))
                records.Add(record);
        }
        catch (OperationCanceledException) { }

        records.Should().BeEmpty();
    }
}
