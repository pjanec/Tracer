using Tracer.Core.Identity;
using Tracer.Core.Queries;
using Tracer.Core.Records;

namespace Tracer.Core.Abstractions;

/// <summary>
/// Reads diagnostic records from storage. Query-oriented.
/// </summary>
public interface IDiagnosticStorageReader : IAsyncDisposable
{
    /// <summary>Queries events matching the given query specification.</summary>
    Task<IReadOnlyList<EventRecord>> QueryEventsAsync(EventQuery query, CancellationToken ct);

    /// <summary>Returns the event with the given ID, or null if not found.</summary>
    Task<EventRecord?> GetEventAsync(EventId eventId, CancellationToken ct);

    /// <summary>Returns the count of events matching the given filter.</summary>
    Task<long> CountEventsAsync(EventFilter filter, CancellationToken ct);
}
