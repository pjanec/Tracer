using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Records;

namespace Tracer.Agent.Ingestion;

public interface IIntervalContext
{
    IDiagnosticStorageWriter? CurrentWriter { get; }
    void NotifyRecordWritten(DiagnosticRecord record);
    void NotifyCaptureGap(CaptureGap gap);
}
