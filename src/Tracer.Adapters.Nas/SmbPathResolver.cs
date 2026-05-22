namespace Tracer.Adapters.Nas;

/// <summary>
/// Maps logical (nodeId, intervalTimestamp) identifiers to filesystem paths under the NAS root.
/// Validates path components to prevent directory traversal.
/// </summary>
public sealed class SmbPathResolver
{
    private readonly string _nasRoot;

    public SmbPathResolver(string nasRoot)
    {
        ArgumentNullException.ThrowIfNull(nasRoot);
        _nasRoot = nasRoot;
    }

    /// <summary>
    /// Returns the full path to the interval zip:
    /// <c>{NasRoot}\telemetry\{nodeId}\{intervalTimestamp}.zip</c>
    /// </summary>
    public string Resolve(string nodeId, string intervalTimestamp)
    {
        ValidateComponent(nodeId, nameof(nodeId));
        ValidateComponent(intervalTimestamp, nameof(intervalTimestamp));
        return Path.Combine(_nasRoot, "telemetry", nodeId, $"{intervalTimestamp}.zip");
    }

    /// <summary>Returns the node's telemetry directory: <c>{NasRoot}\telemetry\{nodeId}</c>.</summary>
    public string ResolveNodeDir(string nodeId)
    {
        ValidateComponent(nodeId, nameof(nodeId));
        return Path.Combine(_nasRoot, "telemetry", nodeId);
    }

    /// <summary>Returns the telemetry root directory: <c>{NasRoot}\telemetry</c>.</summary>
    public string ResolveTelemetryRoot() => Path.Combine(_nasRoot, "telemetry");

    private static void ValidateComponent(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Path component cannot be empty or whitespace.", paramName);

        if (value.Contains("..") ||
            value.Contains('/') ||
            value.Contains('\\') ||
            value.Contains('\0'))
        {
            throw new ArgumentException(
                $"Path component '{value}' contains directory traversal characters.", paramName);
        }
    }
}
