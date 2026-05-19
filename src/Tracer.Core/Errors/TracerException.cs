namespace Tracer.Core.Errors;

/// <summary>
/// Base exception for all Tracer domain errors.
/// </summary>
public class TracerException : Exception
{
    /// <summary>Constructs a <see cref="TracerException"/> with a message.</summary>
    public TracerException(string message) : base(message) { }

    /// <summary>Constructs a <see cref="TracerException"/> with a message and inner exception.</summary>
    public TracerException(string message, Exception inner) : base(message, inner) { }
}
