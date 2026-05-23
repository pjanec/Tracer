using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Tracer.Adapters.DDS.Configuration;
using Tracer.Core.Abstractions;
using Tracer.Core.Records;

namespace Tracer.Adapters.DDS;

/// <summary>
/// Production <see cref="IDiagnosticDataSource"/> that subscribes to DDS topics
/// and translates samples into <see cref="DiagnosticRecord"/> instances.
/// </summary>
public sealed class DdsDiagnosticDataSource : IDiagnosticDataSource
{
    private readonly DdsAdapterConfig _config;
    private readonly IDdsSubscriberFactory _subscriberFactory;
    private readonly DdsSampleTranslator _translator;
    private readonly DdsTopicRegistry _topicRegistry;
    private readonly ILogger<DdsDiagnosticDataSource> _logger;

    private int _dropBurstActive;
    private long _droppedCount;

    public long GetDroppedCount() => Interlocked.Read(ref _droppedCount);

    public DdsDiagnosticDataSource(
        DdsAdapterConfig config,
        IDdsSubscriberFactory subscriberFactory,
        DdsSampleTranslator translator,
        DdsTopicRegistry topicRegistry,
        ILogger<DdsDiagnosticDataSource> logger)
    {
        _config = config;
        _subscriberFactory = subscriberFactory;
        _translator = translator;
        _topicRegistry = topicRegistry;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<DiagnosticRecord> ReadAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateBounded<DiagnosticRecord>(
            new BoundedChannelOptions(_config.IngestBufferSize)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });

        var subscribers = new List<IDisposable>();
        foreach (var topicSub in _config.Topics)
        {
            var meta = _topicRegistry.Lookup(topicSub.TopicName);
            if (meta is null)
            {
                _logger.LogWarning("Topic {Topic} not in registry; skipping subscription", topicSub.TopicName);
                continue;
            }

            var sub = _subscriberFactory.Create(
                topicSub,
                meta.SampleType,
                sample => OnSampleReceived(sample, topicSub, channel.Writer, channel.Reader, _config.IngestBufferSize));
            subscribers.Add(sub);
        }

        try
        {
            while (await channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var record))
                    yield return record;
            }
        }
        finally
        {
            foreach (var s in subscribers)
                s.Dispose();
        }
    }

    private void OnSampleReceived(
        IDdsSample sample,
        DdsTopicSubscription topicSub,
        ChannelWriter<DiagnosticRecord> writer,
        ChannelReader<DiagnosticRecord> reader,
        int capacity)
    {
        try
        {
            var record = _translator.Translate(sample, topicSub);
            if (record is null) return;

            // Pre-check: DropOldest means TryWrite always succeeds, so we must
            // check the count before writing to detect that a drop will occur.
            if (reader.Count >= capacity)
            {
                Interlocked.Increment(ref _droppedCount);
                if (Interlocked.Exchange(ref _dropBurstActive, 1) == 0)
                    _logger.LogWarning(
                        "DDS ingest channel full (capacity={Capacity}), dropping oldest record for topic {Topic}",
                        capacity, topicSub.TopicName);
            }
            else
            {
                Interlocked.Exchange(ref _dropBurstActive, 0);
            }

            writer.TryWrite(record);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to translate DDS sample on topic {Topic}", topicSub.TopicName);
        }
    }
}
