using Tracer.Agent.Configuration;
using Tracer.Agent.Lifecycle;
using Tracer.Agent.Storage;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Time;
using Microsoft.Extensions.Logging;

namespace Tracer.Agent.Upload;

public sealed class UploadIntentDispatcher
{
    private readonly ITelemetryUploadService _uploadService;
    private readonly ILogger<UploadIntentDispatcher> _logger;
    private readonly int _backlogWarningThreshold;
    private int _pendingCount;

    public int PendingCount => _pendingCount;

    public UploadIntentDispatcher(
        ITelemetryUploadService uploadService,
        ILogger<UploadIntentDispatcher> logger,
        AgentConfig? config = null)
    {
        _uploadService = uploadService;
        _logger = logger;
        _backlogWarningThreshold = config?.BacklogWarningThreshold ?? 3;
    }

    public async Task DispatchAsync(
        IntervalDirectory directory,
        IntervalManifest manifest,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(manifest);

        Interlocked.Increment(ref _pendingCount);
        try
        {
            if (_pendingCount > _backlogWarningThreshold)
            {
                _logger.LogWarning(
                    "Upload backlog exceeds threshold: PendingCount={PendingCount}, Threshold={Threshold}",
                    _pendingCount, _backlogWarningThreshold);
            }

            var files = directory.EnumerateFiles();
            var request = new UploadRequest
            {
                NodeId = manifest.NodeId,
                Interval = manifest.IntervalStart,
                IntervalStartUtc = WallclockTime.FromDateTimeOffset(manifest.IntervalStart.ToDateTimeOffset()),
                IntervalEndUtc = WallclockTime.FromDateTimeOffset(manifest.IntervalEnd.ToDateTimeOffset()),
                Files = files,
            };

            var intentId = await _uploadService.RequestUploadAsync(request, ct);
            _logger.LogInformation(
                "Upload intent {IntentId} dispatched for interval {Interval}",
                intentId.Value, manifest.IntervalStart.Value);
        }
        finally
        {
            Interlocked.Decrement(ref _pendingCount);
        }
    }

    public async Task WaitForPendingAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (_pendingCount > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(50).ConfigureAwait(false);
    }
}
