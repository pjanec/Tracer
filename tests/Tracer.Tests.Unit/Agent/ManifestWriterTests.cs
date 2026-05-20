using FluentAssertions;
using System.Text.Json;
using Tracer.Agent.Storage;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Agent;

public sealed class ManifestWriterTests
{
    private static IntervalManifest MakeManifest() => new()
    {
        IntervalStart = new IntervalTimestamp("20260519T140000Z"),
        IntervalEnd = new IntervalTimestamp("20260519T150000Z"),
        NodeId = new AgentId("test-node"),
        TracerVersion = "1.0.0-test",
        SchemaVersion = 1,
        EventCount = 42,
        SlowStateCount = 7,
        FastStateTopics = ["topic.transforms"],
        CaptureGaps = [],
        SessionMarkers = [],
        FinalizedAt = WallclockTime.FromDateTimeOffset(
            new DateTimeOffset(2026, 5, 19, 15, 0, 0, TimeSpan.Zero)),
        FinalizationReason = ManifestFinalizationReason.ScheduledRotation,
    };

    [Fact]
    public async Task ManifestWriter_WriteAndDeserialize_RoundTrips()
    {
        var path = Path.GetTempFileName();
        try
        {
            var original = MakeManifest();
            await ManifestWriter.WriteAsync(path, original, CancellationToken.None);

            var loaded = await ManifestWriter.ReadAsync(path, CancellationToken.None);

            loaded.Should().NotBeNull();
            loaded!.IntervalStart.Should().Be(original.IntervalStart);
            loaded.IntervalEnd.Should().Be(original.IntervalEnd);
            loaded.NodeId.Should().Be(original.NodeId);
            loaded.EventCount.Should().Be(original.EventCount);
            loaded.SlowStateCount.Should().Be(original.SlowStateCount);
            loaded.FinalizationReason.Should().Be(original.FinalizationReason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ManifestWriter_IntervalTimestamp_SerializesAsString()
    {
        var path = Path.GetTempFileName();
        try
        {
            await ManifestWriter.WriteAsync(path, MakeManifest(), CancellationToken.None);
            var json = await File.ReadAllTextAsync(path);

            // IntervalTimestamp must appear as a bare string, not a nested object
            json.Should().MatchRegex(@"""interval_start""\s*:\s*""20260519T140000Z""");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ManifestWriter_CaptureGaps_IncludedInJson()
    {
        var path = Path.GetTempFileName();
        try
        {
            var manifest = MakeManifest() with
            {
                CaptureGaps = new[]
                {
                    new CaptureGap
                    {
                        StartUtc = WallclockTime.Zero,
                        EndUtc = WallclockTime.Zero,
                        Reason = CaptureGapReason.BackpressureFastStateDropped,
                        DroppedRecordCount = 3,
                    },
                },
            };

            await ManifestWriter.WriteAsync(path, manifest, CancellationToken.None);
            var json = await File.ReadAllTextAsync(path);

            json.Should().Contain("capture_gaps");
            json.Should().Contain("dropped_record_count");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ManifestWriter_WallclockTimes_SerializeAsIso8601()
    {
        var path = Path.GetTempFileName();
        try
        {
            var manifest = MakeManifest() with
            {
                FinalizedAt = WallclockTime.FromDateTimeOffset(
                    new DateTimeOffset(2026, 5, 19, 15, 0, 0, 500, TimeSpan.Zero)),
            };

            await ManifestWriter.WriteAsync(path, manifest, CancellationToken.None);
            var json = await File.ReadAllTextAsync(path);

            // finalized_at must be a string in ISO 8601 form, not a number
            json.Should().MatchRegex(@"""finalized_at""\s*:\s*""2026-05-19");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ManifestWriter_EmptyGapsAndMarkers_SerializesEmptyArrays()
    {
        var path = Path.GetTempFileName();
        try
        {
            var manifest = MakeManifest() with
            {
                CaptureGaps = [],
                SessionMarkers = [],
            };

            await ManifestWriter.WriteAsync(path, manifest, CancellationToken.None);
            var json = await File.ReadAllTextAsync(path);

            // Both collections must appear as [] in JSON
            json.Should().MatchRegex(@"""capture_gaps""\s*:\s*\[\s*\]");
            json.Should().MatchRegex(@"""session_markers""\s*:\s*\[\s*\]");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
