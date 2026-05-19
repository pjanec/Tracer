using FluentAssertions;
using Tracer.Core.Records;

namespace Tracer.TestHarness.Assertions;

/// <summary>
/// FluentAssertions-style extension methods for validating
/// <see cref="EventRecord"/> collections.
/// </summary>
public static class EventAssertions
{
    /// <summary>
    /// Asserts that every event shares a single <see cref="EventRecord.TraceId"/>
    /// and that every non-null <see cref="EventRecord.ParentEventId"/> references
    /// an event that is present in the collection.
    /// </summary>
    public static void ShouldFormValidTrace(this IEnumerable<EventRecord> events)
    {
        var list = events.ToList();
        list.Should().NotBeEmpty("a valid trace must contain at least one event");

        list.Select(e => e.TraceId)
            .Distinct()
            .Should().HaveCount(1, "all events in a trace must share one trace_id");

        var eventIds = list.Select(e => e.EventId).ToHashSet();
        foreach (var e in list)
        {
            if (e.ParentEventId is { } parent)
                eventIds.Should().Contain(parent,
                    $"event {e.EventId} has parent_event_id {parent} which is missing from the trace");
        }
    }

    /// <summary>
    /// Asserts that the <see cref="DiagnosticRecord.PublishWallclock"/> values of
    /// consecutive events are non-decreasing.
    /// </summary>
    public static void ShouldBeTimeOrdered(this IEnumerable<EventRecord> events)
    {
        var list = events.ToList();
        for (int i = 1; i < list.Count; i++)
        {
            list[i].PublishWallclock.NanosecondsSinceEpoch
                .Should().BeGreaterThanOrEqualTo(
                    list[i - 1].PublishWallclock.NanosecondsSinceEpoch,
                    $"event at index {i} must not precede event at index {i - 1}");
        }
    }
}
