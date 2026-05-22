namespace Tracer.WebApi.Util;

/// <summary>
/// Reservoir sampling (Algorithm R) for quantile estimation.
/// Thread-safety: NOT thread-safe; use externally synchronized.
/// </summary>
public sealed class QuantileSink
{
    private readonly double[] _reservoir;
    private readonly int _reservoirSize;
    private readonly Random _rng;
    private long _count;
    private bool _sorted;

    public QuantileSink(int reservoirSize = 10_000)
    {
        _reservoirSize = reservoirSize;
        _reservoir = new double[reservoirSize];
        _rng = new Random();
    }

    public long Count => _count;

    public void Add(double value)
    {
        _count++;
        _sorted = false;

        if (_count <= _reservoirSize)
        {
            _reservoir[_count - 1] = value;
        }
        else
        {
            var j = (long)(_rng.NextDouble() * _count);
            if (j < _reservoirSize)
                _reservoir[j] = value;
        }
    }

    /// <summary>
    /// Returns the quantile <paramref name="q"/> (0..1) from the reservoir.
    /// Returns <see cref="double.NaN"/> if the sink is empty.
    /// </summary>
    public double GetQuantile(double q)
    {
        if (_count == 0) return double.NaN;

        var filled = (int)Math.Min(_count, _reservoirSize);
        var slice = _reservoir[..filled];

        if (!_sorted)
        {
            Array.Sort(slice);
            _sorted = true;
        }

        var idx = (int)Math.Round(q * (filled - 1));
        idx = Math.Clamp(idx, 0, filled - 1);
        return slice[idx];
    }
}
