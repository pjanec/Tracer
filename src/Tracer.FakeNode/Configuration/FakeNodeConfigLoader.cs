using System.Text.Json;
using Tracer.Adapters.Mock.Scenarios;
using Tracer.Agent.Configuration;

namespace Tracer.FakeNode.Configuration;

/// <summary>
/// Loads <see cref="FakeNodeConfig"/> from a JSON file specified by --config &lt;path&gt;.
/// </summary>
public static class FakeNodeConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Loads the FakeNode configuration from the JSON file specified by --config.
    /// The path must be absolute.
    /// </summary>
    public static FakeNodeConfig Load(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var idx = Array.IndexOf(args, "--config");
        if (idx < 0 || idx + 1 >= args.Length)
            throw new ArgumentException("--config <path> is required");

        var path = args[idx + 1];
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException($"--config path must be absolute: '{path}'");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Config file not found: {path}", path);

        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("FakeNode", out var fakeNodeEl))
            throw new InvalidOperationException("JSON must have a top-level 'FakeNode' object");

        var scenarioName = fakeNodeEl.GetProperty("ScenarioName").GetString()
            ?? throw new InvalidOperationException("FakeNode.ScenarioName is required");

        var scenarioConfig = JsonSerializer.Deserialize<ScenarioConfig>(
            fakeNodeEl.GetProperty("ScenarioConfig").GetRawText(), Options)
            ?? throw new InvalidOperationException("FakeNode.ScenarioConfig is required");

        var agentConfig = JsonSerializer.Deserialize<AgentConfig>(
            fakeNodeEl.GetProperty("AgentConfig").GetRawText(), Options)
            ?? throw new InvalidOperationException("FakeNode.AgentConfig is required");

        return new FakeNodeConfig
        {
            ScenarioName = scenarioName,
            ScenarioConfig = scenarioConfig,
            AgentConfig = agentConfig,
        };
    }
}
