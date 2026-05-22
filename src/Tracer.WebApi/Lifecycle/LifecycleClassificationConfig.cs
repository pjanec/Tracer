namespace Tracer.WebApi.Lifecycle;

public sealed class LifecycleClassificationConfig
{
    public IReadOnlyList<string> SpawnSuffixes { get; init; }
        = new[] { "spawn", "created", "spawned" };

    public IReadOnlyList<string> OwnershipSuffixes { get; init; }
        = new[] { "ownership_changed", "owner_transferred", "owner_changed" };

    public IReadOnlyList<string> DestructionSuffixes { get; init; }
        = new[] { "destroyed", "killed", "removed", "despawned" };

    public LifecycleRegexPatterns? Regex { get; init; }
}

public sealed record LifecycleRegexPatterns(string? Spawn, string? Ownership, string? Destruction);
