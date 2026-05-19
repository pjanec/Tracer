namespace Tracer.Core.Domain;

/// <summary>
/// Marks the beginning or end of a named session.
/// </summary>
public enum SessionMarker
{
    /// <summary>Marks the start of a session.</summary>
    Start,

    /// <summary>Marks the end of a session.</summary>
    End
}
