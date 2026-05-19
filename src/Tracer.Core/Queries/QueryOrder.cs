namespace Tracer.Core.Queries;

/// <summary>
/// Ordering options for event query results.
/// </summary>
public enum QueryOrder
{
    /// <summary>Results ordered by publish wallclock time, oldest first.</summary>
    PublishTimeAscending,

    /// <summary>Results ordered by publish wallclock time, newest first.</summary>
    PublishTimeDescending,

    /// <summary>Results ordered by publisher node then sequence number, ascending.</summary>
    SequenceNumberAscending
}
