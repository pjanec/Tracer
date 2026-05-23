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
        // DuckDB 1.0.2 does not support partial indexes (WHERE clause); use a regular index.
        // The index must cover parent_event_id for ancestor/descendant traversal.
        SchemaV1.CreateIndexes.Should().Contain(
            "idx_events_parent_event_id ON events(parent_event_id)",
            because: "Phase 6 requires an index on parent_event_id for trace tree traversal");
    }

    // ── FIX-A3: slow_state entity_id index ───────────────────────────────────

    [Fact]
    public void CreateIndexes_SlowStateEntityTime_UsesEntityIdColumn()
    {
        SchemaV1.CreateIndexes.Should().Contain(
            "idx_slow_state_entity_time ON slow_state (entity_id, publish_wallclock)",
            because: "TRC-P7-002 requires the index to cover entity_id and publish_wallclock");
    }

    [Fact]
    public void CreateIndexes_SlowStateEntityTime_HasNoWhereClause()
    {
        // DuckDB 1.0.2 does not support partial indexes (WHERE clause)
        // The index covers all rows; nulls are indexed but queries filter them naturally
        var idx = SchemaV1.CreateIndexes;
        var idxLine = idx.Split('\n')
            .FirstOrDefault(l => l.Contains("idx_slow_state_entity_time")) ?? "";
        idxLine.Should().NotContain("WHERE",
            because: "DuckDB 1.0.2 does not support partial indexes — the WHERE clause was removed");
    }

    [Fact]
    public void CreateIndexes_SlowStateEntityTime_DoesNotUseInstanceKey()
    {
        // The old (wrong) index used instance_key instead of entity_id
        SchemaV1.CreateIndexes.Should().NotContain(
            "idx_slow_state_entity_time ON slow_state(instance_key",
            because: "FIX-A3 replaces the instance_key-based index with entity_id-based index");
    }

    [Fact]
    public void CreateSlowStateTable_ContainsEntityIdColumn()
    {
        SchemaV1.CreateSlowStateTable.Should().Contain(
            "entity_id",
            because: "the entity_id column must exist for the partial index to work");
    }

    [Fact]
    public void CreateIndexes_SlowStateEntityTime_IsIdempotent_Sql()
    {
        // Ensure CREATE INDEX IF NOT EXISTS is present (idempotency)
        SchemaV1.CreateIndexes.Should().Contain(
            "CREATE INDEX IF NOT EXISTS idx_slow_state_entity_time",
            because: "idempotent DDL requires IF NOT EXISTS");
    }
}

