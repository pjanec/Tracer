using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Lifecycle;

namespace Tracer.WebApi.Queries;

public sealed class SessionQueryService(ReadOnlyConnectionPool pool)
{
    private readonly ReadOnlyConnectionPool _pool = pool;
    public Task<IReadOnlyList<SessionDto>> ListAsync(object? range, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<SessionDto>>(Array.Empty<SessionDto>());

    public Task<SessionDto?> GetAsync(string sessionId, CancellationToken ct)
        => Task.FromResult<SessionDto?>(null);
}
