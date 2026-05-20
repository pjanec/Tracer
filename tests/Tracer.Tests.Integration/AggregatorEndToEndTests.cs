using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Adapters.Mock.Scenarios;
using Tracer.Adapters.Mock.Storage;
using Tracer.Agent.Configuration;
using Tracer.Aggregator;
using Tracer.Aggregator.Configuration;
using Tracer.Aggregator.Progress;
using Tracer.Bundle.Packaging;
using Tracer.Bundle.Validation;
using Tracer.Core.Domain;
using Tracer.Core.Time;
using Tracer.TestHarness;
using Xunit;
using CliProgram = Tracer.Aggregator.Cli.Program;

namespace Tracer.Tests.Integration;

/// <summary>
/// End-to-end tests for the <c>tracer-aggregate</c> CLI (TRC-P4-007).
/// Uses a real FakeNode run to produce a mock-NAS directory, then exercises
/// the CLI build / validate / inspect commands.
/// </summary>
public sealed class AggregatorEndToEndTests : IAsyncDisposable
{
    private readonly List<string> _dirs = new();

    private string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"cli-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        _dirs.Add(d);
        return d;
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        foreach (var d in _dirs)
        {
            try { if (Directory.Exists(d)) Directory.Delete(d, recursive: true); } catch { }
        }
    }

    // ── Helper to run a tiny FakeNode scenario ────────────────────────────────

    private static AgentConfig MakeAgentConfig(string dataRoot, string uploadRoot) => new()
    {
        NodeId = "agg-e2e-node",
        DataRoot = dataRoot,
        LogsRoot = Path.Combine(dataRoot, "_logs"),
        IntervalDuration = TimeSpan.FromMinutes(15),
        KeepLastNIntervals = 24,
        Transport = new TransportConfig { CapacityRecords = 50_000 },
        UploadService = new UploadServiceConfig { LocalFileSystemRoot = uploadRoot },
    };

    /// <summary>
    /// Runs a short FakeNode scenario and returns (nasRoot, timeRange) where
    /// timeRange is an ISO-8601 UTC range string "start..end" covering all intervals.
    /// </summary>
    private async Task<(string nasRoot, string timeRange)> RunNasAsync()
    {
        var dataRoot   = TempDir();
        var uploadRoot = TempDir();

        var agentConfig = MakeAgentConfig(dataRoot, uploadRoot);
        var scenarioConfig = new ScenarioConfig
        {
            Duration = TimeSpan.FromSeconds(8),
            Seed = 77,
            EventsPerSecond = 30,
        };

        // Do NOT use await-using here: the fixture's DisposeAsync deletes uploadRoot.
        // We hold a reference so we can dispose it manually after we're done reading from it.
        var fixture = await FakeNodeFixture.RunScenarioAsync(
            "Calm", scenarioConfig, agentConfig);

        fixture.Manifests.Should().NotBeEmpty();

        var start = fixture.Manifests.Min(m => m.IntervalStart.ToDateTimeOffset());
        var end   = fixture.Manifests.Max(m => m.IntervalEnd.ToDateTimeOffset());
        var timeRange = $"{start:O}..{end:O}";

        // Snapshot: copy upload root to a separate dir that only our TempDir cleanup manages.
        var nasSnapshot = TempDir();
        foreach (var file in Directory.GetFiles(uploadRoot, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(uploadRoot, file);
            var dst = Path.Combine(nasSnapshot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(file, dst);
        }

        // Now safe to dispose (it deletes dataRoot and uploadRoot, not nasSnapshot)
        await fixture.DisposeAsync();

        return (nasSnapshot, timeRange);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildCommand_ProducesValidBundle()
    {
        var (nasRoot, timeRange) = await RunNasAsync();
        var outputPath = Path.Combine(TempDir(), "output.bundle");

        var exitCode = await CliProgram.Main(new[]
        {
            "build",
            "--nas-root", nasRoot,
            "--time-range", timeRange,
            "--output", outputPath,
        });

        exitCode.Should().Be(0, "build command should succeed");
        Directory.Exists(outputPath).Should().BeTrue("bundle directory should be created");

        // Validate the bundle
        var manifest = await BundleReader.ReadManifestAsync(outputPath);
        manifest.BundleId.Should().NotBeNullOrEmpty();

        var validation = await BundleValidator.ValidateAsync(outputPath, manifest, strict: true);
        validation.IsValid.Should().BeTrue(
            $"bundle should be valid; errors: {string.Join(", ", validation.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public async Task BuildCommand_NeitherSessionNorTimeRange_ExitsNonZero()
    {
        var exitCode = await CliProgram.Main(new[]
        {
            "build",
            "--nas-root", TempDir(),
            "--output", Path.Combine(TempDir(), "out"),
        });

        exitCode.Should().NotBe(0, "missing --session-id and --time-range should fail");
    }

    [Fact]
    public async Task BuildCommand_ExistingOutput_WithoutForce_ExitsNonZero()
    {
        var outputPath = TempDir(); // already exists

        var exitCode = await CliProgram.Main(new[]
        {
            "build",
            "--nas-root", TempDir(),
            "--time-range", "2025-01-01T00:00:00Z..2025-01-01T01:00:00Z",
            "--output", outputPath,
            // no --force
        });

        exitCode.Should().NotBe(0, "should fail when output exists and --force not specified");
    }

    [Fact]
    public async Task ValidateCommand_ValidBundle_ExitsZero()
    {
        var (nasRoot, timeRange) = await RunNasAsync();
        var outputPath = Path.Combine(TempDir(), "bundle");

        // Build the bundle first
        var buildExit = await CliProgram.Main(new[]
        {
            "build", "--nas-root", nasRoot, "--time-range", timeRange, "--output", outputPath,
        });
        buildExit.Should().Be(0);

        // Now validate
        var validateExit = await CliProgram.Main(new[]
        {
            "validate", outputPath,
        });
        validateExit.Should().Be(0, "validate should succeed on a valid bundle");
    }

    [Fact]
    public async Task ValidateCommand_CorruptedManifest_ExitsOne()
    {
        var (nasRoot, timeRange) = await RunNasAsync();
        var outputPath = Path.Combine(TempDir(), "bundle");

        var buildExit = await CliProgram.Main(new[]
        {
            "build", "--nas-root", nasRoot, "--time-range", timeRange, "--output", outputPath,
        });
        buildExit.Should().Be(0);

        // Corrupt the manifest
        var manifestPath = Path.Combine(outputPath, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, "NOT VALID JSON {{{{");

        var validateExit = await CliProgram.Main(new[]
        {
            "validate", outputPath,
        });
        validateExit.Should().Be(1, "corrupted manifest should cause validate to exit 1");
    }

    [Fact]
    public async Task InspectCommand_OutputContainsBundleId()
    {
        var (nasRoot, timeRange) = await RunNasAsync();
        var outputPath = Path.Combine(TempDir(), "bundle");

        var buildExit = await CliProgram.Main(new[]
        {
            "build", "--nas-root", nasRoot, "--time-range", timeRange, "--output", outputPath,
        });
        buildExit.Should().Be(0);

        var manifest = await BundleReader.ReadManifestAsync(outputPath);

        // Capture stdout from inspect
        var originalOut = Console.Out;
        using var sw = new System.IO.StringWriter();
        Console.SetOut(sw);
        try
        {
            var inspectExit = await CliProgram.Main(new[]
            {
                "inspect", outputPath,
            });
            inspectExit.Should().Be(0, "inspect should succeed");
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = sw.ToString();
        output.Should().Contain(manifest.BundleId,
            "inspect output should contain the bundle ID");
    }

    [Fact]
    public async Task BuildCommand_LogFileAnnouncedOnStdout()
    {
        var (nasRoot, timeRange) = await RunNasAsync();
        var outputPath = Path.Combine(TempDir(), "bundle");

        // Capture stdout
        var originalOut = Console.Out;
        using var sw = new System.IO.StringWriter();
        Console.SetOut(sw);
        try
        {
            await CliProgram.Main(new[]
            {
                "build", "--nas-root", nasRoot, "--time-range", timeRange, "--output", outputPath,
            });
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var stdoutLines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        stdoutLines.Should().NotBeEmpty();
        stdoutLines[0].Should().StartWith("LOG_FILE=",
            "first stdout line must be the LOG_FILE= announcement");
    }

    // ── TRC-P4-013: Additional required test methods ─────────────────────────

    [Fact]
    public async Task Build_SessionIdVariant_UsesCorrectTimeRange()
    {
        // AggregationFixture aligns event timestamps with real UTC so the session resolver works
        await using var agg = await AggregationFixture.InitializeAsync();
        var outputPath = Path.Combine(TempDir(), "session-bundle");

        // Extract session ID from interval manifests via the storage reader
        var sessionId = await FindFirstSessionIdAsync(agg.NasRoot);
        sessionId.Should().NotBeNullOrEmpty(
            "CalmScenario always emits a session_start marker with a sessionId in the payload");

        var exitCode = await CliProgram.Main(new[]
        {
            "build",
            "--nas-root", agg.NasRoot,
            "--session-id", sessionId!,
            "--output", outputPath,
        });

        exitCode.Should().Be(0, "build command with --session-id should succeed");
        Directory.Exists(outputPath).Should().BeTrue("bundle directory should be created");

        var manifest = await BundleReader.ReadManifestAsync(outputPath);
        manifest.TimeRange.StartUtc.Should().BeBefore(manifest.TimeRange.EndUtc,
            "session-resolved time range must be a valid non-empty interval");
    }

    [Fact]
    public async Task Build_EventCount_MatchesSumOfSources()
    {
        var (nasRoot, timeRange) = await RunNasAsync();
        var outputPath = Path.Combine(TempDir(), "count-bundle");

        // Parse time range boundaries
        var parts = timeRange.Split("..");
        var rangeStart = DateTimeOffset.Parse(parts[0]).UtcDateTime;
        var rangeEnd   = DateTimeOffset.Parse(parts[1]).UtcDateTime;

        // Count source events within the time range by opening each interval's DuckDB file
        var sourceCount = await CountSourceEventsAsync(nasRoot, rangeStart, rangeEnd);

        // Build the bundle via CLI using the same time range
        var exitCode = await CliProgram.Main(new[]
        {
            "build",
            "--nas-root", nasRoot,
            "--time-range", timeRange,
            "--output", outputPath,
        });
        exitCode.Should().Be(0, "build should succeed");

        // Count events in the bundle's consolidated events.duckdb
        var bundleEventsPath = Path.Combine(outputPath, "events.duckdb");
        var bundleCount = await CountBundleEventsAsync(bundleEventsPath);

        // The bundle must contain exactly the events from source files within the time range
        bundleCount.Should().Be(sourceCount,
            "bundle event count must equal the sum of source events within the time range");
    }

    [Fact]
    public async Task Build_ProgressEvents_InOrder()
    {
        var (nasRoot, timeRange) = await RunNasAsync();
        var outputPath = Path.Combine(TempDir(), "progress-bundle");

        var parts = timeRange.Split("..");
        var rangeStart = WallclockTime.FromDateTimeOffset(DateTimeOffset.Parse(parts[0]));
        var rangeEnd   = WallclockTime.FromDateTimeOffset(DateTimeOffset.Parse(parts[1]));

        var orchestrator = new AggregationOrchestrator(
            new LocalFileSystemStorageReader(nasRoot),
            NullLogger<AggregationOrchestrator>.Instance);

        var capturedStages = new List<AggregationStage>();
        var reporter = new DelegatingProgressReporter((stage, _) => capturedStages.Add(stage));

        var request = new AggregationRequest
        {
            TimeRange = new Core.Time.TimeRange(rangeStart, rangeEnd),
            OutputPath = outputPath,
        };
        await orchestrator.RunAsync(request, reporter);

        capturedStages.Should().NotBeEmpty("orchestrator must emit at least one progress event");
        capturedStages.First().Should().Be(AggregationStage.Started,
            "first progress event must be AggregationStage.Started");
        capturedStages.Last().Should().Be(AggregationStage.Completed,
            "last progress event must be AggregationStage.Completed");
        capturedStages.Should().NotContain(AggregationStage.Failed,
            "no Failed stage should be emitted for a successful run");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans interval manifests in the NAS and returns the first session ID found.
    /// </summary>
    private static async Task<string?> FindFirstSessionIdAsync(string nasRoot)
    {
        var reader = new LocalFileSystemStorageReader(nasRoot);
        var nodes = await reader.ListNodesAsync();
        foreach (var node in nodes)
        {
            var intervals = await reader.ListIntervalsAsync(node);
            foreach (var iv in intervals)
            {
                var manifest = await reader.ReadIntervalManifestAsync(node, iv);
                if (manifest is null) continue;
                var marker = manifest.SessionMarkers
                    .FirstOrDefault(m => m.Type == SessionMarkerType.Start);
                if (marker is not null) return marker.SessionId;
            }
        }
        return null;
    }

    /// <summary>
    /// Counts events in all source DuckDB files found in interval zips within <paramref name="nasRoot"/>
    /// that fall within the specified wallclock time range.
    /// </summary>
    private static async Task<long> CountSourceEventsAsync(
        string nasRoot, DateTime rangeStart, DateTime rangeEnd)
    {
        long total = 0;
        var extractBase = Path.Combine(Path.GetTempPath(), $"src-count-{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractBase);
        try
        {
            foreach (var zipFile in Directory.GetFiles(nasRoot, "*.zip", SearchOption.AllDirectories))
            {
                var extractDir = Path.Combine(extractBase, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(extractDir);
                System.IO.Compression.ZipFile.ExtractToDirectory(zipFile, extractDir);

                var dbPath = Path.Combine(extractDir, "events.duckdb");
                if (!File.Exists(dbPath)) continue;

                await using var conn = new DuckDB.NET.Data.DuckDBConnection($"DataSource={dbPath}");
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "SELECT COUNT(*) FROM events " +
                    "WHERE publish_wallclock >= $from AND publish_wallclock < $to";
                cmd.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter("from", rangeStart));
                cmd.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter("to", rangeEnd));
                var scalar = await cmd.ExecuteScalarAsync();
                if (scalar is not null)
                    total += Convert.ToInt64(scalar);
            }
        }
        finally
        {
            try { Directory.Delete(extractBase, recursive: true); } catch { /* best-effort */ }
        }
        return total;
    }

    /// <summary>
    /// Counts all events in the bundle's consolidated <c>events.duckdb</c> file.
    /// </summary>
    private static async Task<long> CountBundleEventsAsync(string bundleEventsDbPath)
    {
        if (!File.Exists(bundleEventsDbPath)) return 0;
        await using var conn = new DuckDB.NET.Data.DuckDBConnection($"DataSource={bundleEventsDbPath}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM events";
        var scalar = await cmd.ExecuteScalarAsync();
        return scalar is not null ? Convert.ToInt64(scalar) : 0;
    }

    /// <summary>Captures aggregation progress stages via a callback.</summary>
    private sealed class DelegatingProgressReporter : IAggregationProgressReporter
    {
        private readonly Action<AggregationStage, string?> _callback;

        public DelegatingProgressReporter(Action<AggregationStage, string?> callback)
        {
            _callback = callback;
        }

        public void Report(AggregationStage stage, string? message = null)
            => _callback(stage, message);
    }
}
