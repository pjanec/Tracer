using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Adapters.Mock.Storage;
using Tracer.Aggregator.Cli.Logging;
using Tracer.Aggregator.Configuration;
using Tracer.Core.Time;

namespace Tracer.Aggregator.Cli.Commands;

internal static class BuildCommand
{
    public static Command Create(Option<string?> nasRootOption, Option<string> logLevelOption)
    {
        var cmd = new Command("build", "Build a bundle from telemetry data");

        var sessionIdOption = new Option<string?>("--session-id", "Session ID to build a bundle for");
        var timeRangeOption = new Option<string?>(
            "--time-range",
            "Time range in ISO 8601 UTC format: start..end (e.g. 2026-05-19T14:00:00Z..2026-05-19T15:00:00Z)");
        var outputOption = new Option<string>("--output", "Output path for the bundle (required)") { IsRequired = true };
        var nodesOption = new Option<string?>("--nodes", "Comma-separated node IDs to include (default: all)");
        var fastStateOption = new Option<string>("--fast-state", () => "none", "Fast-state scope: none | selected | all");
        var fastStateEntitiesOption = new Option<string?>("--fast-state-entities", "Comma-separated entity IDs for --fast-state selected");
        var labelOption = new Option<string?>("--label", "Override the bundle label");
        var forceOption = new Option<bool>("--force", "Overwrite output path if it exists");

        cmd.AddOption(sessionIdOption);
        cmd.AddOption(timeRangeOption);
        cmd.AddOption(outputOption);
        cmd.AddOption(nodesOption);
        cmd.AddOption(fastStateOption);
        cmd.AddOption(fastStateEntitiesOption);
        cmd.AddOption(labelOption);
        cmd.AddOption(forceOption);

        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var nasRoot    = ctx.ParseResult.GetValueForOption(nasRootOption);
            var sessionId  = ctx.ParseResult.GetValueForOption(sessionIdOption);
            var timeRange  = ctx.ParseResult.GetValueForOption(timeRangeOption);
            var output     = ctx.ParseResult.GetValueForOption(outputOption)!;
            var nodes      = ctx.ParseResult.GetValueForOption(nodesOption);
            var fastState  = ctx.ParseResult.GetValueForOption(fastStateOption);
            var fsEntities = ctx.ParseResult.GetValueForOption(fastStateEntitiesOption);
            var label      = ctx.ParseResult.GetValueForOption(labelOption);
            var force      = ctx.ParseResult.GetValueForOption(forceOption);

            if (string.IsNullOrWhiteSpace(sessionId) && string.IsNullOrWhiteSpace(timeRange))
            {
                Console.Error.WriteLine("[error] You must specify either --session-id or --time-range.");
                ctx.ExitCode = 1;
                return;
            }

            // Validate output path
            if (!force && (Directory.Exists(output) || File.Exists(output)))
            {
                Console.Error.WriteLine($"[error] Output path already exists: {output}. Use --force to overwrite.");
                ctx.ExitCode = 1;
                return;
            }

            if (force)
            {
                if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
                else if (File.Exists(output)) File.Delete(output);
            }

            if (string.IsNullOrWhiteSpace(nasRoot))
            {
                Console.Error.WriteLine("[error] --nas-root is required for the build command.");
                ctx.ExitCode = 1;
                return;
            }

            var reporter = new CliProgressReporter();

            var reader = new LocalFileSystemStorageReader(nasRoot);
            var orchestrator = new AggregationOrchestrator(reader);

            // Build request
            var request = new AggregationRequest { OutputPath = output };

            if (!string.IsNullOrWhiteSpace(sessionId))
                request = request with { SessionId = sessionId };

            if (!string.IsNullOrWhiteSpace(timeRange))
            {
                var parts = timeRange.Split("..", 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2 ||
                    !DateTimeOffset.TryParse(parts[0].Trim(), out var from) ||
                    !DateTimeOffset.TryParse(parts[1].Trim(), out var to))
                {
                    Console.Error.WriteLine("[error] --time-range must be in the format start..end (ISO 8601 UTC).");
                    ctx.ExitCode = 1;
                    return;
                }
                request = request with
                {
                    TimeRange = new Tracer.Core.Time.TimeRange(
                        WallclockTime.FromDateTimeOffset(from),
                        WallclockTime.FromDateTimeOffset(to))
                };
            }

            if (!string.IsNullOrWhiteSpace(nodes))
                request = request with { NodeFilter = nodes.Split(',', StringSplitOptions.TrimEntries) };

            request = request with
            {
                FastStateScope = fastState?.ToLowerInvariant() switch
                {
                    "all"      => FastStateScope.All,
                    "selected" => FastStateScope.SelectedEntities,
                    _          => FastStateScope.None,
                }
            };

            if (!string.IsNullOrWhiteSpace(fsEntities))
                request = request with
                {
                    FastStateEntities = fsEntities.Split(',', StringSplitOptions.TrimEntries)
                };

            if (!string.IsNullOrWhiteSpace(label))
                request = request with { LabelOverride = label };

            try
            {
                var result = await orchestrator.RunAsync(request, reporter, ctx.GetCancellationToken());
                Console.Error.WriteLine($"[info] Bundle complete: {result.OutputPath}");
                ctx.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[error] Build failed: {ex.Message}");
                ctx.ExitCode = 1;
            }
        });

        return cmd;
    }
}
