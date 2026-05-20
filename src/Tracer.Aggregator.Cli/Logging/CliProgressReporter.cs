using Tracer.Aggregator.Progress;

namespace Tracer.Aggregator.Cli.Logging;

/// <summary>
/// Progress reporter that writes aggregation stage updates to stderr.
/// On creation it emits a LOG_FILE= line to stdout per the CLI convention.
/// </summary>
internal sealed class CliProgressReporter : IAggregationProgressReporter
{
    internal CliProgressReporter()
    {
        // Per §6.6: print LOG_FILE= to stdout so it can be captured via redirect.
        // We write to a per-invocation log file in the user's local app data.
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tracer", "cli-logs");
        Directory.CreateDirectory(logDir);
        var logFile = Path.Combine(logDir,
            $"tracer-aggregate-{DateTimeOffset.UtcNow:yyyyMMdd}.log");

        Console.WriteLine($"LOG_FILE={logFile}");
    }

    public void Report(AggregationStage stage, string? message = null)
    {
        var prefix = stage switch
        {
            AggregationStage.Failed => "[error]",
            _ => "[info]",
        };
        Console.Error.WriteLine(message is null
            ? $"{prefix} {stage}"
            : $"{prefix} {message}");
    }
}
