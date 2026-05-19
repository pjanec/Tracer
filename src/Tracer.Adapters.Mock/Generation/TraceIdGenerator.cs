using Tracer.Core.Identity;

namespace Tracer.Adapters.Mock.Generation;

/// <summary>
/// Generates deterministic trace IDs and monotonically-increasing event IDs.
/// Both derive from a single seeded <see cref="Random"/> — same seed produces identical sequences.
/// </summary>
public sealed class TraceIdGenerator
{
    private readonly Random _random;
    private ulong _nextEventId = 1;

    public TraceIdGenerator(Random seededRandom)
    {
        ArgumentNullException.ThrowIfNull(seededRandom);
        _random = seededRandom;
    }

    /// <summary>
    /// Returns a new non-zero <see cref="TraceId"/>. Retries the byte-generation loop until
    /// a non-zero value is produced (extremely rare, but handles the edge case).
    /// </summary>
    public TraceId NewTrace()
    {
        ulong v;
        do
        {
            var bytes = new byte[8];
            _random.NextBytes(bytes);
            v = BitConverter.ToUInt64(bytes, 0);
        }
        while (v == 0);
        return new TraceId(v);
    }

    /// <summary>
    /// Returns the next <see cref="EventId"/>, starting from 1 and incrementing by 1.
    /// Never returns <see cref="EventId.None"/>.
    /// </summary>
    public EventId NewEvent() => new EventId(_nextEventId++);
}
