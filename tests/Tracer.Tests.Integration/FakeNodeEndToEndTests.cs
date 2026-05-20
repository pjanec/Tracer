using FluentAssertions;
using Tracer.Adapters.Mock.Scenarios;
using Tracer.Agent.Configuration;
using Tracer.Core.Domain;
using Tracer.Core.Time;
using Tracer.TestHarness;
using Xunit;

namespace Tracer.Tests.Integration;

/// <summary>End-to-end tests via <see cref="FakeNodeFixture"/>.</summary>
public sealed class FakeNodeEndToEndTests
{
    private static AgentConfig MakeAgentConfig(string dataRoot, string uploadRoot) =>
        new()
        {
            NodeId = "e2e-node",
            DataRoot = dataRoot,
            LogsRoot = Path.Combine(dataRoot, "_logs"),
            IntervalDuration = TimeSpan.FromMinutes(15),
            KeepLastNIntervals = 24,
            Transport = new TransportConfig { CapacityRecords = 50_000 },
            UploadService = new UploadServiceConfig { LocalFileSystemRoot = uploadRoot },
        };

    [Fact]
    public async Task CalmScenario_ProducesIntervals()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), $"tracer-e2e-{Guid.NewGuid():N}");
        var uploadRoot = Path.Combine(Path.GetTempPath(), $"tracer-e2e-upload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(uploadRoot);

        var agentConfig = MakeAgentConfig(dataRoot, uploadRoot);
        var scenarioConfig = new ScenarioConfig
        {
            Duration = TimeSpan.FromSeconds(10),
            Seed = 42,
            EventsPerSecond = 50,
        };

        await using var fixture = await FakeNodeFixture.RunScenarioAsync(
            "Calm",
            scenarioConfig,
            agentConfig);

        fixture.Manifests.Should().NotBeEmpty("FakeNode should produce at least one interval");

        foreach (var manifest in fixture.Manifests)
        {
            manifest.FinalizationReason.Should().NotBe(
                ManifestFinalizationReason.RecoveryAfterCrash,
                "no crash recovery expected in a clean FakeNode run");
        }
    }

    [Fact]
    public async Task AllIntervalsUploaded()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), $"tracer-e2e2-{Guid.NewGuid():N}");
        var uploadRoot = Path.Combine(Path.GetTempPath(), $"tracer-e2e2-upload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(uploadRoot);

        var agentConfig = MakeAgentConfig(dataRoot, uploadRoot);
        var scenarioConfig = new ScenarioConfig
        {
            Duration = TimeSpan.FromSeconds(10),
            Seed = 42,
            EventsPerSecond = 50,
        };

        await using var fixture = await FakeNodeFixture.RunScenarioAsync(
            "Calm",
            scenarioConfig,
            agentConfig);

        // Count ready interval directories
        var intervalsDir = Path.Combine(dataRoot, "intervals");
        var readyCount = Directory.Exists(intervalsDir)
            ? Directory.GetDirectories(intervalsDir)
                .Count(d => File.Exists(Path.Combine(d, "_ready")))
            : 0;

        fixture.IntervalZipPaths.Should().HaveCount(readyCount,
            "every completed interval should produce one upload zip");
    }

    [Fact]
    public async Task GracefulShutdown_LastInterval_HasGracefulReason()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), $"tracer-e2e3-{Guid.NewGuid():N}");
        var uploadRoot = Path.Combine(Path.GetTempPath(), $"tracer-e2e3-upload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(uploadRoot);

        var agentConfig = MakeAgentConfig(dataRoot, uploadRoot);
        var scenarioConfig = new ScenarioConfig
        {
            Duration = TimeSpan.FromSeconds(10),
            Seed = 42,
            EventsPerSecond = 50,
        };

        await using var fixture = await FakeNodeFixture.RunScenarioAsync(
            "Calm",
            scenarioConfig,
            agentConfig);

        fixture.Manifests.Should().NotBeEmpty();

        var lastManifest = fixture.Manifests.Last();
        lastManifest.FinalizationReason.Should().Be(
            ManifestFinalizationReason.GracefulShutdown,
            "the final interval must be finalized with GracefulShutdown");
    }
}
