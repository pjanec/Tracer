using Microsoft.Extensions.Logging;
using Tracer.Agent.Lifecycle;
using Tracer.Core.Records;
using Tracer.Observer.Sources;
using Tracer.WebApi.Streaming;

namespace Tracer.Observer.Lifecycle;

public sealed class ObserverIngestionPipeline
{
    private readonly IReadOnlyList<NamedDataSource> _sources;
    private readonly IntervalRotator _rotator;
    private readonly LiveEventBroadcaster _broadcaster;
    private readonly ObserverStateReporter _state;
    private readonly ILogger<ObserverIngestionPipeline> _logger;

    public ObserverIngestionPipeline(
        IReadOnlyList<NamedDataSource> sources,
        IntervalRotator rotator,
        LiveEventBroadcaster broadcaster,
        ObserverStateReporter state,
        ILogger<ObserverIngestionPipeline> logger)
    {
        _sources = sources;
        _rotator = rotator;
        _broadcaster = broadcaster;
        _state = state;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _logger.LogInformation("Observer ingestion starting with {SourceCount} source(s)", _sources.Count);

        var tasks = _sources.Select(s => RunOneSourceAsync(s, ct)).ToList();
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Observer ingestion stopping (cancelled)");
        }
    }

    private async Task RunOneSourceAsync(NamedDataSource source, CancellationToken ct)
    {
        try
        {
            await foreach (var record in source.Source.ReadAsync(ct))
            {
                await ProcessOneAsync(record, ct);
            }
            _logger.LogInformation("Source {Source} completed", source.Name);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* normal shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Source {Source} failed unrecoverably", source.Name);
            throw;
        }
    }

    private async Task ProcessOneAsync(DiagnosticRecord record, CancellationToken ct)
    {
        var writer = _rotator.CurrentWriter;
        if (writer is null)
        {
            _state.IncrementDropped();
            return;
        }

        try
        {
            switch (record)
            {
                case EventRecord ev:
                    await writer.AppendEventAsync(ev, ct);
                    _broadcaster.Publish(ev);
                    break;
                case StateSampleRecord ss when ss.Rate == StateSampleRate.Slow:
                    await writer.AppendStateAsync(ss, ct);
                    break;
                case StateSampleRecord ss when ss.Rate == StateSampleRate.Fast:
                    await writer.AppendFastStateAsync(ss, ct);
                    break;
            }
            _rotator.NotifyRecordWritten(record);
            _state.IncrementIngested();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write record from {Publisher} on topic {Topic}",
                record.PublisherNode, record.Topic);
            _state.IncrementDropped();
            // Don't propagate — keep the pipeline running
        }
    }
}
