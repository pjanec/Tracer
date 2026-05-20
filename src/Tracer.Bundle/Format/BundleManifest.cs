using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracer.Bundle.Format;

/// <summary>Root manifest record for a Tracer bundle. Serializes to/from JSON with camelCase names.</summary>
public record BundleManifest
{
    public required string BundleId { get; init; }
    public required int SchemaVersion { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required string TracerVersion { get; init; }
    public required BundleWriterInfo Writer { get; init; }
    public required BundleTimeRange TimeRange { get; init; }
    public required BundleSessionContext SessionContext { get; init; }
    public required IReadOnlyList<string> ParticipatingNodes { get; init; }
    public required string FastStateScope { get; init; }
    public required IReadOnlyList<string> FastStateEntities { get; init; }
    public required BundleStatistics Statistics { get; init; }
    public required IReadOnlyList<BundleFileEntry> Files { get; init; }

    /// <summary>Shared serializer options: camelCase property naming.</summary>
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public record BundleWriterInfo
{
    public required string Tool { get; init; }
    public required string Version { get; init; }
    public required string Host { get; init; }
}

public record BundleTimeRange
{
    public required DateTimeOffset StartUtc { get; init; }
    public required DateTimeOffset EndUtc { get; init; }
}

public record BundleSessionContext
{
    public required string SessionId { get; init; }
    public required string ScenarioId { get; init; }
    public string? Label { get; init; }
}

public record BundleStatistics
{
    public required long TotalEvents { get; init; }
    public required long TotalSlowStateSamples { get; init; }
    public required long TotalFastStateRows { get; init; }
    public required long UncompressedBytes { get; init; }
}

public record BundleFileEntry
{
    public required string Path { get; init; }
    public required long SizeBytes { get; init; }
    public required string Sha256 { get; init; }
}
