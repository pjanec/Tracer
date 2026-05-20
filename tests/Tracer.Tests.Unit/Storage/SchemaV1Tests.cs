using FluentAssertions;
using Tracer.Storage.DuckDB.Schema;
using Xunit;

namespace Tracer.Tests.Unit.Storage;

/// <summary>Unit tests for <see cref="SchemaV1"/> DDL constants.</summary>
public sealed class SchemaV1Tests
{
    [Fact]
    public void CreateIndexes_ContainsPartialIndexOnParentEventId()
    {
        // The Phase 6 partial index must appear with this exact name and clause.
        SchemaV1.CreateIndexes.Should().Contain(
            "idx_events_parent_event_id ON events(parent_event_id)",
            because: "Phase 6 requires an index on parent_event_id for ancestor/descendant traversal");
    }
}
