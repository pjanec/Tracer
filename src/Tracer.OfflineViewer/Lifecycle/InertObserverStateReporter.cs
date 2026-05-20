using Tracer.Observer.Lifecycle;

namespace Tracer.OfflineViewer.Lifecycle;

/// <summary>
/// No-op implementation of ObserverStateReporter for the offline viewer.
/// No events are ever ingested in bundle mode; all counters remain at zero.
/// </summary>
internal sealed class InertObserverStateReporter : ObserverStateReporter
{
    public InertObserverStateReporter() : base(null)
    {
    }
}
