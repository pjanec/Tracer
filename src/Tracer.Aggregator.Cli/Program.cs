using System.CommandLine;
using System.CommandLine.Invocation;
using Tracer.Aggregator.Cli.Commands;

namespace Tracer.Aggregator.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var root = BuildRootCommand();
        return await root.InvokeAsync(args);
    }

    internal static RootCommand BuildRootCommand()
    {
        var nasRootOption = new Option<string?>("--nas-root", "Path to (mock) NAS root");
        var logLevelOption = new Option<string>("--log-level", () => "information",
            "Minimum log level: trace, debug, information, warning, error");

        var root = new RootCommand("Tracer Aggregator — build, validate, and inspect bundles");
        root.AddGlobalOption(nasRootOption);
        root.AddGlobalOption(logLevelOption);

        root.AddCommand(BuildCommand.Create(nasRootOption, logLevelOption));
        root.AddCommand(ValidateCommand.Create(logLevelOption));
        root.AddCommand(InspectCommand.Create());

        return root;
    }
}
