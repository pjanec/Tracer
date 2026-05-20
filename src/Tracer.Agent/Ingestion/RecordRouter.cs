using Microsoft.Extensions.Logging;
using Tracer.Core.Records;

namespace Tracer.Agent.Ingestion;

public sealed class RecordRouter
{
    private readonly IIntervalContext _context;
    private readonly ILogger<RecordRouter> _logger;

    public RecordRouter(IIntervalContext context, ILogger<RecordRouter> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task RouteAsync(DiagnosticRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        var writer = _context.CurrentWriter;
        if (writer is null)
            return;

        switch (record)
        {
            case EventRecord evt:
                await writer.AppendEventAsync(evt, ct);
                break;
            case StateSampleRecord { Rate: StateSampleRate.Slow } slow:
                await writer.AppendStateAsync(slow, ct);
                break;
            case StateSampleRecord { Rate: StateSampleRate.Fast } fast:
                await writer.AppendFastStateAsync(fast, ct);
                break;
            default:
                _logger.LogWarning("Unknown record type {Type} – skipped", record.GetType().Name);
                return;
        }

        _context.NotifyRecordWritten(record);
    }
}
