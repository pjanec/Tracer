namespace Tracer.Storage.DuckDB.Schema;

/// <summary>
/// DDL constants for Schema Version 1.
/// </summary>
internal static class SchemaV1
{
    /// <summary>The schema version number stored in _schema_meta.</summary>
    public const int Version = 1;

    /// <summary>DDL for the events table.</summary>
    public const string CreateEventsTable = """
        CREATE TABLE IF NOT EXISTS events (
            event_id            UBIGINT NOT NULL,
            trace_id            UBIGINT NOT NULL,
            parent_event_id     UBIGINT,
            sequence_number     UBIGINT NOT NULL,
            publish_wallclock   TIMESTAMP_NS NOT NULL,
            receive_wallclock   TIMESTAMP_NS NOT NULL,
            publisher_node      VARCHAR NOT NULL,
            subscriber_node     VARCHAR NOT NULL,
            topic               VARCHAR NOT NULL,
            entity_id           VARCHAR,
            owning_player_id    VARCHAR,
            scenario_phase      VARCHAR,
            severity            VARCHAR,
            notable_label       VARCHAR,
            payload             JSON NOT NULL
        );
        """;

    /// <summary>DDL for the slow_state table.</summary>
    public const string CreateSlowStateTable = """
        CREATE TABLE IF NOT EXISTS slow_state (
            sequence_number     UBIGINT NOT NULL,
            publish_wallclock   TIMESTAMP_NS NOT NULL,
            receive_wallclock   TIMESTAMP_NS NOT NULL,
            publisher_node      VARCHAR NOT NULL,
            subscriber_node     VARCHAR NOT NULL,
            topic               VARCHAR NOT NULL,
            instance_key        VARCHAR NOT NULL,
            trace_id            UBIGINT,
            payload             JSON NOT NULL
        );
        """;

    /// <summary>DDL for the schema metadata table.</summary>
    public const string CreateSchemaMetaTable = """
        CREATE TABLE IF NOT EXISTS _schema_meta (
            schema_version  INTEGER NOT NULL,
            tracer_version  VARCHAR NOT NULL,
            created_at      TIMESTAMP_NS NOT NULL
        );
        """;

    /// <summary>DDL for all seven indexes.</summary>
    public const string CreateIndexes = """
        CREATE INDEX IF NOT EXISTS idx_events_trace ON events(trace_id);
        CREATE INDEX IF NOT EXISTS idx_events_parent_event_id ON events(parent_event_id);
        CREATE INDEX IF NOT EXISTS idx_events_entity ON events(entity_id);
        CREATE INDEX IF NOT EXISTS idx_events_player ON events(owning_player_id);
        CREATE INDEX IF NOT EXISTS idx_events_topic_time ON events(topic, publish_wallclock);
        CREATE INDEX IF NOT EXISTS idx_state_instance_time ON slow_state(instance_key, publish_wallclock);
        CREATE INDEX IF NOT EXISTS idx_state_topic ON slow_state(topic);
        """;
}
