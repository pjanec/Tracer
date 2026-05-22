namespace Tracer.Storage.Annotations.Schema;

public static class AnnotationsSchema
{
    public const string CreateSql = """
        CREATE TABLE IF NOT EXISTS annotations (
            annotation_id     TEXT PRIMARY KEY,
            session_id        TEXT NOT NULL,
            kind              TEXT NOT NULL,
            event_id          TEXT,
            entity_id         TEXT,
            trace_id          TEXT,
            target_wallclock  TEXT,
            body              TEXT NOT NULL,
            title             TEXT,
            tags_json         TEXT NOT NULL DEFAULT '[]',
            author            TEXT,
            created_at        TEXT NOT NULL,
            modified_at       TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_annotations_session    ON annotations (session_id);
        CREATE INDEX IF NOT EXISTS idx_annotations_event_id   ON annotations (event_id)   WHERE event_id   IS NOT NULL;
        CREATE INDEX IF NOT EXISTS idx_annotations_entity_id  ON annotations (entity_id)  WHERE entity_id  IS NOT NULL;
        CREATE INDEX IF NOT EXISTS idx_annotations_trace_id   ON annotations (trace_id)   WHERE trace_id   IS NOT NULL;
        CREATE INDEX IF NOT EXISTS idx_annotations_created_at ON annotations (created_at);
        """;
}
