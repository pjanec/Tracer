using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Lifecycle;

namespace Tracer.WebApi.Queries;

public sealed class ScenarioQueryService(ReadOnlyConnectionPool pool)
{
    private readonly ReadOnlyConnectionPool _pool = pool;

    public Task<IReadOnlyList<ScenarioPhaseDto>> GetPhasesAsync(string sessionId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ScenarioPhaseDto>>(Array.Empty<ScenarioPhaseDto>());

    public Task<IReadOnlyList<NotableEventDto>> GetNotablesAsync(string sessionId, int limit, DateTimeOffset? before, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<NotableEventDto>>(Array.Empty<NotableEventDto>());

    public Task<ScenarioStateDto?> GetCurrentStateAsync(string sessionId, CancellationToken ct)
        => Task.FromResult<ScenarioStateDto?>(null);
}
