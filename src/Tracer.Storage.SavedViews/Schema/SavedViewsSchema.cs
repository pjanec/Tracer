namespace Tracer.Storage.SavedViews.Schema;

public static class SavedViewsSchema
{
    public const string CreateSql = """
        CREATE TABLE IF NOT EXISTS saved_views (
            saved_view_id  TEXT PRIMARY KEY,
            session_id     TEXT NOT NULL,
            kind           TEXT NOT NULL,
            view_type      TEXT NOT NULL,
            url            TEXT NOT NULL,
            label          TEXT NOT NULL,
            description    TEXT,
            persona        TEXT NOT NULL,
            author         TEXT,
            created_at     TEXT NOT NULL,
            last_opened_at TEXT,
            open_count     INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS idx_saved_views_session_persona ON saved_views (session_id, persona);
        CREATE INDEX IF NOT EXISTS idx_saved_views_session_kind    ON saved_views (session_id, kind);
        CREATE INDEX IF NOT EXISTS idx_saved_views_last_opened     ON saved_views (last_opened_at)
        """;
}
