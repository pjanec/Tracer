namespace Tracer.WebApi.Contracts.Dto;

public sealed record LifecycleConfigDto
{
    public required IReadOnlyList<string> SpawnSuffixes { get; init; }
    public required IReadOnlyList<string> OwnershipSuffixes { get; init; }
    public required IReadOnlyList<string> DestructionSuffixes { get; init; }
    public string? SpawnRegex { get; init; }
    public string? OwnershipRegex { get; init; }
    public string? DestructionRegex { get; init; }
}
