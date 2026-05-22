namespace Tracer.Adapters.Sync.Configuration;

/// <summary>Configuration for the sync system upload adapter.</summary>
public sealed class SyncAdapterConfig
{
    public required string SyncMasterBaseUrl { get; init; }
    public int RequestTimeoutSeconds { get; init; } = 30;
    public int RetryAttempts { get; init; } = 3;
    public int RetryBaseDelaySeconds { get; init; } = 2;
    public int RetryMaxDelaySeconds { get; init; } = 60;
}
