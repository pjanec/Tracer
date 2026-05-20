using Microsoft.Extensions.Logging;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Records;

namespace Tracer.Agent.Ingestion;

public sealed class IngestionPipeline
{
    private readonly IAgentTransport _transport;
    private readonly BackpressureMonitor _backpressure;
    private readonly DropPolicy _dropPolicy;
    private readonly RecordRouter _router;
    private readonly IIntervalContext _context;
    private readonly ILogger<IngestionPipeline> _logger;

    public IngestionPipeline(
        IAgentTransport transport,
        BackpressureMonitor backpressure,
        DropPolicy dropPolicy,
        RecordRouter router,
        IIntervalContext context,
        ILogger<IngestionPipeline> logger)
    {
        _transport = transport;
        _backpressure = backpressure;
        _dropPolicy = dropPolicy;
        _router = router;
        _context = context;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await foreach (var record in _transport.ReadAsync(ct).WithCancellation(ct))
        {
            if (_context.CurrentWriter is null)
            {
                _context.NotifyCaptureGap(new CaptureGap
                {
                    StartUtc = record.PublishWallclock,
                    EndUtc = record.ReceiveWallclock,
                    Reason = CaptureGapReason.TransportDisconnected,
                    DroppedRecordCount = 1,
                });
                continue;
            }

            var level = _backpressure.Evaluate();
            if (_dropPolicy.ShouldDrop(record, level, out var gapReason))
            {
                _context.NotifyCaptureGap(new CaptureGap
                {
                    StartUtc = record.PublishWallclock,
                    EndUtc = record.ReceiveWallclock,
                    Reason = gapReason,
                    DroppedRecordCount = 1,
                });
                continue;
            }

            try
            {
                await _router.RouteAsync(record, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to route record seq={Seq}", record.SequenceNumber);
                _context.NotifyCaptureGap(new CaptureGap
                {
                    StartUtc = record.PublishWallclock,
                    EndUtc = record.ReceiveWallclock,
                    Reason = CaptureGapReason.UnrecoveredCrashGap,
                    DroppedRecordCount = 1,
                    Detail = ex.Message,
                });
            }
        }
    }
}
