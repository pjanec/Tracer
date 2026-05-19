namespace Tracer.Core.Errors;

/// <summary>
/// Raised when a scenario script encounters an unrecoverable condition.
/// </summary>
public sealed class TracerScenarioException : TracerException
{
    /// <summary>Constructs a <see cref="TracerScenarioException"/> with a message.</summary>
    public TracerScenarioException(string message) : base(message) { }

    /// <summary>Constructs a <see cref="TracerScenarioException"/> with a message and inner exception.</summary>
    public TracerScenarioException(string message, Exception inner) : base(message, inner) { }
}
