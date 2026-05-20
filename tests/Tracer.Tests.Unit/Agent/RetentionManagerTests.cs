using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Agent.Configuration;
using Tracer.Agent.Storage;
using Tracer.Core.Domain;
using Xunit;

namespace Tracer.Tests.Unit.Agent;

public sealed class RetentionManagerTests : IDisposable
{
    private readonly string _tempDir;

    public RetentionManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private AgentConfig MakeConfig(int keepLast = 3) => new()
    {
        NodeId = "n",
        DataRoot = _tempDir,
        LogsRoot = _tempDir,
        IntervalDuration = TimeSpan.FromHours(1),
        KeepLastNIntervals = keepLast,
        DiskWatermarkPercent = 1,
    };

    private static void CreateReadyInterval(string dataRoot, string timestamp)
    {
        var path = Path.Combine(dataRoot, "intervals", timestamp);
        Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path, "_ready"), Array.Empty<byte>());
    }

    private static void CreateOrphanInterval(string dataRoot, string timestamp)
    {
        var path = Path.Combine(dataRoot, "intervals", timestamp);
        Directory.CreateDirectory(path);
    }

    private RetentionManager BuildManager(AgentConfig config)
        => new RetentionManager(config, NullLogger<RetentionManager>.Instance);

    [Fact]
    public async Task RetentionManager_KeepLast3_WithFiveIntervals_DeletesOldestTwo()
    {
        var config = MakeConfig(keepLast: 3);
        var timestamps = new[] { "20260519T100000Z", "20260519T110000Z", "20260519T120000Z", "20260519T130000Z", "20260519T140000Z" };
        foreach (var ts in timestamps) CreateReadyInterval(_tempDir, ts);

        await BuildManager(config).ApplyAsync(null, CancellationToken.None);

        var remaining = Directory.GetDirectories(Path.Combine(_tempDir, "intervals"))
            .Select(d => Path.GetFileName(d)).OrderBy(x => x).ToList();

        remaining.Should().HaveCount(3);
        remaining.Should().NotContain("20260519T100000Z");
        remaining.Should().NotContain("20260519T110000Z");
        remaining.Should().Contain("20260519T120000Z");
        remaining.Should().Contain("20260519T130000Z");
        remaining.Should().Contain("20260519T140000Z");
    }

    [Fact]
    public async Task RetentionManager_OrphanNotDeleted()
    {
        var config = MakeConfig(keepLast: 1);
        CreateReadyInterval(_tempDir, "20260519T120000Z");
        CreateReadyInterval(_tempDir, "20260519T130000Z");
        CreateOrphanInterval(_tempDir, "20260519T100000Z");

        await BuildManager(config).ApplyAsync(null, CancellationToken.None);

        Directory.Exists(Path.Combine(_tempDir, "intervals", "20260519T100000Z"))
            .Should().BeTrue("orphan intervals are not deleted by retention");
    }

    [Fact]
    public async Task RetentionManager_NothingToEvict_NoException()
    {
        var config = MakeConfig(keepLast: 10);
        CreateReadyInterval(_tempDir, "20260519T130000Z");
        CreateReadyInterval(_tempDir, "20260519T140000Z");

        var act = async () => await BuildManager(config).ApplyAsync(null, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }
}
