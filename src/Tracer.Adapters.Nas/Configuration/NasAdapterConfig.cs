namespace Tracer.Adapters.Nas.Configuration;

/// <summary>Configuration for the NAS storage reader adapter.</summary>
public sealed class NasAdapterConfig
{
    /// <summary>UNC root of the NAS, e.g. <c>\\\\nas-server\\tracer</c>.</summary>
    public required string NasRoot { get; init; }

    /// <summary>When true, zip files are copied to a local temp directory before reading.</summary>
    public bool PreferLocalStaging { get; init; } = false;

    public int FileOperationTimeoutSeconds { get; init; } = 30;
    public int RetryOnTransientError { get; init; } = 3;
    public int RetryBaseDelaySeconds { get; init; } = 2;
    public int CircuitBreakerThreshold { get; init; } = 5;
    public int CircuitBreakerResetIntervalSeconds { get; init; } = 60;
}
