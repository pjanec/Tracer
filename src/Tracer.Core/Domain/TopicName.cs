namespace Tracer.Core.Domain;

/// <summary>
/// Identifies a DDS topic by name.
/// </summary>
public readonly record struct TopicName
{
    /// <summary>The string value of this topic name.</summary>
    public string Value { get; }

    /// <summary>
    /// Constructs a <see cref="TopicName"/>.
    /// </summary>
    /// <param name="value">The topic name. Must not be null, empty, or whitespace.</param>
    public TopicName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("TopicName cannot be empty or whitespace.", nameof(value));
        Value = value;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
