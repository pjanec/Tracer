using Microsoft.Extensions.Logging;
using Tracer.Adapters.Sync.Configuration;
using Tracer.Core.Abstractions;

namespace Tracer.Adapters.Sync;

/// <summary>
/// Production <see cref="ITelemetryUploadService"/> that registers upload intents
/// with the sync system master over REST.
/// Per <c>sync_addendum_telemetry.md §A4</c>.
/// </summary>
public sealed class SyncSystemUploadService : ITelemetryUploadService
{
    private readonly SyncMasterRestClient _client;
    private readonly SyncAdapterConfig _config;
    private readonly ILogger<SyncSystemUploadService> _logger;

    public SyncSystemUploadService(
        SyncMasterRestClient client,
        SyncAdapterConfig config,
        ILogger<SyncSystemUploadService> logger)
    {
        _client = client;
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<UploadIntentId> RequestUploadAsync(UploadRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nodeId = request.NodeId.Value;
        var intervalTimestamp = request.Interval.Value;

        var intentRequest = new UploadIntentRequest
        {
            NodeId = nodeId,
            IntervalTimestamp = intervalTimestamp,
            IntervalStartUtc = request.IntervalStartUtc.ToDateTimeOffset().ToString("O"),
            IntervalEndUtc = request.IntervalEndUtc.ToDateTimeOffset().ToString("O"),
            Files = request.Files
                .Select(f => new TelemetryFileEntry
                {
                    Name = System.IO.Path.GetFileName(f.Path),
                    SizeBytes = f.SizeBytes,
                })
                .ToArray(),
        };

        await RetryAsync(
            () => _client.RegisterUploadIntentAsync(intentRequest, ct),
            "RegisterUploadIntent",
            ct).ConfigureAwait(false);

        // Encode nodeId + intervalTimestamp in the intentId so GetStatusAsync can call the REST API.
        _logger.LogInformation(
            "Registered upload intent for {NodeId}/{Interval}", nodeId, intervalTimestamp);

        return new UploadIntentId($"{nodeId}|{intervalTimestamp}");
    }

    /// <inheritdoc/>
    public async Task<UploadStatus> GetStatusAsync(UploadIntentId intentId, CancellationToken ct)
    {
        var parts = intentId.Value.Split('|', 2);
        if (parts.Length != 2) return UploadStatus.Unknown;

        var statusStr = await _client.GetIntentStatusAsync(parts[0], parts[1], ct).ConfigureAwait(false);
        return MapStatus(statusStr);
    }

    /// <summary>
    /// Polls with exponential backoff until the upload completes or fails.
    /// Not part of <see cref="ITelemetryUploadService"/>; available on the concrete type.
    /// </summary>
    public async Task<UploadStatus> WaitForCompletionAsync(UploadIntentId intentId, CancellationToken ct)
    {
        var delaySeconds = _config.RetryBaseDelaySeconds;
        while (!ct.IsCancellationRequested)
        {
            var status = await GetStatusAsync(intentId, ct).ConfigureAwait(false);
            if (status is UploadStatus.Complete or UploadStatus.Failed)
                return status;

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct).ConfigureAwait(false);
            delaySeconds = Math.Min(delaySeconds * 2, _config.RetryMaxDelaySeconds);
        }

        ct.ThrowIfCancellationRequested();
        return UploadStatus.Unknown;
    }

    private async Task<T> RetryAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        CancellationToken ct)
    {
        var delaySeconds = _config.RetryBaseDelaySeconds;

        for (var attempt = 1; attempt <= _config.RetryAttempts; attempt++)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt < _config.RetryAttempts && IsTransient(ex))
            {
                _logger.LogWarning(ex,
                    "{Op} attempt {Attempt}/{Max} failed; retrying in {Delay}s",
                    operationName, attempt, _config.RetryAttempts, delaySeconds);

                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct).ConfigureAwait(false);
                delaySeconds = Math.Min(delaySeconds * 2, _config.RetryMaxDelaySeconds);
            }
        }

        // Final attempt (retries exhausted)
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "{Op} exhausted {Max} retries", operationName, _config.RetryAttempts);
            throw;
        }
    }

    private static bool IsTransient(HttpRequestException ex) =>
        ex.StatusCode is null
        or System.Net.HttpStatusCode.InternalServerError
        or System.Net.HttpStatusCode.BadGateway
        or System.Net.HttpStatusCode.ServiceUnavailable
        or System.Net.HttpStatusCode.GatewayTimeout;

    private static UploadStatus MapStatus(string status) => status switch
    {
        "Completed" or "Complete" => UploadStatus.Complete,
        "Failed" => UploadStatus.Failed,
        "InProgress" or "Uploading" => UploadStatus.InProgress,
        "Pending" => UploadStatus.Pending,
        _ => UploadStatus.Unknown,
    };
}
