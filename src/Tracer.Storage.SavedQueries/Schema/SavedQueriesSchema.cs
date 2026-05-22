namespace Tracer.Storage.SavedQueries.Schema;

public static class SavedQueriesSchema
{
    public const string CreateSql = """
        CREATE TABLE IF NOT EXISTS saved_queries (
            saved_query_id  TEXT PRIMARY KEY,
            label           TEXT NOT NULL,
            description     TEXT,
            sql_text        TEXT NOT NULL,
            parameters_json TEXT NOT NULL DEFAULT '[]',
            tags_json       TEXT NOT NULL DEFAULT '[]',
            is_built_in     INTEGER NOT NULL DEFAULT 0,
            is_favorite     INTEGER NOT NULL DEFAULT 0,
            author          TEXT,
            created_at      TEXT NOT NULL,
            last_run_at     TEXT,
            run_count       INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS idx_saved_queries_label    ON saved_queries (label);
        CREATE INDEX IF NOT EXISTS idx_saved_queries_favorite ON saved_queries (is_favorite)
        """;
}
