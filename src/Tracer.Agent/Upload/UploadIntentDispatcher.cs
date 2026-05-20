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

    public UploadIntentDispatcher(
        ITelemetryUploadService uploadService,
        ILogger<UploadIntentDispatcher> logger)
    {
        _uploadService = uploadService;
        _logger = logger;
    }

    public async Task DispatchAsync(
        IntervalDirectory directory,
        IntervalManifest manifest,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(manifest);
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
}
