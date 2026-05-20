using Tracer.Core.Time;
using Tracer.WebApi.Lifecycle;

namespace Tracer.Observer.Lifecycle;

public sealed class ObserverStateReporter : ILiveStatusProvider
{
    private long _ingestedTotal;
    private long _droppedTotal;
    private readonly RollingCounter _ingestedLastMinute;
    private DateTimeOffset _lastEventAt;
    private readonly object _lastEventLock = new();

    public ObserverStateReporter(IClock? clock = null)
    {
        _ingestedLastMinute = new RollingCounter(TimeSpan.FromMinutes(1), clock);
    }

    public void IncrementIngested()
    {
        Interlocked.Increment(ref _ingestedTotal);
        _ingestedLastMinute.Increment();
        lock (_lastEventLock) { _lastEventAt = DateTimeOffset.UtcNow; }
    }

    public void IncrementDropped()
    {
        Interlocked.Increment(ref _droppedTotal);
    }

    // ILiveStatusProvider implementation
    long ILiveStatusProvider.IngestedTotal => Interlocked.Read(ref _ingestedTotal);
    long ILiveStatusProvider.DroppedTotal => Interlocked.Read(ref _droppedTotal);
    DateTimeOffset? ILiveStatusProvider.LastEventUtc
    {
        get { lock (_lastEventLock) { return _lastEventAt == default ? null : _lastEventAt; } }
    }

    public ObserverStateSnapshot Snapshot()
    {
        DateTimeOffset lastEvent;
        lock (_lastEventLock) { lastEvent = _lastEventAt; }
        return new ObserverStateSnapshot
        {
            IngestedTotal = Interlocked.Read(ref _ingestedTotal),
            DroppedTotal = Interlocked.Read(ref _droppedTotal),
            IngestedLastMinute = _ingestedLastMinute.Count,
            LastEventUtc = lastEvent == default ? null : lastEvent
        };
    }
}

public sealed record ObserverStateSnapshot
{
    public required long IngestedTotal { get; init; }
    public required long DroppedTotal { get; init; }
    public required long IngestedLastMinute { get; init; }
    public DateTimeOffset? LastEventUtc { get; init; }
}

/// <summary>
/// Counter of increments within a sliding window.
/// Bucketed implementation (one bucket per second); precision is one second.
/// Accepts an optional IClock for test-time control.
/// </summary>
internal sealed class RollingCounter
{
    private readonly long[] _buckets;
    private readonly object _lock = new();
    private long _lastBucketSecond;
    private readonly Func<long> _nowSeconds;

    public RollingCounter(TimeSpan window, IClock? clock = null)
    {
        _buckets = new long[(int)window.TotalSeconds + 1];
        _nowSeconds = clock is null
            ? static () => DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            : () => clock.Now.ToDateTimeOffset().ToUnixTimeSeconds();
        _lastBucketSecond = _nowSeconds();
    }

    public void Increment()
    {
        var nowSec = _nowSeconds();
        lock (_lock)
        {
            AdvanceTo(nowSec);
            _buckets[nowSec % _buckets.Length]++;
        }
    }

    public long Count
    {
        get
        {
            var nowSec = _nowSeconds();
            lock (_lock)
            {
                AdvanceTo(nowSec);
                long sum = 0;
                for (int i = 0; i < _buckets.Length; i++) sum += _buckets[i];
                return sum;
            }
        }
    }

    private void AdvanceTo(long nowSec)
    {
        var gap = nowSec - _lastBucketSecond;
        if (gap <= 0) return;
        if (gap >= _buckets.Length)
        {
            Array.Clear(_buckets, 0, _buckets.Length);
        }
        else
        {
            for (long s = _lastBucketSecond + 1; s <= nowSec; s++)
                _buckets[s % _buckets.Length] = 0;
        }
        _lastBucketSecond = nowSec;
    }
}
