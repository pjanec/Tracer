namespace Tracer.OfflineViewer.WebApi;

public sealed record OpenBundleRequestDto
{
    public required string Path { get; init; }
}

public sealed record OpenBundleResponseDto
{
    public required string BundleId { get; init; }
}

public sealed record CurrentBundleDto
{
    public required string BundleId { get; init; }
    public string? Label { get; init; }
    public required CurrentBundleTimeRange TimeRange { get; init; }
}

public sealed record CurrentBundleTimeRange
{
    public required DateTimeOffset StartUtc { get; init; }
    public required DateTimeOffset EndUtc { get; init; }
}
