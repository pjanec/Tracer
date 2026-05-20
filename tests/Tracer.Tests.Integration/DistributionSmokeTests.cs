using FluentAssertions;
using Xunit;

namespace Tracer.Tests.Integration;

/// <summary>
/// Smoke tests for the self-contained distribution package (TRC-P4-010).
/// Invokes <c>dotnet publish</c> for the OfflineViewer and verifies expected output layout.
/// </summary>
[Collection("Distribution")]
public sealed class DistributionSmokeTests : IAsyncLifetime
{
    private string? _outputDir;

    public Task InitializeAsync()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), $"dist-smoke-{Guid.NewGuid():N}");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            if (_outputDir is not null && Directory.Exists(_outputDir))
                Directory.Delete(_outputDir, recursive: true);
        }
        catch { /* best-effort cleanup */ }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Publish_ProducesExpectedLayout()
    {
        // Resolve the OfflineViewer project path relative to test DLL location
        // Structure: tests/Tracer.Tests.Integration/bin/Release/net8.0 → repo root is 5 levels up
        var binDir = Path.GetDirectoryName(typeof(DistributionSmokeTests).Assembly.Location)!;
        var repoRoot = binDir;
        for (var i = 0; i < 5; i++) repoRoot = Path.GetDirectoryName(repoRoot)!;
        var projectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "Tracer.OfflineViewer"));

        Directory.Exists(projectPath).Should().BeTrue(
            $"OfflineViewer project directory should exist at: {projectPath}");

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"publish \"{projectPath}\" -c Release -r win-x64 --self-contained " +
                        $"-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true " +
                        $"-o \"{_outputDir}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = System.Diagnostics.Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        process.ExitCode.Should().Be(0,
            $"dotnet publish should succeed.\nStdout:\n{stdout}\nStderr:\n{stderr}");

        var exePath = Path.Combine(_outputDir!, "tracer-viewer.exe");
        File.Exists(exePath).Should().BeTrue(
            $"tracer-viewer.exe should be present in publish output at: {exePath}");
    }

    [Fact]
    public void Csproj_ContainsSelfContainedProperties()
    {
        var binDir = Path.GetDirectoryName(typeof(DistributionSmokeTests).Assembly.Location)!;
        var repoRoot = binDir;
        for (var i = 0; i < 5; i++) repoRoot = Path.GetDirectoryName(repoRoot)!;
        var csprojPath = Path.GetFullPath(
            Path.Combine(repoRoot, "src", "Tracer.OfflineViewer", "Tracer.OfflineViewer.csproj"));

        File.Exists(csprojPath).Should().BeTrue($"csproj not found at: {csprojPath}");

        var xml = File.ReadAllText(csprojPath);
        xml.Should().Contain("<SelfContained>true</SelfContained>",
            "csproj must have SelfContained=true");
        xml.Should().Contain("<PublishSingleFile>true</PublishSingleFile>",
            "csproj must have PublishSingleFile=true");
        xml.Should().Contain("<PublishTrimmed>false</PublishTrimmed>",
            "csproj must have PublishTrimmed=false");
        xml.Should().Contain("<InvariantGlobalization>true</InvariantGlobalization>",
            "csproj must have InvariantGlobalization=true");
        xml.Should().Contain("<RuntimeIdentifier>win-x64</RuntimeIdentifier>",
            "csproj must have RuntimeIdentifier=win-x64");
    }

    [Fact]
    public void BuildScript_ContainsRequiredPhrases()
    {
        var binDir = Path.GetDirectoryName(typeof(DistributionSmokeTests).Assembly.Location)!;
        var repoRoot = binDir;
        for (var i = 0; i < 5; i++) repoRoot = Path.GetDirectoryName(repoRoot)!;
        repoRoot = Path.GetFullPath(repoRoot);

        var scriptPath = Path.Combine(repoRoot, "build-viewer-distribution.ps1");
        File.Exists(scriptPath).Should().BeTrue($"build script not found at: {scriptPath}");

        var content = File.ReadAllText(scriptPath);
        content.Should().Contain("Double-click tracer-viewer.exe",
            "README must tell user to double-click the exe");
        content.Should().Contain("No installation required",
            "README must state no installation required");
        content.Should().Contain("TracerViewer.zip",
            "Script must produce TracerViewer.zip");
    }
}
