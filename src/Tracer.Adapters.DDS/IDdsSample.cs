namespace Tracer.Adapters.DDS;

/// <summary>
/// Abstraction over a DDS sample, isolating Tracer.Core from CycloneDDS types.
/// </summary>
public interface IDdsSample
{
    /// <summary>Timestamp set by dds_write_ts() at publish time.</summary>
    DateTimeOffset SourceTimestamp { get; }

    /// <summary>DDS sequence number (monotonically increasing per writer).</summary>
    ulong SequenceNumber { get; }

    /// <summary>The typed sample payload object (customer's DDS IDL-generated class).</summary>
    object GetPayload();
}
