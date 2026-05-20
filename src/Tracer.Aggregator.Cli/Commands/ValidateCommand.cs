using System.CommandLine;
using System.CommandLine.Invocation;
using Tracer.Bundle.Packaging;
using Tracer.Bundle.Validation;

namespace Tracer.Aggregator.Cli.Commands;

internal static class ValidateCommand
{
    public static Command Create(Option<string> logLevelOption)
    {
        var cmd = new Command("validate", "Validate an existing bundle's manifest, checksums, and schema");

        var bundlePathArg = new Argument<string>("bundle-path", "Path to the bundle directory or .zip file");
        var strictOption  = new Option<bool>("--strict", "Verify SHA-256 checksums of each file (slower but catches corruption)");

        cmd.AddArgument(bundlePathArg);
        cmd.AddOption(strictOption);

        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var bundlePath = ctx.ParseResult.GetValueForArgument(bundlePathArg);
            var strict     = ctx.ParseResult.GetValueForOption(strictOption);

            try
            {
                var manifest = await BundleReader.ReadManifestAsync(bundlePath, ctx.GetCancellationToken());

                // Resolve directory path for validation (BundleValidator needs a directory)
                var bundleDirectory = Directory.Exists(bundlePath)
                    ? bundlePath
                    : throw new InvalidOperationException(
                        "validate only supports bundle directories. Use 'inspect' for .zip bundles.");

                var result = await BundleValidator.ValidateAsync(
                    bundleDirectory, manifest, strict, ctx.GetCancellationToken());

                if (result.IsValid)
                {
                    Console.Error.WriteLine($"[info] Bundle is valid: {bundlePath}");
                    ctx.ExitCode = 0;
                }
                else
                {
                    foreach (var error in result.Errors)
                        Console.Error.WriteLine($"[error] {error.Code}: {error.Message}");
                    ctx.ExitCode = 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[error] Validation failed: {ex.Message}");
                ctx.ExitCode = 1;
            }
        });

        return cmd;
    }
}
