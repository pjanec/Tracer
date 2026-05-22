namespace Tracer.WebApi.Contracts.Dto;

public sealed record NetworkTopologyEdgeDto
{
    public required string Topic { get; init; }
    public required string PublisherNode { get; init; }
    public required string SubscriberNode { get; init; }
    public required long MessageCount { get; init; }
    public required DateTimeOffset FirstSeenUtc { get; init; }
    public required DateTimeOffset LastSeenUtc { get; init; }
}

public sealed record NetworkTopologyDto
{
    public required IReadOnlyList<string> Nodes { get; init; }
    public required IReadOnlyList<NetworkTopologyEdgeDto> Edges { get; init; }
}
