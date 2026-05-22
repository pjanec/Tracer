using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Tracer.Adapters.Sync;

/// <summary>
/// Thin <see cref="HttpClient"/> wrapper for the sync system's Telemetry REST API.
/// Per <c>sync_addendum_telemetry.md §A4</c>.
/// </summary>
public sealed class SyncMasterRestClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SyncMasterRestClient> _logger;

    public SyncMasterRestClient(HttpClient httpClient, ILogger<SyncMasterRestClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/telemetry — declares a per-node interval ready for upload.
    /// Returns the <c>intentId</c> from the sync master.
    /// </summary>
    public async Task<string> RegisterUploadIntentAsync(
        UploadIntentRequest request,
        CancellationToken ct)
    {
        using var response = await _httpClient.PostAsJsonAsync("/api/telemetry", request, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content
            .ReadFromJsonAsync<UploadIntentResponse>(cancellationToken: ct)
            .ConfigureAwait(false);
        return result?.IntentId
            ?? throw new InvalidOperationException("Sync master response did not contain intentId.");
    }

    /// <summary>
    /// GET /api/telemetry/{nodeId}/{intervalTimestamp} — returns the upload status string.
    /// </summary>
    public async Task<string> GetIntentStatusAsync(
        string nodeId,
        string intervalTimestamp,
        CancellationToken ct)
    {
        var url = $"/api/telemetry/{Uri.EscapeDataString(nodeId)}/{Uri.EscapeDataString(intervalTimestamp)}";
        using var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content
            .ReadFromJsonAsync<UploadStatusResponse>(cancellationToken: ct)
            .ConfigureAwait(false);
        return result?.Status ?? "Unknown";
    }
}

/// <summary>Request body for POST /api/telemetry.</summary>
public sealed record UploadIntentRequest
{
    public required string NodeId { get; init; }
    public required string IntervalTimestamp { get; init; }
    public required string IntervalStartUtc { get; init; }
    public required string IntervalEndUtc { get; init; }
    public required IReadOnlyList<TelemetryFileEntry> Files { get; init; }
}

/// <summary>A single file entry in an upload intent request.</summary>
public sealed record TelemetryFileEntry
{
    public required string Name { get; init; }
    public required long SizeBytes { get; init; }
}

internal sealed record UploadIntentResponse
{
    public string? IntentId { get; init; }
}

internal sealed record UploadStatusResponse
{
    public string? Status { get; init; }
    public string? ErrorMessage { get; init; }
}
