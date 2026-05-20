using Tracer.Core.Domain;
using Tracer.Core.Records;

namespace Tracer.Agent.Ingestion;

public sealed class DropPolicy
{
    /// <summary>
    /// Returns true when the record should be dropped at the given backpressure level.
    /// Sets <paramref name="reason"/> to the appropriate gap reason when dropping.
    /// </summary>
    public bool ShouldDrop(DiagnosticRecord record, BackpressureLevel level, out CaptureGapReason reason)
    {
        reason = default;

        if (level == BackpressureLevel.Healthy)
            return false;

        // Fast-state drops first (at FastStateAtRisk and above)
        if (record is StateSampleRecord { Rate: StateSampleRate.Fast })
        {
            reason = CaptureGapReason.BackpressureFastStateDropped;
            return true;
        }

        // Slow-state drops at SlowStateAtRisk and above
        if (level >= BackpressureLevel.SlowStateAtRisk &&
            record is StateSampleRecord { Rate: StateSampleRate.Slow })
        {
            reason = CaptureGapReason.BackpressureSlowStateDropped;
            return true;
        }

        // Events dropped only at Saturated
        if (level >= BackpressureLevel.Saturated && record is EventRecord)
        {
            reason = CaptureGapReason.BackpressureEventsDropped;
            return true;
        }

        return false;
    }
}
