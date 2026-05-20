using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Lifecycle;

namespace Tracer.WebApi.Queries;

public sealed class TopologyQueryService(ReadOnlyConnectionPool pool)
{
    private readonly ReadOnlyConnectionPool _pool = pool;

    public Task<IReadOnlyList<NodeInfoDto>> GetNodesAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<NodeInfoDto>>(Array.Empty<NodeInfoDto>());
}
