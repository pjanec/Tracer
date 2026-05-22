using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Adapters.Nas;
using Tracer.Adapters.Nas.Configuration;
using Tracer.Core.Domain;
using Xunit;

namespace Tracer.Tests.Unit.Adapters.Nas;

public sealed class NasStorageReaderTests : IDisposable
{
    private readonly string _nasRoot;

    public NasStorageReaderTests()
    {
        _nasRoot = Path.Combine(Path.GetTempPath(), $"tracer-nas-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_nasRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_nasRoot, recursive: true); } catch { /* best-effort */ }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private NasStorageReader BuildReader(bool preferLocalStaging = false) =>
        new(new NasAdapterConfig
        {
            NasRoot = _nasRoot,
            PreferLocalStaging = preferLocalStaging,
        }, NullLogger<NasStorageReader>.Instance);

    /// <summary>Creates a minimal telemetry zip and returns its path.</summary>
    private string CreateIntervalZip(
        string nodeId,
        string intervalTimestamp,
        bool includeReadySentinel = true,
        bool includeManifest = false)
    {
        var nodeDir = Path.Combine(_nasRoot, "telemetry", nodeId);
        Directory.CreateDirectory(nodeDir);
        var zipPath = Path.Combine(nodeDir, $"{intervalTimestamp}.zip");

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        if (includeReadySentinel)
            archive.CreateEntry("_ready");

        if (includeManifest)
        {
            var entry = archive.CreateEntry("manifest.json");
            using var stream = entry.Open();
            using var writer = new Utf8JsonWriter(stream);
            // Minimal manifest JSON — NasStorageReader falls back gracefully if fields are missing.
            writer.WriteStartObject();
            writer.WriteString("interval_start", intervalTimestamp);
            writer.WriteString("interval_end", intervalTimestamp);
            writer.WriteString("node_id", nodeId);
            writer.WriteString("tracer_version", "test");
            writer.WriteNumber("schema_version", 1);
            writer.WriteNumber("event_count", 0);
            writer.WriteNumber("slow_state_count", 0);
            writer.WriteStartArray("fast_state_topics"); writer.WriteEndArray();
            writer.WriteStartArray("capture_gaps"); writer.WriteEndArray();
            writer.WriteStartArray("session_markers"); writer.WriteEndArray();
            writer.WriteString("finalized_at", "2026-05-19T14:00:00Z");
            writer.WriteString("finalization_reason", "GracefulShutdown");
            writer.WriteEndObject();
        }

        return zipPath;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListNodesAsync_NodesExist_ReturnsNodeIds()
    {
        CreateIntervalZip("node-1", "20260519T140000Z");
        CreateIntervalZip("node-2", "20260519T140000Z");

        var reader = BuildReader();
        var nodes = await reader.ListNodesAsync();

        nodes.Should().Contain("node-1");
        nodes.Should().Contain("node-2");
    }

    [Fact]
    public async Task ListNodesAsync_TelemetryRootMissing_ReturnsEmpty()
    {
        var reader = new NasStorageReader(
            new NasAdapterConfig { NasRoot = Path.Combine(_nasRoot, "nonexistent") },
            NullLogger<NasStorageReader>.Instance);

        var nodes = await reader.ListNodesAsync();

        nodes.Should().BeEmpty();
    }

    [Fact]
    public async Task ListIntervalsAsync_ReadyZip_ReturnsDescriptor()
    {
        CreateIntervalZip("node-1", "20260519T140000Z", includeReadySentinel: true);

        var reader = BuildReader();
        var intervals = await reader.ListIntervalsAsync("node-1");

        intervals.Should().HaveCount(1);
        intervals[0].Timestamp.Value.Should().Be("20260519T140000Z");
    }

    [Fact]
    public async Task ListIntervalsAsync_MissingReadySentinel_SkipsInterval()
    {
        CreateIntervalZip("node-1", "20260519T140000Z", includeReadySentinel: false);

        var reader = BuildReader();
        var intervals = await reader.ListIntervalsAsync("node-1");

        intervals.Should().BeEmpty();
    }

    [Fact]
    public async Task ListIntervalsAsync_NodeDirectoryMissing_ReturnsEmpty()
    {
        var reader = BuildReader();
        var intervals = await reader.ListIntervalsAsync("nonexistent-node");

        intervals.Should().BeEmpty();
    }

    [Fact]
    public async Task ListIntervalsAsync_MultipleNodes_EachListsOwn()
    {
        CreateIntervalZip("node-a", "20260519T140000Z");
        CreateIntervalZip("node-a", "20260519T150000Z");
        CreateIntervalZip("node-b", "20260519T140000Z");

        var reader = BuildReader();

        var aIntervals = await reader.ListIntervalsAsync("node-a");
        var bIntervals = await reader.ListIntervalsAsync("node-b");

        aIntervals.Should().HaveCount(2);
        bIntervals.Should().HaveCount(1);
    }

    [Fact]
    public async Task StageAsync_NoLocalStaging_LocalPathEqualsZipPath()
    {
        CreateIntervalZip("node-1", "20260519T140000Z");
        var reader = BuildReader(preferLocalStaging: false);
        var intervals = await reader.ListIntervalsAsync("node-1");
        var descriptor = intervals[0];

        using var staged = await reader.StageAsync("node-1", descriptor);

        staged.LocalPath.Should().EndWith(".zip");
        File.Exists(staged.LocalPath).Should().BeTrue();
    }

    [Fact]
    public async Task StageAsync_WithLocalStaging_CopiesFileAndCleansUpOnDispose()
    {
        CreateIntervalZip("node-1", "20260519T140000Z");
        var reader = BuildReader(preferLocalStaging: true);
        var intervals = await reader.ListIntervalsAsync("node-1");
        var descriptor = intervals[0];

        string tempPath;
        {
            using var staged = await reader.StageAsync("node-1", descriptor);
            tempPath = staged.LocalPath;
            File.Exists(tempPath).Should().BeTrue("staged copy should exist during use");
        }

        // After dispose the temp dir should be removed.
        Directory.Exists(Path.GetDirectoryName(tempPath)!).Should().BeFalse();
    }

    [Fact]
    public void GetIntervalZipPath_ReturnsCorrectPath()
    {
        var reader = BuildReader();
        var descriptor = new IntervalDescriptor(
            new IntervalTimestamp("20260519T140000Z"),
            new DateTimeOffset(2026, 5, 19, 14, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 19, 15, 0, 0, TimeSpan.Zero));

        var result = reader.GetIntervalZipPath("node-1", descriptor);

        result.Should().EndWith("20260519T140000Z.zip");
        result.Should().Contain("node-1");
    }
}
