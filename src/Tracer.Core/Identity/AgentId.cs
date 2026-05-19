namespace Tracer.Core.Identity;

/// <summary>
/// Identifies a Tracer agent (node) by a human-readable name.
/// Maximum length is 64 characters.
/// </summary>
public readonly record struct AgentId
{
    private const int MaxLength = 64;

    /// <summary>The string value of this agent identifier.</summary>
    public string Value { get; }

    /// <summary>
    /// Constructs an <see cref="AgentId"/>.
    /// </summary>
    /// <param name="value">The agent name. Must not be null, empty, or whitespace, and must not exceed 64 characters.</param>
    public AgentId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("AgentId cannot be empty or whitespace.", nameof(value));
        if (value.Length > MaxLength)
            throw new ArgumentException($"AgentId max length is {MaxLength} characters.", nameof(value));
        Value = value;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
