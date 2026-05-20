using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Lifecycle;

namespace Tracer.WebApi.Queries;

public sealed class EventLookupService(ReadOnlyConnectionPool pool)
{
    private readonly ReadOnlyConnectionPool _pool = pool;

    public Task<EventDto?> GetByIdAsync(string eventId, CancellationToken ct)
        => Task.FromResult<EventDto?>(null);
}
