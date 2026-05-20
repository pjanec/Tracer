using FluentAssertions;
using Tracer.Aggregator.Discovery;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Aggregator;

public class SessionResolverTests
{
    private static readonly DateTimeOffset _base = new(2026, 5, 19, 14, 0, 0, TimeSpan.Zero);
    private const string SessionId = "test-session";

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static IntervalDescriptor MakeDescriptor(DateTimeOffset start, DateTimeOffset end)
        => new(IntervalTimestamp.FromUtc(start), start, end);

    private static IntervalManifest MakeManifest(
        string sessionId,
        bool includeStart,
        bool includeEnd,
        DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null)
    {
        var markers = new List<SessionMarker>();
        if (includeStart)
            markers.Add(new SessionMarker
            {
                SessionId = sessionId,
                Type = SessionMarkerType.Start,
                Wallclock = WallclockTime.FromDateTimeOffset(startTime ?? _base),
            });
        if (includeEnd)
            markers.Add(new SessionMarker
            {
                SessionId = sessionId,
                Type = SessionMarkerType.End,
                Wallclock = WallclockTime.FromDateTimeOffset(endTime ?? _base.AddHours(1)),
            });

        return new IntervalManifest
        {
            IntervalStart = IntervalTimestamp.FromUtc(_base),
            IntervalEnd = IntervalTimestamp.FromUtc(_base.AddHours(1)),
            NodeId = new AgentId("test-node"),
            TracerVersion = "1.0.0",
            SchemaVersion = 1,
            EventCount = 0,
            SlowStateCount = 0,
            FastStateTopics = Array.Empty<string>(),
            CaptureGaps = Array.Empty<CaptureGap>(),
            SessionMarkers = markers,
            FinalizedAt = WallclockTime.FromDateTimeOffset(_base.AddHours(1)),
            FinalizationReason = ManifestFinalizationReason.ScheduledRotation,
        };
    }

    // ── Tests ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_SessionWithStartAndEnd_ReturnsCorrectRange()
    {
        var start = _base;
        var end = _base.AddHours(2);

        var reader = new FakeStorageReader(new Dictionary<string, (IntervalDescriptor, IntervalManifest)[]>
        {
            ["node-a"] = new[]
            {
                (MakeDescriptor(start, end),
                 MakeManifest(SessionId, includeStart: true, includeEnd: true, start, end))
            },
        });

        var result = await SessionResolver.ResolveAsync(reader, SessionId);

        result.Should().NotBeNull();
        result!.StartUtc.ToDateTimeOffset().Should().BeCloseTo(start, TimeSpan.FromMilliseconds(1));
        result.EndUtc.ToDateTimeOffset().Should().BeCloseTo(end, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task ResolveAsync_SessionWithOnlyStart_UsesNowAsEnd()
    {
        var start = _base;

        var reader = new FakeStorageReader(new Dictionary<string, (IntervalDescriptor, IntervalManifest)[]>
        {
            ["node-a"] = new[]
            {
                (MakeDescriptor(start, start.AddHours(1)),
                 MakeManifest(SessionId, includeStart: true, includeEnd: false, start))
            },
        });

        var before = DateTimeOffset.UtcNow;
        var result = await SessionResolver.ResolveAsync(reader, SessionId);
        var after = DateTimeOffset.UtcNow;

        result.Should().NotBeNull();
        result!.StartUtc.ToDateTimeOffset().Should().BeCloseTo(start, TimeSpan.FromMilliseconds(1));
        result.EndUtc.ToDateTimeOffset().Should().BeOnOrAfter(before).And.BeOnOrBefore(after.AddSeconds(5));
    }

    [Fact]
    public async Task ResolveAsync_NonExistentSession_ReturnsNull()
    {
        var reader = new FakeStorageReader(new Dictionary<string, (IntervalDescriptor, IntervalManifest)[]>
        {
            ["node-a"] = new[]
            {
                (MakeDescriptor(_base, _base.AddHours(1)),
                 MakeManifest("OTHER-SESSION", includeStart: true, includeEnd: true))
            },
        });

        var result = await SessionResolver.ResolveAsync(reader, SessionId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_MultipleIntervalsWithMarkers_ReturnsEarliestStartLatestEnd()
    {
        var start1 = _base;
        var end2 = _base.AddHours(3);

        var reader = new FakeStorageReader(new Dictionary<string, (IntervalDescriptor, IntervalManifest)[]>
        {
            ["node-a"] = new[]
            {
                (MakeDescriptor(start1, start1.AddHours(1)),
                 MakeManifest(SessionId, includeStart: true, includeEnd: false, startTime: start1)),
            },
            ["node-b"] = new[]
            {
                (MakeDescriptor(_base.AddMinutes(30), end2),
                 MakeManifest(SessionId, includeStart: false, includeEnd: true, endTime: end2)),
            },
        });

        var result = await SessionResolver.ResolveAsync(reader, SessionId);

        result.Should().NotBeNull();
        result!.StartUtc.ToDateTimeOffset().Should().BeCloseTo(start1, TimeSpan.FromMilliseconds(1));
        result.EndUtc.ToDateTimeOffset().Should().BeCloseTo(end2, TimeSpan.FromMilliseconds(1));
    }

    // ── Fake storage reader ──────────────────────────────────────────────────────

    private sealed class FakeStorageReader : ITelemetryStorageReader
    {
        private readonly Dictionary<string, (IntervalDescriptor, IntervalManifest)[]> _data;
        public FakeStorageReader(Dictionary<string, (IntervalDescriptor, IntervalManifest)[]> data) => _data = data;

        public Task<IReadOnlyList<string>> ListNodesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(_data.Keys.ToArray());

        public Task<IReadOnlyList<IntervalDescriptor>> ListIntervalsAsync(string nodeId, CancellationToken ct = default)
        {
            IReadOnlyList<IntervalDescriptor> result = _data.TryGetValue(nodeId, out var entries)
                ? entries.Select(e => e.Item1).ToArray()
                : Array.Empty<IntervalDescriptor>();
            return Task.FromResult(result);
        }

        public Task<IntervalManifest?> ReadIntervalManifestAsync(
            string nodeId, IntervalDescriptor descriptor, CancellationToken ct = default)
        {
            if (_data.TryGetValue(nodeId, out var entries))
            {
                var match = entries.FirstOrDefault(e => e.Item1.Timestamp == descriptor.Timestamp);
                if (match.Item1 is not null)
                    return Task.FromResult<IntervalManifest?>(match.Item2);
            }
            return Task.FromResult<IntervalManifest?>(null);
        }

        public string GetIntervalZipPath(string nodeId, IntervalDescriptor descriptor)
            => $"/fake/{nodeId}/{descriptor.Timestamp.Value}.zip";
    }
}
