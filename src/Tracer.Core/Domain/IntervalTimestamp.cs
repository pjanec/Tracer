using System.Globalization;

namespace Tracer.Core.Domain;

/// <summary>
/// Wall-clock-aligned interval identifier in ISO 8601 basic format: YYYYMMDDTHHMMSSZ.
/// Always UTC. Always wall-clock-aligned (no fractional seconds).
/// </summary>
public readonly record struct IntervalTimestamp
{
    private const string Format = "yyyyMMddTHHmmssZ";
    private const int ExpectedLength = 16;

    /// <summary>The formatted string value, e.g. "20260519T140000Z".</summary>
    public string Value { get; }

    /// <summary>
    /// Constructs an <see cref="IntervalTimestamp"/> from a formatted string.
    /// </summary>
    /// <exception cref="ArgumentException">When the value does not match YYYYMMDDTHHMMSSZ.</exception>
    public IntervalTimestamp(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!IsValid(value))
            throw new ArgumentException(
                $"Invalid interval timestamp: '{value}'. Expected YYYYMMDDTHHMMSSZ.",
                nameof(value));
        Value = value;
    }

    /// <summary>
    /// Creates an <see cref="IntervalTimestamp"/> from a UTC <see cref="DateTimeOffset"/>.
    /// </summary>
    /// <exception cref="ArgumentException">When the offset is not zero (non-UTC).</exception>
    public static IntervalTimestamp FromUtc(DateTimeOffset utc)
    {
        if (utc.Offset != TimeSpan.Zero)
            throw new ArgumentException("IntervalTimestamp must be UTC (Offset must be zero).", nameof(utc));
        return new IntervalTimestamp(utc.ToString(Format, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Converts this timestamp back to a <see cref="DateTimeOffset"/> (UTC).
    /// </summary>
    public DateTimeOffset ToDateTimeOffset()
    {
        return DateTimeOffset.ParseExact(
            Value,
            Format,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal);
    }

    /// <summary>
    /// Attempts to parse a string as an <see cref="IntervalTimestamp"/>.
    /// Returns <c>false</c> for malformed input without throwing.
    /// </summary>
    public static bool TryParse(string? value, out IntervalTimestamp result)
    {
        if (value is not null && IsValid(value))
        {
            result = new IntervalTimestamp(value);
            return true;
        }
        result = default;
        return false;
    }

    private static bool IsValid(string value)
    {
        if (value is null || value.Length != ExpectedLength) return false;
        return DateTimeOffset.TryParseExact(
            value,
            Format,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out _);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
