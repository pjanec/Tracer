namespace Tracer.Core.Errors;

/// <summary>
/// Raised when a storage operation fails.
/// </summary>
public sealed class TracerStorageException : TracerException
{
    /// <summary>Constructs a <see cref="TracerStorageException"/> with a message.</summary>
    public TracerStorageException(string message) : base(message) { }

    /// <summary>Constructs a <see cref="TracerStorageException"/> with a message and inner exception.</summary>
    public TracerStorageException(string message, Exception inner) : base(message, inner) { }
}
