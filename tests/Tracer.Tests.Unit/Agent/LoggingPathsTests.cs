using FluentAssertions;
using Tracer.Agent.Logging;
using Xunit;

namespace Tracer.Tests.Unit.Agent;

/// <summary>Unit tests for LOG_FILE path generation (FIX-A4).</summary>
public sealed class LoggingPathsTests
{
    [Fact]
    public void GetCurrentLogFilePath_ReturnsPathInLogsRoot()
    {
        var logsRoot = Path.Combine(Path.GetTempPath(), "tracer-logs-test");
        var path = LoggingPaths.GetCurrentLogFilePath(logsRoot);
        path.Should().StartWith(logsRoot);
    }

    [Fact]
    public void GetCurrentLogFilePath_ContainsDateSuffix()
    {
        var logsRoot = @"C:\tracer\logs";
        var path = LoggingPaths.GetCurrentLogFilePath(logsRoot);
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        path.Should().Contain(today,
            because: "log file name must include the current UTC date");
    }

    [Fact]
    public void GetCurrentLogFilePath_HasJsonExtension()
    {
        var path = LoggingPaths.GetCurrentLogFilePath(@"C:\logs");
        path.Should().EndWith(".json",
            because: "Serilog compact JSON format files use .json extension");
    }

    [Fact]
    public void GetCurrentLogFilePath_ContainsAgentPrefix()
    {
        var path = LoggingPaths.GetCurrentLogFilePath(@"/var/log/tracer");
        Path.GetFileName(path).Should().StartWith("tracer-agent-",
            because: "log file name must start with 'tracer-agent-' to distinguish from other log sources");
    }
}
