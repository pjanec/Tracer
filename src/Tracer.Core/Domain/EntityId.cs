namespace Tracer.Core.Domain;

/// <summary>
/// Identifies a simulation entity (vehicle, aircraft, etc.).
/// </summary>
public readonly record struct EntityId
{
    /// <summary>The string value of this entity identifier.</summary>
    public string Value { get; }

    /// <summary>
    /// Constructs an <see cref="EntityId"/>.
    /// </summary>
    /// <param name="value">The entity identifier. Must not be null, empty, or whitespace.</param>
    public EntityId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("EntityId cannot be empty or whitespace.", nameof(value));
        Value = value;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
