using FluentAssertions;
using Tracer.Adapters.Mock.Scenarios;
using Tracer.Agent.Configuration;
using Tracer.Bundle.Packaging;
using Tracer.Bundle.Validation;
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

        var validation = await BundleValidator.ValidateAsync(outputPath, manifest, strict: false);
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
}
