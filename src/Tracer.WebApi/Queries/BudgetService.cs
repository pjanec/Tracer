using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tracer.Core.Domain;

namespace Tracer.WebApi.Queries;

/// <summary>
/// Provides latency budgets per topic, reading from bundle metadata.json in bundle mode
/// or from the in-memory registry in live mode.
/// </summary>
public sealed class BudgetService
{
    private readonly Func<string?>? _getBundleWorkingDirectory;
    private readonly InMemoryBudgetRegistry? _registry;
    private readonly ILogger<BudgetService>? _logger;

    public BudgetService(
        Func<string?>? getBundleWorkingDirectory,
        InMemoryBudgetRegistry? registry = null,
        ILogger<BudgetService>? logger = null)
    {
        _getBundleWorkingDirectory = getBundleWorkingDirectory;
        _registry = registry;
        _logger = logger;
    }

    public async Task<IReadOnlyList<LatencyBudget>> GetBudgetsAsync(string sessionId, CancellationToken ct)
    {
        // 1. Check in-memory registry first (for live mode / test override)
        if (_registry is not null && _getBundleWorkingDirectory?.Invoke() is null)
            return _registry.GetAll();

        // 2. Bundle mode: read metadata.json
        var workDir = _getBundleWorkingDirectory?.Invoke();
        if (workDir is null) return [];

        var metaPath = Path.Combine(workDir, "metadata.json");
        if (!File.Exists(metaPath)) return [];

        try
        {
            var json = await File.ReadAllTextAsync(metaPath, ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("latencyBudgets", out var arr)) return [];
            return arr.Deserialize<List<LatencyBudget>>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? [];
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read latency budgets from {Path}", metaPath);
            return [];
        }
    }
}
