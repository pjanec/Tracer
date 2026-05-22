using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Agent.Storage;
using Tracer.Bundle.Format;
using Tracer.Core.Domain;
using Tracer.Storage.DuckDB.MultiInterval;
using Tracer.WebApi.Queries;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class FastStateFileLocatorTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly List<string> _tempDirs = [];

    public FastStateFileLocatorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"fsfl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
                try { Directory.Delete(dir, recursive: true); } catch { }
        }
        if (Directory.Exists(_tempRoot))
            try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    // Creates an interval directory under _tempRoot with a fast_state subdir
    private IntervalDirectory CreateIntervalDir(string name)
    {
        var dir = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(dir);
        var fakeDb = Path.Combine(dir, "events.duckdb");
        File.WriteAllText(fakeDb, "");
        return IntervalDirectory.ForEventsDb(fakeDb);
    }

    private static void CreateParquetFile(IntervalDirectory iv, string topic, string entityId)
    {
        var safeTopic = BundleNaming.SafeFileName(topic);
        var safeEntity = BundleNaming.SafeFileName(entityId);
        var dir = Path.Combine(iv.FastStateDirectory, safeTopic, safeEntity);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "samples.parquet"), "");
    }

    private static StubTracker EmptyTracker() =>
        new(new IntervalSetSnapshot([]));

    private static StubTracker TrackerWith(params IntervalDirectory[] dirs)
    {
        var refs = dirs
            .Select(d => new IntervalReference(d, IntervalRole.Completed))
            .ToList()
            .AsReadOnly();
        return new(new IntervalSetSnapshot(refs));
    }

    // ── Tests ──────────────────────────────────────────────────────────────

    [Fact]
    public void LocateFiles_NoIntervals_NoBundle_ReturnsEmpty()
    {
        var locator = new FastStateFileLocator(EmptyTracker());

        locator.LocateFiles("my/topic", "entity-1").Should().BeEmpty();
    }

    [Fact]
    public void LocateFiles_OneIntervalWithFile_ReturnsPath()
    {
        var iv = CreateIntervalDir("iv1");
        CreateParquetFile(iv, "robot/sensors", "robot-01");
        var locator = new FastStateFileLocator(TrackerWith(iv));

        var results = locator.LocateFiles("robot/sensors", "robot-01");

        results.Should().HaveCount(1);
        File.Exists(results[0]).Should().BeTrue();
    }

    [Fact]
    public void LocateFiles_FileAbsent_ReturnsEmpty()
    {
        var iv = CreateIntervalDir("iv-absent");
        // Do NOT create the parquet file
        var locator = new FastStateFileLocator(TrackerWith(iv));

        locator.LocateFiles("robot/sensors", "robot-01").Should().BeEmpty();
    }

    [Fact]
    public void LocateFiles_MultipleIntervals_ReturnsBothFiles()
    {
        var iv1 = CreateIntervalDir("iv-a");
        var iv2 = CreateIntervalDir("iv-b");
        CreateParquetFile(iv1, "nav/state", "robot-42");
        CreateParquetFile(iv2, "nav/state", "robot-42");
        var locator = new FastStateFileLocator(TrackerWith(iv1, iv2));

        locator.LocateFiles("nav/state", "robot-42").Should().HaveCount(2);
    }

    [Fact]
    public void LocateFiles_WithBundle_IncludesBundleFile()
    {
        var iv = CreateIntervalDir("iv-bundle-test");
        CreateParquetFile(iv, "nav/state", "robot-1");

        var bundleDir = Path.Combine(_tempRoot, "bundle");
        var safeTopic = BundleNaming.SafeFileName("nav/state");
        var safeEntity = BundleNaming.SafeFileName("robot-1");
        var bundleFile = Path.Combine(bundleDir, "fast_state", safeTopic, safeEntity, "samples.parquet");
        Directory.CreateDirectory(Path.GetDirectoryName(bundleFile)!);
        File.WriteAllText(bundleFile, "");

        var locator = new FastStateFileLocator(TrackerWith(iv), () => bundleDir);

        locator.LocateFiles("nav/state", "robot-1").Should().HaveCount(2);
    }

    [Fact]
    public void GetAvailableTopicsForEntity_NoFiles_ReturnsEmpty()
    {
        var iv = CreateIntervalDir("iv-no-topics");
        var locator = new FastStateFileLocator(TrackerWith(iv));

        locator.GetAvailableTopicsForEntity("entity-x").Should().BeEmpty();
    }

    [Fact]
    public void GetAvailableTopicsForEntity_MultipleTopics_ReturnsAll()
    {
        var iv = CreateIntervalDir("iv-topics");
        CreateParquetFile(iv, "sensors/temp", "sensor-01");
        CreateParquetFile(iv, "sensors/pressure", "sensor-01");
        CreateParquetFile(iv, "nav/odometry", "sensor-01");
        // Different entity — should not be included
        CreateParquetFile(iv, "sensors/temp", "sensor-99");

        var locator = new FastStateFileLocator(TrackerWith(iv));

        var topics = locator.GetAvailableTopicsForEntity("sensor-01");
        topics.Should().HaveCount(3);
        topics.Should().Contain(BundleNaming.SafeFileName("sensors/temp"));
        topics.Should().Contain(BundleNaming.SafeFileName("sensors/pressure"));
        topics.Should().Contain(BundleNaming.SafeFileName("nav/odometry"));
    }

    // ── Stub ───────────────────────────────────────────────────────────────

    private sealed class StubTracker : IntervalSetTracker
    {
        private readonly IntervalSetSnapshot _snapshot;

        public StubTracker(IntervalSetSnapshot snapshot)
            : base(null!, 0, NullLogger<IntervalSetTracker>.Instance)
        {
            _snapshot = snapshot;
        }

        public override IntervalSetSnapshot CurrentSnapshot() => _snapshot;
    }
}
