using System.CommandLine;
using System.CommandLine.Invocation;
using Tracer.Bundle.Packaging;

namespace Tracer.Aggregator.Cli.Commands;

internal static class InspectCommand
{
    public static Command Create()
    {
        var cmd = new Command("inspect", "Print a human-readable summary of a bundle");
        var bundlePathArg = new Argument<string>("bundle-path", "Path to the bundle directory or .zip file");
        cmd.AddArgument(bundlePathArg);

        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var bundlePath = ctx.ParseResult.GetValueForArgument(bundlePathArg);

            try
            {
                var manifest = await BundleReader.ReadManifestAsync(bundlePath, ctx.GetCancellationToken());

                Console.WriteLine($"Bundle: {Path.GetFileName(bundlePath)}");
                Console.WriteLine($"ID:          {manifest.BundleId}");
                Console.WriteLine($"Schema:      v{manifest.SchemaVersion}");
                Console.WriteLine($"Created:     {manifest.CreatedAtUtc:O}");
                Console.WriteLine($"Time range:  {manifest.TimeRange?.StartUtc:O} .. {manifest.TimeRange?.EndUtc:O}");
                Console.WriteLine($"Label:       {manifest.SessionContext?.Label ?? "(none)"}");
                Console.WriteLine($"Session:     {manifest.SessionContext?.SessionId ?? "(none)"}");
                Console.WriteLine();
                Console.WriteLine("Statistics:");
                if (manifest.Statistics is { } s)
                {
                    Console.WriteLine($"  Events:               {s.TotalEvents:N0}");
                    Console.WriteLine($"  Slow-state samples:   {s.TotalSlowStateSamples:N0}");
                    Console.WriteLine($"  Fast-state rows:      {s.TotalFastStateRows:N0}");
                    Console.WriteLine($"  Uncompressed bytes:   {FormatBytes(s.UncompressedBytes)}");
                }
                Console.WriteLine();
                Console.WriteLine($"Participating nodes ({manifest.ParticipatingNodes?.Count ?? 0}):");
                if (manifest.ParticipatingNodes is { Count: > 0 } nodes)
                    Console.WriteLine($"  {string.Join(", ", nodes)}");
                Console.WriteLine();
                Console.WriteLine($"Files ({manifest.Files?.Count ?? 0}):");
                if (manifest.Files is not null)
                {
                    foreach (var f in manifest.Files)
                        Console.WriteLine($"  {f.Path,-50}  {FormatBytes(f.SizeBytes),10}  {f.Sha256[..8]}...");
                }

                ctx.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[error] Inspect failed: {ex.Message}");
                ctx.ExitCode = 1;
            }
        });

        return cmd;
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576     => $"{bytes / 1_048_576.0:F1} MB",
            >= 1_024         => $"{bytes / 1_024.0:F1} KB",
            _                => $"{bytes} B",
        };
    }
}
