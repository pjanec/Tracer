namespace Tracer.WebApi.Util;

/// <summary>
/// Logarithmic histogram with power-of-two bucket widths (4 buckets per octave).
/// </summary>
public sealed class HistogramSink
{
    private readonly Dictionary<long, long> _buckets = new();

    public void Add(double valueMs)
    {
        var index = BucketIndex(valueMs);
        _buckets.TryGetValue(index, out var cnt);
        _buckets[index] = cnt + 1;
    }

    public IReadOnlyList<HistogramBucket> GetBuckets()
    {
        return _buckets
            .OrderBy(kv => kv.Key)
            .Select(kv =>
            {
                var (lowMs, highMs) = BucketBounds(kv.Key);
                return new HistogramBucket(kv.Key, lowMs, highMs, kv.Value);
            })
            .ToList();
    }

    public static long BucketIndex(double valueMs)
    {
        var clamped = Math.Max(valueMs, 0.001);
        return (long)Math.Floor(Math.Log2(clamped) * 4);
    }

    public static (double LowMs, double HighMs) BucketBounds(long index)
    {
        var lowMs = Math.Pow(2.0, index / 4.0);
        var highMs = Math.Pow(2.0, (index + 1.0) / 4.0);
        return (lowMs, highMs);
    }
}

public sealed record HistogramBucket(long Index, double LowMs, double HighMs, long Count);
