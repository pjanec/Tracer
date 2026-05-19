namespace Tracer.Core.Identity;

/// <summary>
/// Identifies a distributed trace across multiple nodes and events.
/// </summary>
public readonly record struct TraceId(ulong Value)
{
    /// <summary>The null/absent trace identifier.</summary>
    public static TraceId None => new(0);

    /// <summary>Returns true if this is the null identifier.</summary>
    public bool IsNone => Value == 0;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("X16");
}
