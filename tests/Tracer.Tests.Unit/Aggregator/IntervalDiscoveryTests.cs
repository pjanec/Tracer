using FluentAssertions;
using Tracer.Aggregator.Discovery;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Aggregator;

public class IntervalDiscoveryTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IntervalDescriptor MakeInterval(DateTimeOffset start, DateTimeOffset end)
        => new(IntervalTimestamp.FromUtc(start), start, end);

    private static TimeRange MakeRange(DateTimeOffset start, DateTimeOffset end)
        => new(WallclockTime.FromDateTimeOffset(start), WallclockTime.FromDateTimeOffset(end));

    private static readonly DateTimeOffset _base = new(2026, 5, 19, 14, 0, 0, TimeSpan.Zero);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FindOverlappingAsync_NoFilter_ReturnsAllMatchingIntervals()
    {
        // Intervals:
        //   node-a: [14:00, 14:05)  → overlaps range [13:58, 14:03)
        //   node-b: [14:06, 14:11)  → does NOT overlap
        var reader = new FakeStorageReader(new Dictionary<string, IReadOnlyList<IntervalDescriptor>>
        {
            ["node-a"] = new[] { MakeInterval(_base, _base.AddMinutes(5)) },
            ["node-b"] = new[] { MakeInterval(_base.AddMinutes(6), _base.AddMinutes(11)) },
        });

        var range = MakeRange(_base.AddMinutes(-2), _base.AddMinutes(3));
        var result = await IntervalDiscovery.FindOverlappingAsync(reader, range, nodeFilter: null);

        result.Count.Should().Be(1);
        result.Intervals[0].NodeId.Should().Be("node-a");
    }

    [Fact]
    public async Task FindOverlappingAsync_WithNodeFilter_ReturnsOnlyFilteredNodes()
    {
        var reader = new FakeStorageReader(new Dictionary<string, IReadOnlyList<IntervalDescriptor>>
        {
            ["node-a"] = new[] { MakeInterval(_base, _base.AddMinutes(5)) },
            ["node-b"] = new[] { MakeInterval(_base, _base.AddMinutes(5)) },
            ["node-c"] = new[] { MakeInterval(_base, _base.AddMinutes(5)) },
        });

        var range = MakeRange(_base.AddMinutes(-1), _base.AddMinutes(3));
        var result = await IntervalDiscovery.FindOverlappingAsync(
            reader, range, nodeFilter: new[] { "node-a", "node-c" });

        result.Count.Should().Be(2);
        result.Intervals.Select(i => i.NodeId).Should().BeEquivalentTo(new[] { "node-a", "node-c" });
    }

    [Fact]
    public async Task FindOverlappingAsync_NodeFilterIsCaseInsensitive()
    {
        var reader = new FakeStorageReader(new Dictionary<string, IReadOnlyList<IntervalDescriptor>>
        {
            ["Node-A"] = new[] { MakeInterval(_base, _base.AddMinutes(5)) },
        });

        var range = MakeRange(_base.AddMinutes(-1), _base.AddMinutes(3));
        var result = await IntervalDiscovery.FindOverlappingAsync(
            reader, range, nodeFilter: new[] { "node-a" }); // lowercase

        result.Count.Should().Be(1);
    }

    [Fact]
    public async Task FindOverlappingAsync_NoOverlappingIntervals_ReturnsEmpty()
    {
        var reader = new FakeStorageReader(new Dictionary<string, IReadOnlyList<IntervalDescriptor>>
        {
            ["node-a"] = new[] { MakeInterval(_base.AddHours(2), _base.AddHours(3)) },
        });

        var range = MakeRange(_base, _base.AddHours(1));
        var result = await IntervalDiscovery.FindOverlappingAsync(reader, range, nodeFilter: null);

        result.Count.Should().Be(0);
    }

    [Fact]
    public async Task FindOverlappingAsync_IntervalEndEqualsRangeStart_IsExcluded()
    {
        // Interval [14:00, 14:05) — end exactly equals range start → no overlap
        var reader = new FakeStorageReader(new Dictionary<string, IReadOnlyList<IntervalDescriptor>>
        {
            ["node-a"] = new[] { MakeInterval(_base, _base.AddMinutes(5)) },
        });

        // Range starts at 14:05 (exactly when interval ends)
        var range = MakeRange(_base.AddMinutes(5), _base.AddMinutes(10));
        var result = await IntervalDiscovery.FindOverlappingAsync(reader, range, nodeFilter: null);

        result.Count.Should().Be(0);
    }

    // ── Fake storage reader ────────────────────────────────────────────────────

    private sealed class FakeStorageReader : ITelemetryStorageReader
    {
        private readonly Dictionary<string, IReadOnlyList<IntervalDescriptor>> _data;
        public FakeStorageReader(Dictionary<string, IReadOnlyList<IntervalDescriptor>> data) => _data = data;

        public Task<IReadOnlyList<string>> ListNodesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(_data.Keys.ToArray());

        public Task<IReadOnlyList<IntervalDescriptor>> ListIntervalsAsync(string nodeId, CancellationToken ct = default)
            => Task.FromResult(_data.TryGetValue(nodeId, out var ivs) ? ivs : Array.Empty<IntervalDescriptor>() as IReadOnlyList<IntervalDescriptor>);

        public Task<IntervalManifest?> ReadIntervalManifestAsync(string nodeId, IntervalDescriptor descriptor, CancellationToken ct = default)
            => Task.FromResult<IntervalManifest?>(null);

        public string GetIntervalZipPath(string nodeId, IntervalDescriptor descriptor)
            => $"/fake/{nodeId}/{descriptor.Timestamp.Value}.zip";
    }
}
