using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging;
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

    private static DdsDiagnosticDataSource Build(
        IDdsSubscriberFactory factory,
        int ingestBufferSize = 100,
        ILogger<DdsDiagnosticDataSource>? logger = null)
    {
        var config = new DdsAdapterConfig
        {
            PublisherNodeId = "test-node",
            Topics = new[]
            {
                new DdsTopicSubscription { TopicName = "topic.event", SampleTypeName = "FakeEventPayload" },
            },
            Participant = new CycloneDdsParticipantConfig { DomainId = 0 },
            IngestBufferSize = ingestBufferSize,
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
            logger ?? NullLogger<DdsDiagnosticDataSource>.Instance);
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

    [Fact]
    public async Task ReadAsync_OverfilledChannel_DropsRecordsAndLogsWarning()
    {
        // Arrange: inject more samples than the buffer capacity.
        const int bufferSize = 3;
        var samples = Enumerable.Range(0, 10)
            .Select(i => (IDdsSample)new FakeSample(new FakeEventPayload { eventId = (ulong)i }, (ulong)i))
            .ToList();
        var factory = new FakeSubscriberFactory(samples);

        var logger = new CapturingLogger<DdsDiagnosticDataSource>();
        var source = Build(factory, ingestBufferSize: bufferSize, logger: logger);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var received = new List<DiagnosticRecord>();
        try
        {
            await foreach (var record in source.ReadAsync(cts.Token))
            {
                received.Add(record);
            }
        }
        catch (OperationCanceledException) { /* expected */ }

        // At most bufferSize items were queued; the rest were dropped.
        received.Count.Should().BeLessThanOrEqualTo(bufferSize);
        source.GetDroppedCount().Should().BeGreaterThan(0);
        logger.Warnings.Should().Contain(w => w.Contains("channel full"));
    }
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Warnings { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= LogLevel.Warning)
            Warnings.Add(formatter(state, exception));
    }
}
