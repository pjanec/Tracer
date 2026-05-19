namespace Tracer.Core.Identity;

/// <summary>
/// Identifies a single diagnostic event within a trace.
/// </summary>
public readonly record struct EventId(ulong Value)
{
    /// <summary>The null/absent event identifier.</summary>
    public static EventId None => new(0);

    /// <summary>Returns true if this is the null identifier.</summary>
    public bool IsNone => Value == 0;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("X16");
}
