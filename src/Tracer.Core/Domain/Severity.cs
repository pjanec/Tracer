namespace Tracer.Core.Domain;

/// <summary>
/// Diagnostic severity level for events.
/// </summary>
public enum Severity
{
    /// <summary>Informational events — normal operation.</summary>
    Info,

    /// <summary>Warning events — anomalous but non-critical.</summary>
    Warning,

    /// <summary>Error events — critical failures or unexpected conditions.</summary>
    Error
}
