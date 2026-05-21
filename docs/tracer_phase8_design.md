# Tracer Phase 8 — Detailed Design
## Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish

*Companion to `tracer_architecture_v1.md` and `tracer_phase1_design.md` through `tracer_phase7_design.md`*
*Phase 8 of the build sequence (architecture §18)*
*C# / .NET 8 backend · Vue 3 / TypeScript frontend · May 2026*

*Phase 8 is the "make it stick" phase. Phases 1-7 built the diagnostic toolkit; Phase 8 makes it persistent. Engineers can now leave notes on events. They can save complex filter combinations under a name. They can bookmark a viewport for later. And scenario authors get their own first-class view: the trigger evaluation log.*

*Phase 8 also closes the gap between the engineer persona (timeline, causal tree, entity history) and the scenario-author persona (scenario view, trigger log). Both communities now have dedicated views and persistent affordances.*

*Architecturally, Phase 8 introduces the first user-written data into the system. Until now, everything Tracer stored was derived from the simulation. Now Tracer stores user content too — annotations, saved views — and that content must survive bundle export, live-session-to-bundle migration, and the offline viewer round-trip.*

---

## 1. Phase 8 Scope and Goals

### 1.1 What Phase 8 Delivers

**Annotations**
- Backend annotation store, separate from the event store. SQLite for the live Observer; a JSON file in the bundle for offline.
- REST API: list, create, update, delete annotations
- Annotations attach to one of: an event, an entity, a trace, or a free-floating time point
- Vue UI in `EventInspector` and all primary views to view and create annotations
- Annotations show as a subtle marker in Timeline (Phase 5), Causal Tree (Phase 6), Entity History (Phase 7), and Scenario View (Phase 3)
- Bundle export includes any annotations made against the live session

**Saved Views**
- A "Save this view" affordance in the toolbar of Timeline, Causal Tree, and Entity History
- Backend store for saved views (URL template + label + description + persona tag)
- `SavedViewsView.vue` listing all saved views, filterable by persona and view type
- Click → opens the view at the saved URL state

**Bookmarks** (lightweight saved views)
- One-click bookmark from the toolbar — the URL gets saved with auto-generated label
- A `BookmarkBar` component that shows recent bookmarks per view
- Bookmarks are a special-case saved view (`kind: 'bookmark'` in the data model)

**Trigger Evaluation Log**
- A scenario-author-facing view at `/v/triggers/{sessionId}`
- Lists all `scenario.trigger_evaluated` events (a domain convention) with their inputs and results
- Filterable by trigger ID, result, time range
- Click a trigger row → see what fired next (causally — uses Phase 6's machinery)
- Helps scenario authors debug "why didn't this trigger fire?" / "why did this fire when it shouldn't have?"

**Lifecycle Topic Configuration** (Phase 7 carryover)
- Phase 7's hardcoded lifecycle topic patterns (`*.spawn`, `*.created`, etc.) become configurable via the site config
- Per-deployment override of what counts as a spawn/ownership/destruction event

**Multi-Persona Polish**
- A persona switcher in the app header: "Engineer", "Scenario Author", "Operator". Each persona's session browser pre-filters to that persona's preferred views and views' saved bookmarks. Persona is stored in localStorage; defaults to "Engineer".
- Per-persona default view: when a session card is clicked, route to the persona's primary view rather than always to Scenario View. Engineer → Timeline; Scenario Author → Scenario View; Operator → Scenario View.

### 1.2 What Phase 8 Does NOT Deliver

- **No replication latency or stats** (Phase 9)
- **No SQL console** (Phase 10)
- **No annotation editing history** — annotations are mutable but no audit log
- **No collaborative annotation** — single-user model; the system has no notion of "who" wrote an annotation. (The data model has an `author` field, set from a `Settings`-stored display name; no authentication, no enforcement.)
- **No annotation comments / threading** — one note per annotation
- **No annotation export to external systems** — Slack/Jira/etc. integration is out of scope
- **No trigger replay or simulation** — the log shows what happened; it doesn't let you re-run a trigger with different inputs
- **No automated anomaly detection** — Phase 8 surfaces data, doesn't analyze it

### 1.3 Success Criteria

1. **Create an annotation from any view**: from Timeline event marker click → inspector → "Add note", the annotation persists. Reload the page; the annotation is still there as a small indicator on the marker.
2. **Annotations survive bundle export**: build a bundle from a live session with annotations. Open in offline viewer. Annotations appear on the same events.
3. **Save a view**: from Timeline with filters set, click "Save this view", give it a label "Network errors during engagement". Navigate away. Return via the saved-views list. The view restores with the same filters and viewport.
4. **Bookmark a view**: one-click bookmark from any primary view. The bookmark bar shows it. Click → restored.
5. **Trigger evaluation log**: open `/v/triggers/{sessionId}`. The log shows all trigger evaluations. Click a row → see the events fired as a result.
6. **Persona switcher**: switch from Engineer to Scenario Author. Session browser's default click target switches accordingly.
7. **Performance**: annotation list query under 100 ms; saved-view list query under 50 ms; trigger log query under 300 ms for a session with 5000 evaluations.
8. **All Phase 1-7 tests pass**.

### 1.4 Estimated Duration

Two calendar weeks. Phase 8 work is wide but mostly shallow — many small UI affordances and modest backend additions. Distribution:
- Week 1: annotations (backend store, API, integration into existing views) + saved views (backend store, API, basic UI)
- Week 2: trigger evaluation log + persona switcher + bookmark bar + polish

---

## 2. Project Layout Additions

Building on Phase 7:

```
tracer/
  src/
    Tracer.Core/                                  (unchanged)
    Tracer.Storage.DuckDB/                        (unchanged)
    Tracer.Storage.Annotations/                   NEW assembly
      Tracer.Storage.Annotations.csproj
      AnnotationStore.cs                          interface — abstracts SQLite vs bundle JSON
      SqliteAnnotationStore.cs                    live Observer mode
      BundleAnnotationStore.cs                    offline mode — reads/writes annotations.json
      AnnotationRecord.cs
      AnnotationKind.cs
      Schema/
        AnnotationsSchema.cs                      SQLite schema for live mode
    Tracer.Storage.SavedViews/                    NEW assembly
      Tracer.Storage.SavedViews.csproj
      SavedViewStore.cs                           interface
      SqliteSavedViewStore.cs                     uses the same SQLite DB as annotations
      SavedViewRecord.cs
    Tracer.Aggregator/                            (additions for bundle annotation export)
      Consolidation/
        AnnotationsExporter.cs                    NEW — writes annotations.json into bundle
    Tracer.WebApi/
      Endpoints/
        AnnotationEndpoints.cs                    NEW
        SavedViewEndpoints.cs                     NEW
        TriggerEvalEndpoints.cs                   NEW
      Queries/
        TriggerEvalService.cs                     NEW
      Contracts/Dto/
        AnnotationDto.cs
        SavedViewDto.cs
        TriggerEvaluationDto.cs
  tracer-viewer/
    src/
      views/
        SavedViewsView.vue                        NEW
        TriggerEvalView.vue                       NEW
      components/
        AnnotationMarker.vue                      NEW — small badge for views to overlay
        AnnotationEditor.vue                      NEW — modal/popover for editing
        AnnotationList.vue                        NEW — sidebar list inside views
        SaveViewButton.vue                        NEW — in toolbar of primary views
        BookmarkBar.vue                           NEW — recent bookmarks per view
        PersonaSwitcher.vue                       NEW — in AppHeader
        TriggerEvalRow.vue                        NEW
      composables/
        useAnnotations.ts                         NEW — query + create + update
        useSavedViews.ts                          NEW
        usePersona.ts                             NEW
        useBookmarks.ts                           NEW
      stores/
        annotationStore.ts                        NEW
        savedViewStore.ts                         NEW
        personaStore.ts                           NEW
      types/
        annotation.ts                             NEW
        savedView.ts                              NEW
        persona.ts                                NEW
  tests/
    Tracer.Tests.Unit/
      Annotations/
        SqliteAnnotationStoreTests.cs
        BundleAnnotationStoreTests.cs
      SavedViews/
        SqliteSavedViewStoreTests.cs
      WebApi/
        AnnotationEndpointsTests.cs
        SavedViewEndpointsTests.cs
        TriggerEvalServiceTests.cs
        TriggerEvalEndpointsTests.cs
      Aggregator/
        AnnotationsExporterTests.cs
    Tracer.Tests.Integration/
      AnnotationsRoundTripTests.cs                live → bundle → offline viewer
      SavedViewsRoundTripTests.cs                 live → bundle → offline viewer (read-only in bundle)
      TriggerEvalIntegrationTests.cs
  tracer-viewer/tests/
    unit/
      annotationStore.spec.ts
      useAnnotations.spec.ts
      usePersona.spec.ts
      useBookmarks.spec.ts
    e2e/
      annotations-flow.spec.ts
      saved-views-flow.spec.ts
      persona-switcher.spec.ts
```

### 2.1 Dependencies

`Microsoft.Data.Sqlite` is added for the live annotation/saved-view store. Already a dependency in the .NET 8 ecosystem; small footprint.

```xml
<PackageVersion Include="Microsoft.Data.Sqlite" Version="8.0.0" />
```

No new frontend packages.

---

## 3. Annotations: Data Model and Storage

### 3.1 What an Annotation Is

An annotation is **a piece of user-authored text attached to a target in the data**. Phase 8 targets:

| Target kind | Identified by | Use case |
|---|---|---|
| `event` | event_id | "This is the event that should have triggered a respawn — but didn't." |
| `entity` | entity_id | "Vehicle 17 had erratic ownership in this session." |
| `trace` | trace_id | "This entire engagement chain shows the cascade-failure pattern." |
| `time-point` | sessionId + wallclock_utc | "This is when the demo lost video. No specific event captures it." |

The target's identifier(s) and the annotation's metadata are stored together. The annotation body is plain text (with optional markdown). No attachments, no image upload.

### 3.2 AnnotationRecord

```csharp
namespace Tracer.Storage.Annotations;

public sealed record AnnotationRecord
{
    /// <summary>Globally unique. ULID — same scheme as Phase 4 bundle IDs.</summary>
    public required string AnnotationId { get; init; }
    
    public required string SessionId { get; init; }
    public required AnnotationKind Kind { get; init; }
    
    // Target identifiers — exactly one set populated per kind
    public string? EventId { get; init; }           // hex; null unless kind=Event
    public string? EntityId { get; init; }          // null unless kind=Entity
    public string? TraceId { get; init; }           // hex; null unless kind=Trace
    public DateTimeOffset? TargetWallclock { get; init; }  // null unless kind=TimePoint
    
    public required string Body { get; init; }
    public string? Title { get; init; }             // optional short header
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    
    public string? Author { get; init; }            // free-form display name from Settings
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? ModifiedAtUtc { get; init; }
}

public enum AnnotationKind { Event, Entity, Trace, TimePoint }
```

### 3.3 The AnnotationStore Interface

The storage layer is abstracted so the live Observer and the offline viewer use the same code path with different backends.

```csharp
namespace Tracer.Storage.Annotations;

public interface IAnnotationStore
{
    /// <summary>Read annotations matching the filter.</summary>
    Task<IReadOnlyList<AnnotationRecord>> ListAsync(AnnotationFilter filter, CancellationToken ct);
    
    /// <summary>Read a single annotation by ID. Returns null if not found.</summary>
    Task<AnnotationRecord?> GetAsync(string annotationId, CancellationToken ct);
    
    /// <summary>Insert. The store assigns AnnotationId, CreatedAtUtc, ModifiedAtUtc if absent.</summary>
    Task<AnnotationRecord> CreateAsync(AnnotationRecord record, CancellationToken ct);
    
    /// <summary>Replace. Returns null if not found. Updates ModifiedAtUtc.</summary>
    Task<AnnotationRecord?> UpdateAsync(AnnotationRecord record, CancellationToken ct);
    
    /// <summary>Delete. Returns true if removed, false if not found.</summary>
    Task<bool> DeleteAsync(string annotationId, CancellationToken ct);
    
    /// <summary>Bulk export — used by the aggregator for bundle build.</summary>
    Task<IReadOnlyList<AnnotationRecord>> ExportAllForSessionAsync(string sessionId, CancellationToken ct);
}

public sealed record AnnotationFilter
{
    public string? SessionId { get; init; }
    public AnnotationKind? Kind { get; init; }
    public string? EventId { get; init; }
    public string? EntityId { get; init; }
    public string? TraceId { get; init; }
    public DateTimeOffset? FromUtc { get; init; }
    public DateTimeOffset? ToUtc { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public int Limit { get; init; } = 500;
}
```

### 3.4 SqliteAnnotationStore — Live Observer

The Observer keeps annotations in a single SQLite database file separate from the DuckDB intervals. Why SQLite for this:

- **Annotations are tiny** (text + IDs); SQLite is perfect for that.
- **Annotations live across interval rotations** — they're not tied to a specific interval. A SQLite file outside the interval directory structure makes that natural.
- **Annotations are written from the Web API** (user actions), not from the ingestion pipeline. Different consistency requirements; different concurrency profile.
- **SQLite is a standard library dependency**; no new heavy runtime piece.

```csharp
namespace Tracer.Storage.Annotations;

public sealed class SqliteAnnotationStore : IAnnotationStore
{
    private readonly string _dbPath;
    private readonly ILogger<SqliteAnnotationStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SqliteAnnotationStore(string dbPath, ILogger<SqliteAnnotationStore> logger)
    {
        _dbPath = dbPath;
        _logger = logger;
    }
    
    public async Task InitializeAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        await using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = AnnotationsSchema.CreateSql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<AnnotationRecord>> ListAsync(AnnotationFilter filter, CancellationToken ct)
    {
        await using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        await conn.OpenAsync(ct);
        
        var (sql, parameters) = BuildSelectSql(filter);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in parameters)
            cmd.Parameters.AddWithValue(k, v ?? (object)DBNull.Value);
        
        var results = new List<AnnotationRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(MapRecord(reader));
        return results;
    }

    public async Task<AnnotationRecord?> GetAsync(string annotationId, CancellationToken ct)
    {
        var list = await ListAsync(new AnnotationFilter { Limit = 1 }, ct);
        // Simpler: a direct query
        await using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM annotations WHERE annotation_id = $id";
        cmd.Parameters.AddWithValue("$id", annotationId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapRecord(reader) : null;
    }

    public async Task<AnnotationRecord> CreateAsync(AnnotationRecord record, CancellationToken ct)
    {
        var withDefaults = record with
        {
            AnnotationId = string.IsNullOrEmpty(record.AnnotationId)
                ? Ulid.NewUlid().ToString()
                : record.AnnotationId,
            CreatedAtUtc = record.CreatedAtUtc == default ? DateTimeOffset.UtcNow : record.CreatedAtUtc,
        };
        
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO annotations (
                    annotation_id, session_id, kind, event_id, entity_id, trace_id,
                    target_wallclock, body, title, tags_json, author, created_at, modified_at)
                VALUES (
                    $aid, $sid, $kind, $eid, $entid, $tid,
                    $tw, $body, $title, $tags, $author, $created, $modified);
                """;
            BindRecordParameters(cmd, withDefaults);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _writeLock.Release(); }
        return withDefaults;
    }

    public async Task<AnnotationRecord?> UpdateAsync(AnnotationRecord record, CancellationToken ct)
    {
        var modified = record with { ModifiedAtUtc = DateTimeOffset.UtcNow };
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE annotations SET
                    session_id = $sid, kind = $kind, event_id = $eid, entity_id = $entid,
                    trace_id = $tid, target_wallclock = $tw,
                    body = $body, title = $title, tags_json = $tags,
                    author = $author, modified_at = $modified
                WHERE annotation_id = $aid;
                """;
            BindRecordParameters(cmd, modified);
            var affected = await cmd.ExecuteNonQueryAsync(ct);
            return affected > 0 ? modified : null;
        }
        finally { _writeLock.Release(); }
    }

    public async Task<bool> DeleteAsync(string annotationId, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM annotations WHERE annotation_id = $id";
            cmd.Parameters.AddWithValue("$id", annotationId);
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }
        finally { _writeLock.Release(); }
    }

    public async Task<IReadOnlyList<AnnotationRecord>> ExportAllForSessionAsync(
        string sessionId, CancellationToken ct)
    {
        return await ListAsync(new AnnotationFilter { SessionId = sessionId, Limit = 100_000 }, ct);
    }

    private static (string Sql, IReadOnlyList<(string, object?)> Parameters) BuildSelectSql(
        AnnotationFilter filter)
    {
        var clauses = new List<string>();
        var ps = new List<(string, object?)>();
        if (filter.SessionId is not null)  { clauses.Add("session_id = $sid"); ps.Add(("$sid", filter.SessionId)); }
        if (filter.Kind is { } k)          { clauses.Add("kind = $kind"); ps.Add(("$kind", k.ToString())); }
        if (filter.EventId is not null)    { clauses.Add("event_id = $eid"); ps.Add(("$eid", filter.EventId)); }
        if (filter.EntityId is not null)   { clauses.Add("entity_id = $entid"); ps.Add(("$entid", filter.EntityId)); }
        if (filter.TraceId is not null)    { clauses.Add("trace_id = $tid"); ps.Add(("$tid", filter.TraceId)); }
        if (filter.FromUtc is { } from)    { clauses.Add("created_at >= $from"); ps.Add(("$from", from.ToString("O"))); }
        if (filter.ToUtc is { } to)        { clauses.Add("created_at < $to"); ps.Add(("$to", to.ToString("O"))); }
        
        var where = clauses.Count == 0 ? "" : "WHERE " + string.Join(" AND ", clauses);
        var sql = $"SELECT * FROM annotations {where} ORDER BY created_at DESC LIMIT $limit;";
        ps.Add(("$limit", filter.Limit));
        return (sql, ps);
    }

    private static void BindRecordParameters(SqliteCommand cmd, AnnotationRecord r)
    {
        cmd.Parameters.AddWithValue("$aid", r.AnnotationId);
        cmd.Parameters.AddWithValue("$sid", r.SessionId);
        cmd.Parameters.AddWithValue("$kind", r.Kind.ToString());
        cmd.Parameters.AddWithValue("$eid", (object?)r.EventId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$entid", (object?)r.EntityId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tid", (object?)r.TraceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tw", r.TargetWallclock?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$body", r.Body);
        cmd.Parameters.AddWithValue("$title", (object?)r.Title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(r.Tags));
        cmd.Parameters.AddWithValue("$author", (object?)r.Author ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", r.CreatedAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$modified", r.ModifiedAtUtc?.ToString("O") ?? (object)DBNull.Value);
    }

    private static AnnotationRecord MapRecord(DbDataReader r)
    {
        return new AnnotationRecord
        {
            AnnotationId = r.GetString(r.GetOrdinal("annotation_id")),
            SessionId    = r.GetString(r.GetOrdinal("session_id")),
            Kind         = Enum.Parse<AnnotationKind>(r.GetString(r.GetOrdinal("kind"))),
            EventId      = r.IsDBNull(r.GetOrdinal("event_id"))         ? null : r.GetString(r.GetOrdinal("event_id")),
            EntityId     = r.IsDBNull(r.GetOrdinal("entity_id"))        ? null : r.GetString(r.GetOrdinal("entity_id")),
            TraceId      = r.IsDBNull(r.GetOrdinal("trace_id"))         ? null : r.GetString(r.GetOrdinal("trace_id")),
            TargetWallclock = r.IsDBNull(r.GetOrdinal("target_wallclock"))
                ? (DateTimeOffset?)null
                : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("target_wallclock"))),
            Body  = r.GetString(r.GetOrdinal("body")),
            Title = r.IsDBNull(r.GetOrdinal("title")) ? null : r.GetString(r.GetOrdinal("title")),
            Tags  = JsonSerializer.Deserialize<List<string>>(r.GetString(r.GetOrdinal("tags_json"))) ?? new(),
            Author = r.IsDBNull(r.GetOrdinal("author")) ? null : r.GetString(r.GetOrdinal("author")),
            CreatedAtUtc = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("created_at"))),
            ModifiedAtUtc = r.IsDBNull(r.GetOrdinal("modified_at"))
                ? (DateTimeOffset?)null
                : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("modified_at"))),
        };
    }
}
```

### 3.5 AnnotationsSchema

```csharp
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
```

### 3.6 BundleAnnotationStore — Offline Viewer

In bundle mode, annotations live in a JSON file inside the bundle (Phase 4 §3.1 reserved `annotations/` for this). The file is read-only in Phase 8 — the offline viewer **cannot** edit annotations. Rationale: bundles are immutable artifacts; if the engineer wants to add notes in offline mode, that's a future capability (Phase 8.5+).

```csharp
namespace Tracer.Storage.Annotations;

public sealed class BundleAnnotationStore : IAnnotationStore
{
    private readonly string _bundleAnnotationsPath;
    private IReadOnlyList<AnnotationRecord>? _cache;

    public BundleAnnotationStore(string bundlePath)
    {
        _bundleAnnotationsPath = Path.Combine(bundlePath, "annotations", "annotations.json");
    }

    private async Task<IReadOnlyList<AnnotationRecord>> LoadAsync(CancellationToken ct)
    {
        if (_cache is not null) return _cache;
        if (!File.Exists(_bundleAnnotationsPath))
        {
            _cache = Array.Empty<AnnotationRecord>();
            return _cache;
        }
        await using var stream = File.OpenRead(_bundleAnnotationsPath);
        var list = await JsonSerializer.DeserializeAsync<List<AnnotationRecord>>(stream, ApiJsonSettings.Default, ct);
        _cache = list ?? new List<AnnotationRecord>();
        return _cache;
    }

    public async Task<IReadOnlyList<AnnotationRecord>> ListAsync(AnnotationFilter filter, CancellationToken ct)
    {
        var all = await LoadAsync(ct);
        IEnumerable<AnnotationRecord> q = all;
        if (filter.SessionId is not null)   q = q.Where(a => a.SessionId == filter.SessionId);
        if (filter.Kind is { } k)           q = q.Where(a => a.Kind == k);
        if (filter.EventId is not null)     q = q.Where(a => a.EventId == filter.EventId);
        if (filter.EntityId is not null)    q = q.Where(a => a.EntityId == filter.EntityId);
        if (filter.TraceId is not null)     q = q.Where(a => a.TraceId == filter.TraceId);
        if (filter.FromUtc is { } from)     q = q.Where(a => a.CreatedAtUtc >= from);
        if (filter.ToUtc is { } to)         q = q.Where(a => a.CreatedAtUtc < to);
        return q.OrderByDescending(a => a.CreatedAtUtc).Take(filter.Limit).ToList();
    }

    public async Task<AnnotationRecord?> GetAsync(string annotationId, CancellationToken ct)
    {
        var all = await LoadAsync(ct);
        return all.FirstOrDefault(a => a.AnnotationId == annotationId);
    }

    public Task<AnnotationRecord> CreateAsync(AnnotationRecord record, CancellationToken ct)
        => throw new InvalidOperationException("Bundle annotations are read-only");

    public Task<AnnotationRecord?> UpdateAsync(AnnotationRecord record, CancellationToken ct)
        => throw new InvalidOperationException("Bundle annotations are read-only");

    public Task<bool> DeleteAsync(string annotationId, CancellationToken ct)
        => throw new InvalidOperationException("Bundle annotations are read-only");

    public async Task<IReadOnlyList<AnnotationRecord>> ExportAllForSessionAsync(
        string sessionId, CancellationToken ct)
        => (await LoadAsync(ct)).Where(a => a.SessionId == sessionId).ToList();
}
```

The Web API translates `InvalidOperationException` from bundle-mode write attempts into a 405 Method Not Allowed response with a clear error message.

### 3.7 AnnotationsExporter — Live → Bundle

When the aggregator builds a bundle from live data, it must also export any annotations made against that session into the bundle.

```csharp
namespace Tracer.Aggregator.Consolidation;

public static class AnnotationsExporter
{
    public static async Task ExportAsync(
        IAnnotationStore liveStore,
        string sessionId,
        string bundleStagingPath,
        CancellationToken ct)
    {
        var annotations = await liveStore.ExportAllForSessionAsync(sessionId, ct);
        if (annotations.Count == 0) return;
        
        var annotationsDir = Path.Combine(bundleStagingPath, "annotations");
        Directory.CreateDirectory(annotationsDir);
        var path = Path.Combine(annotationsDir, "annotations.json");
        
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, annotations,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            }, ct);
    }
}
```

Wired into Phase 4's `AggregationOrchestrator.RunAsync` after step 7 (metadata writing) and before step 8 (manifest computation, so the manifest's checksums cover `annotations/annotations.json`).

```csharp
// In AggregationOrchestrator.RunAsync, between steps 7 and 8:
if (_annotationStore is not null)  // optional dependency
{
    await AnnotationsExporter.ExportAsync(
        _annotationStore, request.SessionId ?? "", staging.BundleStagingPath, ct);
    progress?.Report(AggregationStage.AnnotationsExported,
        "Annotations exported into bundle");
}
```

`AnnotationStage.AnnotationsExported` is a new enum entry. Order of progress matters — annotations are written before the manifest is built so they get checksum-covered.

---

## 4. Annotation Web API

### 4.1 Endpoints

```
GET    /api/annotations                          list annotations with filters
POST   /api/annotations                          create new annotation
GET    /api/annotations/{annotationId}           single annotation
PUT    /api/annotations/{annotationId}           replace existing
DELETE /api/annotations/{annotationId}           remove
```

Filter parameters (on the GET list):

| Parameter | Meaning |
|---|---|
| `sessionId` | required for list |
| `kind` | `event` / `entity` / `trace` / `time-point` |
| `eventId` | hex |
| `entityId` | string |
| `traceId` | hex |
| `from`, `to` | ISO 8601 — filter on `createdAtUtc` |
| `tag` | repeatable; matches if annotation has any of these tags |
| `limit` | max results (default 500, max 5000) |

### 4.2 AnnotationEndpoints

```csharp
namespace Tracer.WebApi.Endpoints;

public static class AnnotationEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/annotations",                       HandleListAsync).WithOpenApi();
        app.MapPost("/api/annotations",                      HandleCreateAsync).WithOpenApi();
        app.MapGet("/api/annotations/{annotationId}",        HandleGetAsync).WithOpenApi();
        app.MapPut("/api/annotations/{annotationId}",        HandleUpdateAsync).WithOpenApi();
        app.MapDelete("/api/annotations/{annotationId}",     HandleDeleteAsync).WithOpenApi();
    }

    public static async Task<Ok<IReadOnlyList<AnnotationDto>>> HandleListAsync(
        [FromQuery] string sessionId,
        [FromQuery] string? kind,
        [FromQuery] string? eventId,
        [FromQuery] string? entityId,
        [FromQuery] string? traceId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string[]? tag,
        [FromQuery] int limit = 500,
        [FromServices] IAnnotationStore store = default!,
        CancellationToken ct = default)
    {
        var filter = new AnnotationFilter
        {
            SessionId = sessionId,
            Kind = kind is not null && Enum.TryParse<AnnotationKind>(kind, true, out var k) ? k : null,
            EventId = eventId, EntityId = entityId, TraceId = traceId,
            FromUtc = from, ToUtc = to,
            Tags = tag, Limit = Math.Clamp(limit, 1, 5000)
        };
        var results = await store.ListAsync(filter, ct);
        return TypedResults.Ok((IReadOnlyList<AnnotationDto>)results.Select(AnnotationDtoMapper.Map).ToList());
    }

    public static async Task<Results<Ok<AnnotationDto>, NotFound>> HandleGetAsync(
        string annotationId,
        [FromServices] IAnnotationStore store,
        CancellationToken ct)
    {
        var r = await store.GetAsync(annotationId, ct);
        return r is null ? TypedResults.NotFound() : TypedResults.Ok(AnnotationDtoMapper.Map(r));
    }

    public static async Task<Results<Created<AnnotationDto>, ProblemHttpResult>> HandleCreateAsync(
        [FromBody] CreateAnnotationDto dto,
        [FromServices] IAnnotationStore store,
        CancellationToken ct)
    {
        try
        {
            var validation = ValidateCreate(dto);
            if (validation is { } error) return TypedResults.Problem(error);
            
            var record = AnnotationDtoMapper.FromCreate(dto);
            var created = await store.CreateAsync(record, ct);
            return TypedResults.Created(
                $"/api/annotations/{created.AnnotationId}",
                AnnotationDtoMapper.Map(created));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("read-only"))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Bundle annotations are read-only",
                Detail = "Open the live observer to create annotations.",
                Status = StatusCodes.Status405MethodNotAllowed
            });
        }
    }

    public static async Task<Results<Ok<AnnotationDto>, NotFound, ProblemHttpResult>> HandleUpdateAsync(
        string annotationId,
        [FromBody] UpdateAnnotationDto dto,
        [FromServices] IAnnotationStore store,
        CancellationToken ct)
    {
        try
        {
            var existing = await store.GetAsync(annotationId, ct);
            if (existing is null) return TypedResults.NotFound();
            var updated = existing with
            {
                Body  = dto.Body  ?? existing.Body,
                Title = dto.Title ?? existing.Title,
                Tags  = dto.Tags  ?? existing.Tags,
                Author = dto.Author ?? existing.Author
            };
            var result = await store.UpdateAsync(updated, ct);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(AnnotationDtoMapper.Map(result));
        }
        catch (InvalidOperationException)
        {
            return TypedResults.Problem(new ProblemDetails {
                Title = "Bundle annotations are read-only",
                Status = StatusCodes.Status405MethodNotAllowed });
        }
    }

    public static async Task<Results<NoContent, NotFound, ProblemHttpResult>> HandleDeleteAsync(
        string annotationId,
        [FromServices] IAnnotationStore store,
        CancellationToken ct)
    {
        try
        {
            return await store.DeleteAsync(annotationId, ct)
                ? TypedResults.NoContent()
                : TypedResults.NotFound();
        }
        catch (InvalidOperationException)
        {
            return TypedResults.Problem(new ProblemDetails {
                Title = "Bundle annotations are read-only",
                Status = StatusCodes.Status405MethodNotAllowed });
        }
    }

    private static ProblemDetails? ValidateCreate(CreateAnnotationDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Body))
            return new ProblemDetails { Title = "Body required", Status = 400 };
        if (string.IsNullOrWhiteSpace(dto.SessionId))
            return new ProblemDetails { Title = "SessionId required", Status = 400 };
        // Exactly one of EventId / EntityId / TraceId / TargetWallclock per kind
        var targetCount =
            (dto.EventId is null ? 0 : 1) +
            (dto.EntityId is null ? 0 : 1) +
            (dto.TraceId is null ? 0 : 1) +
            (dto.TargetWallclockUtc is null ? 0 : 1);
        if (targetCount != 1)
            return new ProblemDetails {
                Title = "Exactly one target required (eventId, entityId, traceId, or targetWallclockUtc)",
                Status = 400 };
        return null;
    }
}
```

### 4.3 DTOs

```csharp
namespace Tracer.WebApi.Contracts.Dto;

public sealed record AnnotationDto
{
    public required string AnnotationId { get; init; }
    public required string SessionId { get; init; }
    public required string Kind { get; init; }   // "Event" | "Entity" | "Trace" | "TimePoint"
    public string? EventId { get; init; }
    public string? EntityId { get; init; }
    public string? TraceId { get; init; }
    public DateTimeOffset? TargetWallclockUtc { get; init; }
    public required string Body { get; init; }
    public string? Title { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public string? Author { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? ModifiedAtUtc { get; init; }
}

public sealed record CreateAnnotationDto
{
    public required string SessionId { get; init; }
    public required string Kind { get; init; }
    public string? EventId { get; init; }
    public string? EntityId { get; init; }
    public string? TraceId { get; init; }
    public DateTimeOffset? TargetWallclockUtc { get; init; }
    public required string Body { get; init; }
    public string? Title { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public string? Author { get; init; }
}

public sealed record UpdateAnnotationDto
{
    public string? Body { get; init; }
    public string? Title { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public string? Author { get; init; }
}
```

### 4.4 Wiring

In `ObserverHostBuilder`:

```csharp
// Live mode: SQLite store at {DataRoot}/annotations.db
builder.Services.AddSingleton<IAnnotationStore>(sp =>
{
    var config = sp.GetRequiredService<ObserverConfig>();
    var path = Path.Combine(config.DataRoot, "annotations.db");
    var store = new SqliteAnnotationStore(path,
        sp.GetRequiredService<ILogger<SqliteAnnotationStore>>());
    store.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
    return store;
});
```

In `OfflineViewerHostBuilder`:

```csharp
// Offline mode: bundle annotations store, read-only
builder.Services.AddSingleton<IAnnotationStore>(sp =>
{
    var bundleMgr = sp.GetRequiredService<BundleOpenManager>();
    return new LazyBundleAnnotationStore(bundleMgr);
});
```

`LazyBundleAnnotationStore` is a small adapter that defers to a `BundleAnnotationStore` whose path is updated whenever the offline viewer opens a different bundle. (The bundle path changes; the store implementation has to follow it.)

```csharp
public sealed class LazyBundleAnnotationStore : IAnnotationStore
{
    private readonly BundleOpenManager _mgr;
    public LazyBundleAnnotationStore(BundleOpenManager mgr) { _mgr = mgr; }

    private IAnnotationStore? Resolve()
    {
        var current = _mgr.Current;
        return current is null ? null : new BundleAnnotationStore(current.WorkingDirectory);
    }

    public Task<IReadOnlyList<AnnotationRecord>> ListAsync(AnnotationFilter filter, CancellationToken ct)
        => Resolve() is { } s ? s.ListAsync(filter, ct) : Task.FromResult<IReadOnlyList<AnnotationRecord>>(Array.Empty<AnnotationRecord>());
    
    // ... similar for the other methods, with read-only throws ...
}
```

In `ConfigureMiddleware` (both hosts):

```csharp
AnnotationEndpoints.Map(app);
```

---

## 5. Annotations in the Frontend

### 5.1 The Composable: useAnnotations

```typescript
// src/composables/useAnnotations.ts
import { ref, computed, watch } from 'vue';
import { useApi } from '@/api/useApi';
import type { AnnotationDto, AnnotationKind } from '@/types/annotation';

interface UseAnnotationsParams {
  sessionId: Ref<string | null>;
  // Filter by exactly one of these
  forEventId?: Ref<string | null>;
  forEntityId?: Ref<string | null>;
  forTraceId?: Ref<string | null>;
}

export function useAnnotations(params: UseAnnotationsParams) {
  const api = useApi();
  const annotations = ref<AnnotationDto[]>([]);
  const loading = ref(false);
  const error = ref<string | null>(null);

  async function load() {
    if (!params.sessionId.value) return;
    loading.value = true;
    error.value = null;
    try {
      annotations.value = await api.listAnnotations({
        sessionId: params.sessionId.value,
        eventId: params.forEventId?.value ?? undefined,
        entityId: params.forEntityId?.value ?? undefined,
        traceId: params.forTraceId?.value ?? undefined,
      });
    } catch (err: any) {
      error.value = err.message ?? 'Failed to load annotations';
    } finally {
      loading.value = false;
    }
  }
  
  async function create(body: string, kind: AnnotationKind, target: any, title?: string, tags?: string[]) {
    const author = localStorage.getItem('tracer:authorName') ?? 'anonymous';
    const created = await api.createAnnotation({
      sessionId: params.sessionId.value!,
      kind, body, title, tags, author,
      ...target,  // eventId | entityId | traceId | targetWallclockUtc
    });
    annotations.value = [created, ...annotations.value];
  }
  
  async function update(id: string, body: string, title?: string, tags?: string[]) {
    const updated = await api.updateAnnotation(id, { body, title, tags });
    const idx = annotations.value.findIndex(a => a.annotationId === id);
    if (idx >= 0) annotations.value[idx] = updated;
  }
  
  async function remove(id: string) {
    await api.deleteAnnotation(id);
    annotations.value = annotations.value.filter(a => a.annotationId !== id);
  }

  watch(
    () => [params.sessionId.value, params.forEventId?.value, params.forEntityId?.value, params.forTraceId?.value],
    load,
    { immediate: true }
  );

  return { annotations, loading, error, reload: load, create, update, remove };
}
```

### 5.2 AnnotationEditor.vue

A modal/popover for creating and editing annotations.

```vue
<!-- src/components/AnnotationEditor.vue -->
<script setup lang="ts">
import { ref, watch } from 'vue';
import type { AnnotationDto } from '@/types/annotation';

const props = defineProps<{
  initial?: AnnotationDto | null;
  visible: boolean;
}>();

const emit = defineEmits<{
  save: [{ body: string; title: string; tags: string[] }];
  cancel: [];
  delete: [];
}>();

const body = ref('');
const title = ref('');
const tags = ref<string[]>([]);
const tagsInput = ref('');

watch(() => props.initial, (a) => {
  body.value = a?.body ?? '';
  title.value = a?.title ?? '';
  tags.value = a?.tags ?? [];
}, { immediate: true });

function save() {
  if (!body.value.trim()) return;
  emit('save', { body: body.value, title: title.value, tags: tags.value });
}

function addTag() {
  const t = tagsInput.value.trim();
  if (t && !tags.value.includes(t)) tags.value = [...tags.value, t];
  tagsInput.value = '';
}

function removeTag(t: string) {
  tags.value = tags.value.filter(x => x !== t);
}
</script>

<template>
  <div v-if="visible" class="annotation-editor">
    <div class="annotation-editor__overlay" @click="$emit('cancel')" />
    <div class="annotation-editor__panel">
      <h3>{{ initial ? 'Edit annotation' : 'Add annotation' }}</h3>
      
      <label>
        Title (optional):
        <input v-model="title" type="text" placeholder="Short header" />
      </label>
      
      <label>
        Note:
        <textarea v-model="body" rows="6" placeholder="What did you see? What does it mean?" autofocus />
      </label>
      
      <label>
        Tags:
        <div class="annotation-editor__tags">
          <span v-for="t in tags" :key="t" class="annotation-editor__tag">
            {{ t }}
            <button @click="removeTag(t)">×</button>
          </span>
          <input
            v-model="tagsInput"
            type="text"
            placeholder="Add tag"
            @keydown.enter.prevent="addTag"
            @keydown.,.prevent="addTag"
          />
        </div>
      </label>
      
      <div class="annotation-editor__actions">
        <button v-if="initial" class="annotation-editor__delete" @click="$emit('delete')">Delete</button>
        <button class="annotation-editor__cancel" @click="$emit('cancel')">Cancel</button>
        <button class="annotation-editor__save" :disabled="!body.trim()" @click="save">
          {{ initial ? 'Save changes' : 'Create' }}
        </button>
      </div>
    </div>
  </div>
</template>

<style lang="scss">
.annotation-editor {
  position: fixed; inset: 0; z-index: 100;
  display: flex; align-items: center; justify-content: center;
  
  &__overlay {
    position: absolute; inset: 0;
    background: rgba(0,0,0,0.5);
  }
  
  &__panel {
    position: relative;
    background: var(--c-bg-surface);
    border-radius: 12px;
    padding: 1.5rem;
    width: 480px;
    max-width: 90vw;
    display: flex; flex-direction: column; gap: 0.75rem;
    
    label { display: flex; flex-direction: column; gap: 0.25rem; font-size: 0.875rem; color: var(--c-text-muted); }
    input, textarea {
      background: var(--c-bg-subtle);
      border: 1px solid transparent;
      border-radius: 6px;
      color: var(--c-text);
      padding: 0.5rem;
      font-family: var(--font-sans);
    }
    textarea { font-family: inherit; resize: vertical; }
  }
  
  &__tags { display: flex; flex-wrap: wrap; gap: 0.25rem; }
  &__tag {
    background: var(--c-bg-subtle);
    border-radius: 999px;
    padding: 0.125rem 0.5rem;
    font-size: 0.75rem;
    display: flex; align-items: center; gap: 0.25rem;
    button { background: none; border: none; color: var(--c-text-muted); cursor: pointer; }
  }
  
  &__actions {
    display: flex; gap: 0.5rem; justify-content: flex-end;
    button { padding: 0.5rem 1rem; border-radius: 6px; border: none; cursor: pointer; }
  }
  &__save { background: var(--c-accent); color: white; &:disabled { opacity: 0.5; cursor: not-allowed; } }
  &__cancel { background: var(--c-bg-subtle); color: var(--c-text); }
  &__delete { background: var(--c-danger); color: white; margin-right: auto; }
}
</style>
```

### 5.3 Annotation Indicators in Views

Each primary view (Timeline, Causal Tree, Entity History, Scenario) overlays small annotation indicators on its visual elements.

**Timeline**: a small "note" badge above the event marker for events with annotations. Click → opens the inspector with the annotation already selected.

**Causal Tree**: a small "note" badge on the corner of any node with annotations.

**Entity History**: a vertical band in the event strip; the lifecycle ribbon shows entity-level annotations as overlay markers; the events strip shows event-level annotations as marker badges.

**Scenario View**: annotations on notable events appear in the notables list (sidebar).

The Vue component `AnnotationMarker.vue` is the shared visual primitive — a small icon (📝 or similar) with a tooltip-on-hover showing the annotation's title (or first line of body).

### 5.4 Inspector Integration

`EventInspector` from Phase 5 gets new sections:

```vue
<!-- excerpt from EventInspector.vue -->
<template>
  <section class="event-inspector">
    <!-- ... existing payload, severity, pivots ... -->
    
    <AnnotationList
      v-if="annotations.length > 0"
      :annotations="annotations"
      @edit="onEditAnnotation"
      @delete="onDeleteAnnotation"
    />
    
    <button v-if="!showEditor" class="event-inspector__add-note" @click="showEditor = true">
      Add note
    </button>
    
    <AnnotationEditor
      :initial="editingAnnotation"
      :visible="showEditor"
      @save="onSaveAnnotation"
      @cancel="closeEditor"
      @delete="confirmDeleteAnnotation"
    />
  </section>
</template>

<script setup lang="ts">
// ... imports ...
import { useAnnotations } from '@/composables/useAnnotations';

const { annotations, create, update, remove } = useAnnotations({
  sessionId: toRef(() => props.sessionId),
  forEventId: toRef(() => props.event?.eventId),
});

const showEditor = ref(false);
const editingAnnotation = ref<AnnotationDto | null>(null);

function onEditAnnotation(a: AnnotationDto) {
  editingAnnotation.value = a;
  showEditor.value = true;
}

async function onSaveAnnotation(data: { body: string; title: string; tags: string[] }) {
  if (editingAnnotation.value) {
    await update(editingAnnotation.value.annotationId, data.body, data.title, data.tags);
  } else {
    await create(data.body, 'Event', { eventId: props.event!.eventId }, data.title, data.tags);
  }
  closeEditor();
}

function closeEditor() {
  showEditor.value = false;
  editingAnnotation.value = null;
}
</script>
```

The inspector shows existing annotations at the bottom of its payload area, with an "Add note" button. Clicking opens the modal editor. Editing an existing annotation populates the editor.

---

## 6. Saved Views and Bookmarks

### 6.1 Data Model

A saved view is **a labeled URL template plus metadata**.

```csharp
namespace Tracer.Storage.SavedViews;

public sealed record SavedViewRecord
{
    public required string SavedViewId { get; init; }       // ULID
    public required string SessionId { get; init; }          // saved views are session-scoped
    public required SavedViewKind Kind { get; init; }
    public required string ViewType { get; init; }           // "timeline" | "causal-tree" | "entity-history" | "scenario" | "trigger-eval"
    public required string Url { get; init; }                // full path + query, relative
    public required string Label { get; init; }
    public string? Description { get; init; }
    public required string Persona { get; init; }            // "engineer" | "scenario-author" | "operator"
    public string? Author { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? LastOpenedAtUtc { get; init; }
    public required int OpenCount { get; init; }             // incremented when opened
}

public enum SavedViewKind { SavedView, Bookmark }
```

The distinction between `SavedView` and `Bookmark`:
- **SavedView**: user explicitly saved this view with a meaningful label. Appears in the saved-views list.
- **Bookmark**: one-click affordance; URL captured automatically with an auto-generated label. Appears in the BookmarkBar, ordered by recency, with a soft cap (last 10).

Both go to the same backend table.

### 6.2 SqliteSavedViewStore

The same SQLite database as annotations — they live in the same file. The schema additions:

```sql
CREATE TABLE IF NOT EXISTS saved_views (
    saved_view_id      TEXT PRIMARY KEY,
    session_id         TEXT NOT NULL,
    kind               TEXT NOT NULL,
    view_type          TEXT NOT NULL,
    url                TEXT NOT NULL,
    label              TEXT NOT NULL,
    description        TEXT,
    persona            TEXT NOT NULL,
    author             TEXT,
    created_at         TEXT NOT NULL,
    last_opened_at     TEXT,
    open_count         INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_saved_views_session_persona ON saved_views (session_id, persona);
CREATE INDEX IF NOT EXISTS idx_saved_views_session_kind    ON saved_views (session_id, kind);
CREATE INDEX IF NOT EXISTS idx_saved_views_last_opened     ON saved_views (last_opened_at);
```

The store interface and implementation follow the same pattern as `SqliteAnnotationStore`. Details elided.

### 6.3 Bundle Saved Views

Should saved views travel in the bundle?

**Arguments for**: a saved view "Network errors during engagement" captured during live analysis is useful when reviewing the bundle later.

**Arguments against**: bundles are immutable artifacts; saved views are about workflow, not content. A field-support engineer opening a bundle creates their own workflow.

**Phase 8 chooses to include saved views in the bundle**, alongside annotations. Rationale: the cost is negligible (a JSON file with ~10 entries typically), and "I marked these as interesting views" is part of analysis context worth preserving.

The bundle stores them in `annotations/saved_views.json` — same directory as `annotations.json`, since both are user-authored content. The offline viewer reads them but cannot edit (read-only in offline mode, same as annotations).

### 6.4 API Endpoints

```
GET    /api/saved-views                          list, filterable
POST   /api/saved-views                          create
GET    /api/saved-views/{id}                     read one
PUT    /api/saved-views/{id}                     update label / description
DELETE /api/saved-views/{id}                     delete
POST   /api/saved-views/{id}/opened              record an open (increments counter)
```

The `POST /opened` endpoint is hit by the frontend when the user opens a saved view, so the store can track usage and present "recent" first.

### 6.5 SaveViewButton

The toolbar of each primary view gets a button:

```vue
<!-- src/components/SaveViewButton.vue -->
<script setup lang="ts">
import { ref, computed } from 'vue';
import { useRoute } from 'vue-router';
import { useApi } from '@/api/useApi';
import { usePersona } from '@/composables/usePersona';

const props = defineProps<{
  sessionId: string;
  viewType: 'timeline' | 'causal-tree' | 'entity-history' | 'scenario' | 'trigger-eval';
}>();

const route = useRoute();
const api = useApi();
const { persona } = usePersona();
const showDialog = ref(false);
const label = ref('');
const description = ref('');
const saving = ref(false);

const currentUrl = computed(() => route.fullPath);

async function saveExplicit() {
  if (!label.value.trim()) return;
  saving.value = true;
  try {
    await api.createSavedView({
      sessionId: props.sessionId,
      kind: 'SavedView',
      viewType: props.viewType,
      url: currentUrl.value,
      label: label.value,
      description: description.value || undefined,
      persona: persona.value,
      author: localStorage.getItem('tracer:authorName') ?? undefined,
    });
    showDialog.value = false;
    label.value = '';
    description.value = '';
  } finally { saving.value = false; }
}

async function bookmark() {
  // One-click: auto-generated label from URL parameters
  const autoLabel = generateAutoLabel(route);
  await api.createSavedView({
    sessionId: props.sessionId,
    kind: 'Bookmark',
    viewType: props.viewType,
    url: currentUrl.value,
    label: autoLabel,
    persona: persona.value,
    author: localStorage.getItem('tracer:authorName') ?? undefined,
  });
}

function generateAutoLabel(route: ReturnType<typeof useRoute>): string {
  const t = new Date().toLocaleTimeString();
  const filterParts: string[] = [];
  if (route.query.topic) filterParts.push(`topic=${(route.query.topic as string[]).join(',')}`);
  if (route.query.trace) filterParts.push(`trace`);
  if (route.query.entity) filterParts.push(`entity`);
  return filterParts.length > 0
    ? `${filterParts.join(' ')} (${t})`
    : `View at ${t}`;
}
</script>

<template>
  <div class="save-view-btn">
    <button class="save-view-btn__bookmark" title="Bookmark this view" @click="bookmark">
      🔖
    </button>
    <button class="save-view-btn__save" @click="showDialog = true">
      Save view
    </button>
    
    <div v-if="showDialog" class="save-view-dialog">
      <div class="save-view-dialog__overlay" @click="showDialog = false" />
      <div class="save-view-dialog__panel">
        <h3>Save this view</h3>
        <label>
          Label:
          <input v-model="label" autofocus placeholder="e.g. Network errors during engagement" />
        </label>
        <label>
          Description (optional):
          <textarea v-model="description" rows="3" placeholder="Why this view matters..." />
        </label>
        <div class="save-view-dialog__actions">
          <button @click="showDialog = false">Cancel</button>
          <button :disabled="!label.trim() || saving" @click="saveExplicit">
            {{ saving ? 'Saving…' : 'Save' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
```

### 6.6 BookmarkBar

A horizontal strip below the toolbar showing the last 10 bookmarks for the current view type and persona. Clicking restores the URL.

```vue
<!-- src/components/BookmarkBar.vue -->
<script setup lang="ts">
import { ref, onMounted, watch } from 'vue';
import { useRouter } from 'vue-router';
import { useApi } from '@/api/useApi';
import { usePersona } from '@/composables/usePersona';
import type { SavedViewDto } from '@/types/savedView';

const props = defineProps<{
  sessionId: string;
  viewType: string;
}>();

const router = useRouter();
const api = useApi();
const { persona } = usePersona();
const bookmarks = ref<SavedViewDto[]>([]);

async function load() {
  const all = await api.listSavedViews({
    sessionId: props.sessionId,
    viewType: props.viewType,
    kind: 'Bookmark',
    persona: persona.value,
    limit: 10,
    orderBy: 'recent'
  });
  bookmarks.value = all;
}

async function openBookmark(b: SavedViewDto) {
  await api.recordSavedViewOpened(b.savedViewId);
  router.push(b.url);
}

watch([() => props.sessionId, () => props.viewType, persona], load, { immediate: true });
</script>

<template>
  <div v-if="bookmarks.length > 0" class="bookmark-bar">
    <span class="bookmark-bar__label">Bookmarks:</span>
    <button
      v-for="b in bookmarks"
      :key="b.savedViewId"
      class="bookmark-bar__chip"
      :title="b.label"
      @click="openBookmark(b)"
    >
      {{ b.label }}
    </button>
  </div>
</template>

<style lang="scss">
.bookmark-bar {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 1rem;
  background: var(--c-bg-subtle);
  border-radius: 6px;
  
  &__label {
    font-size: 0.75rem;
    color: var(--c-text-muted);
    text-transform: uppercase;
    letter-spacing: 0.05em;
  }
  
  &__chip {
    padding: 0.25rem 0.625rem;
    background: var(--c-bg-surface);
    border: none;
    border-radius: 999px;
    color: var(--c-text);
    cursor: pointer;
    font-size: 0.75rem;
    max-width: 16rem;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    &:hover { background: var(--c-accent); }
  }
}
</style>
```

### 6.7 SavedViewsView

The list view at `/v/saved-views/{sessionId}`. Shows all explicit saved views (not bookmarks) grouped by view type. Filterable by persona.

```vue
<!-- src/views/SavedViewsView.vue -->
<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useApi } from '@/api/useApi';
import { usePersona } from '@/composables/usePersona';
import type { SavedViewDto } from '@/types/savedView';

const route = useRoute();
const router = useRouter();
const api = useApi();
const { persona, allPersonas } = usePersona();
const sessionId = route.params.sessionId as string;
const all = ref<SavedViewDto[]>([]);
const personaFilter = ref<string>(persona.value);

async function load() {
  all.value = await api.listSavedViews({
    sessionId,
    kind: 'SavedView',
    persona: personaFilter.value === 'all' ? undefined : personaFilter.value,
    limit: 200,
    orderBy: 'created'
  });
}

const byViewType = computed(() => {
  const groups: Record<string, SavedViewDto[]> = {};
  for (const v of all.value) {
    if (!groups[v.viewType]) groups[v.viewType] = [];
    groups[v.viewType].push(v);
  }
  return groups;
});

async function openView(v: SavedViewDto) {
  await api.recordSavedViewOpened(v.savedViewId);
  router.push(v.url);
}

async function deleteView(v: SavedViewDto) {
  if (!confirm(`Delete "${v.label}"?`)) return;
  await api.deleteSavedView(v.savedViewId);
  await load();
}

onMounted(load);
</script>

<template>
  <div class="saved-views">
    <header class="saved-views__header">
      <h1>Saved views</h1>
      <select v-model="personaFilter" @change="load">
        <option value="all">All personas</option>
        <option v-for="p in allPersonas" :key="p" :value="p">{{ p }}</option>
      </select>
    </header>
    
    <div v-if="all.length === 0" class="saved-views__empty">
      No saved views yet. From any view, click "Save view" in the toolbar.
    </div>
    
    <section v-for="(views, viewType) in byViewType" :key="viewType" class="saved-views__group">
      <h2>{{ viewType }}</h2>
      <ul class="saved-views__list">
        <li v-for="v in views" :key="v.savedViewId" class="saved-views__item">
          <div class="saved-views__main" @click="openView(v)">
            <div class="saved-views__label">{{ v.label }}</div>
            <div v-if="v.description" class="saved-views__description">{{ v.description }}</div>
            <div class="saved-views__meta">
              {{ v.persona }} ·
              opened {{ v.openCount }}×
              <span v-if="v.lastOpenedAtUtc">· last {{ formatRelative(v.lastOpenedAtUtc) }}</span>
            </div>
          </div>
          <button class="saved-views__delete" @click="deleteView(v)">Delete</button>
        </li>
      </ul>
    </section>
  </div>
</template>
```

---

## 7. Persona Switcher

A small affordance in `AppHeader` that lets the user identify themselves as engineer, scenario author, or operator. The persona shapes:

- Default click target for a session card on the Session Browser
- The BookmarkBar's "recent bookmarks" filter (per-persona)
- The SavedViewsView's default filter
- Future Phase 8.5+ features: per-persona dashboards

### 7.1 personaStore

```typescript
// src/stores/personaStore.ts
import { defineStore } from 'pinia';

export type Persona = 'engineer' | 'scenario-author' | 'operator';

export const usePersonaStore = defineStore('persona', {
  state: () => ({
    current: (localStorage.getItem('tracer:persona') as Persona) ?? 'engineer'
  }),
  actions: {
    set(p: Persona) {
      this.current = p;
      localStorage.setItem('tracer:persona', p);
    },
  },
});
```

### 7.2 PersonaSwitcher.vue

```vue
<!-- src/components/PersonaSwitcher.vue -->
<script setup lang="ts">
import { usePersonaStore, type Persona } from '@/stores/personaStore';

const store = usePersonaStore();

const personas: Array<{ id: Persona; label: string; icon: string }> = [
  { id: 'engineer',        label: 'Engineer',        icon: '🔧' },
  { id: 'scenario-author', label: 'Scenario Author', icon: '🎬' },
  { id: 'operator',        label: 'Operator',        icon: '🖥️' },
];
</script>

<template>
  <div class="persona-switcher">
    <button
      v-for="p in personas"
      :key="p.id"
      class="persona-switcher__btn"
      :class="{ 'persona-switcher__btn--active': store.current === p.id }"
      @click="store.set(p.id)"
      :title="p.label"
    >
      <span class="persona-switcher__icon">{{ p.icon }}</span>
      <span class="persona-switcher__label">{{ p.label }}</span>
    </button>
  </div>
</template>
```

### 7.3 Per-Persona Default View

Update `SessionCard.vue` (from Phase 3): clicking a card now routes based on persona.

```typescript
// inside SessionCard.vue
import { usePersonaStore } from '@/stores/personaStore';
const persona = usePersonaStore();

function openSession() {
  switch (persona.current) {
    case 'engineer':
      router.push({ name: 'timeline', params: { sessionId: props.session.sessionId } });
      break;
    case 'scenario-author':
    case 'operator':
    default:
      router.push({ name: 'scenario', params: { sessionId: props.session.sessionId } });
  }
}
```

### 7.4 Persona Is Not Authorization

The persona switcher is **a UI affordance, not an authorization gate**. Anyone can switch. There's no enforcement; the persona is purely about defaults and recent affordances. If the future calls for actual role-based access control, that's a separate Phase (architecture §1.2 lists security as deferred).

---

## 8. Trigger Evaluation Log

### 8.1 What This View Is For

Scenario authors write triggers — declarative rules of the form "if X happens, do Y". When a trigger evaluates during a session, it produces a `scenario.trigger_evaluated` event (a domain convention from the simulation engine). The event's payload includes:

- The trigger's ID and label
- The inputs the trigger saw (typically a snapshot of the state values it tested)
- The result (fired / did-not-fire)
- A `next_event_id` referencing the next event in the trace, if the trigger fired

The Trigger Evaluation Log is a tabular view of these events. Scenario authors use it to debug "why didn't this trigger fire?" — they find the relevant evaluation, see what state values it actually saw, and compare against what they expected.

### 8.2 Backend: TriggerEvalService

```csharp
namespace Tracer.WebApi.Queries;

public sealed class TriggerEvalService
{
    private readonly LiveMultiIntervalReader _reader;
    private readonly ILogger<TriggerEvalService> _logger;

    public TriggerEvalService(LiveMultiIntervalReader reader, ILogger<TriggerEvalService> logger)
    {
        _reader = reader;
        _logger = logger;
    }

    public async Task<TriggerEvalResult> ListAsync(
        string sessionId,
        WallclockTime from, WallclockTime to,
        string? triggerIdFilter,
        TriggerResult? resultFilter,
        int limit,
        CancellationToken ct)
    {
        await using var conn = await _reader.AcquireAsync(ct);
        
        var whereClause = """
            WHERE topic = 'scenario.trigger_evaluated'
              AND publish_wallclock >= $from
              AND publish_wallclock <  $to
            """;
        var unionSql = conn.BuildEventsUnionSql(whereClause: whereClause);
        
        // Apply trigger_id and result filters via payload JSON fields
        var extraClauses = new List<string>();
        if (triggerIdFilter is not null)
            extraClauses.Add("JSON_EXTRACT_STRING(payload, '$.triggerId') = $triggerId");
        if (resultFilter is { } r)
            extraClauses.Add("JSON_EXTRACT_STRING(payload, '$.result') = $result");
        var extraWhere = extraClauses.Count == 0 ? "" : "WHERE " + string.Join(" AND ", extraClauses);
        
        var sql = $"""
            WITH u AS ({unionSql}),
            filtered AS (SELECT * FROM u {extraWhere})
            SELECT * FROM filtered
            ORDER BY publish_wallclock
            LIMIT $limit;
            """;
        
        await using var cmd = conn.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new DuckDBParameter("from", from.ToDateTimeOffset()));
        cmd.Parameters.Add(new DuckDBParameter("to",   to.ToDateTimeOffset()));
        cmd.Parameters.Add(new DuckDBParameter("limit", limit));
        if (triggerIdFilter is not null) cmd.Parameters.Add(new DuckDBParameter("triggerId", triggerIdFilter));
        if (resultFilter is { } r2) cmd.Parameters.Add(new DuckDBParameter("result", r2.ToString().ToLowerInvariant()));
        
        var evals = new List<TriggerEvaluation>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var ev = EventRecordMapper.FromReader(reader);
            evals.Add(ParseEvaluation(ev));
        }
        return new TriggerEvalResult { Evaluations = evals };
    }

    private TriggerEvaluation ParseEvaluation(EventRecord ev)
    {
        try
        {
            using var doc = JsonDocument.Parse(ev.PayloadJson);
            var root = doc.RootElement;
            return new TriggerEvaluation
            {
                EventId = ev.EventId,
                EvaluatedAtUtc = ev.PublishWallclock.ToDateTimeOffset(),
                PublisherNode = ev.PublisherNode,
                TraceId = ev.TraceId,
                TriggerId = root.TryGetProperty("triggerId", out var t) ? t.GetString() ?? "" : "",
                TriggerLabel = root.TryGetProperty("triggerLabel", out var l) ? l.GetString() : null,
                Inputs = root.TryGetProperty("inputs", out var i) ? i.GetRawText() : "{}",
                Result = root.TryGetProperty("result", out var r) && r.GetString() == "fired"
                    ? TriggerResult.Fired : TriggerResult.NotFired,
                NextEventId = root.TryGetProperty("nextEventId", out var n) && n.ValueKind != JsonValueKind.Null
                    ? new EventId(ulong.Parse(n.GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
                    : null,
                Reason = root.TryGetProperty("reason", out var reason) ? reason.GetString() : null,
            };
        }
        catch
        {
            return new TriggerEvaluation
            {
                EventId = ev.EventId,
                EvaluatedAtUtc = ev.PublishWallclock.ToDateTimeOffset(),
                PublisherNode = ev.PublisherNode,
                TraceId = ev.TraceId,
                TriggerId = "(malformed payload)",
                Result = TriggerResult.NotFired,
                Inputs = ev.PayloadJson
            };
        }
    }
}

public sealed record TriggerEvaluation
{
    public required EventId EventId { get; init; }
    public required DateTimeOffset EvaluatedAtUtc { get; init; }
    public required string PublisherNode { get; init; }
    public required ulong TraceId { get; init; }
    public required string TriggerId { get; init; }
    public string? TriggerLabel { get; init; }
    public required string Inputs { get; init; }     // JSON
    public required TriggerResult Result { get; init; }
    public EventId? NextEventId { get; init; }       // if fired
    public string? Reason { get; init; }              // if not-fired, sometimes
}

public enum TriggerResult { Fired, NotFired }

public sealed record TriggerEvalResult
{
    public required IReadOnlyList<TriggerEvaluation> Evaluations { get; init; }
}
```

### 8.3 Endpoint

```csharp
namespace Tracer.WebApi.Endpoints;

public static class TriggerEvalEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/scenario/triggers", HandleListAsync).WithOpenApi();
    }

    public static async Task<Ok<TriggerEvaluationListDto>> HandleListAsync(
        [FromQuery] string sessionId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? triggerId,
        [FromQuery] string? result,            // "fired" or "not-fired"
        [FromQuery] int limit = 1000,
        [FromServices] TriggerEvalService service = default!,
        [FromServices] SessionQueryService sessions = default!,
        CancellationToken ct = default)
    {
        var session = await sessions.GetAsync(sessionId, ct);
        var resultEnum = result?.ToLowerInvariant() switch
        {
            "fired" => TriggerResult.Fired,
            "not-fired" => TriggerResult.NotFired,
            _ => (TriggerResult?)null
        };
        var rangeFrom = from ?? session?.StartUtc ?? DateTimeOffset.MinValue;
        var rangeTo   = to   ?? session?.EndUtc   ?? DateTimeOffset.UtcNow;
        
        var data = await service.ListAsync(
            sessionId,
            WallclockTime.FromDateTimeOffset(rangeFrom),
            WallclockTime.FromDateTimeOffset(rangeTo),
            triggerId, resultEnum,
            Math.Clamp(limit, 1, 5000), ct);
        
        return TypedResults.Ok(TriggerEvalDtoMapper.Map(data));
    }
}
```

### 8.4 TriggerEvalView

```vue
<!-- src/views/TriggerEvalView.vue -->
<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useApi } from '@/api/useApi';
import type { TriggerEvaluationDto } from '@/types/triggerEval';

const route = useRoute();
const router = useRouter();
const api = useApi();
const sessionId = computed(() => route.params.sessionId as string);

const triggerIdFilter = ref('');
const resultFilter = ref<'all' | 'fired' | 'not-fired'>('all');
const evaluations = ref<TriggerEvaluationDto[]>([]);
const loading = ref(false);

async function load() {
  loading.value = true;
  try {
    const result = await api.listTriggerEvaluations({
      sessionId: sessionId.value,
      triggerId: triggerIdFilter.value || undefined,
      result: resultFilter.value === 'all' ? undefined : resultFilter.value,
      limit: 1000
    });
    evaluations.value = result.evaluations;
  } finally { loading.value = false; }
}

const distinctTriggerIds = computed(() =>
  Array.from(new Set(evaluations.value.map(e => e.triggerId))).sort());

function openCausalTree(ev: TriggerEvaluationDto) {
  router.push({ name: 'causal-by-event', params: { eventId: ev.eventId } });
}

function showInTimeline(ev: TriggerEvaluationDto) {
  const t = new Date(ev.evaluatedAtUtc).getTime();
  router.push({
    name: 'timeline',
    params: { sessionId: sessionId.value },
    query: {
      from: new Date(t - 5000).toISOString(),
      to: new Date(t + 5000).toISOString(),
      select: ev.eventId
    }
  });
}

watch([triggerIdFilter, resultFilter], load);
onMounted(load);
</script>

<template>
  <div class="trigger-eval-view">
    <header class="trigger-eval-view__header">
      <h1>Trigger evaluations</h1>
      <div class="trigger-eval-view__filters">
        <select v-model="triggerIdFilter">
          <option value="">All triggers</option>
          <option v-for="id in distinctTriggerIds" :key="id" :value="id">{{ id }}</option>
        </select>
        <select v-model="resultFilter">
          <option value="all">All results</option>
          <option value="fired">Fired</option>
          <option value="not-fired">Not fired</option>
        </select>
      </div>
    </header>
    
    <div v-if="loading">Loading…</div>
    <table v-else class="trigger-eval-view__table">
      <thead>
        <tr>
          <th>Time</th>
          <th>Trigger</th>
          <th>Label</th>
          <th>Node</th>
          <th>Result</th>
          <th>Actions</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="e in evaluations" :key="e.eventId" :class="`trigger-eval-view__row--${e.result}`">
          <td class="trigger-eval-view__time">{{ formatTime(e.evaluatedAtUtc) }}</td>
          <td class="trigger-eval-view__id">{{ e.triggerId }}</td>
          <td>{{ e.triggerLabel ?? '—' }}</td>
          <td>{{ e.publisherNode }}</td>
          <td>
            <span class="trigger-eval-view__pill" :class="`trigger-eval-view__pill--${e.result}`">
              {{ e.result }}
            </span>
          </td>
          <td>
            <button @click="showInTimeline(e)">Timeline</button>
            <button @click="openCausalTree(e)">Tree</button>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
```

A click on a row could open an inline panel showing the full inputs (the JSON object the trigger saw). For Phase 8 a simple click-to-toggle inline expansion is sufficient.

---

## 9. Lifecycle Topic Configuration

Phase 7 hardcoded the lifecycle classification (spawn/ownership/destruction) by topic-name suffix. Phase 8 makes it configurable per deployment.

### 9.1 Configuration Shape

A new section in `ObserverConfig` (and the same shape in offline viewer config, since bundles are deployment-agnostic):

```csharp
public sealed class LifecycleClassificationConfig
{
    /// <summary>Topic name suffixes (after final dot) that mark spawn events.</summary>
    public IReadOnlyList<string> SpawnSuffixes { get; init; } = new[] { "spawn", "created", "spawned" };
    
    /// <summary>Suffixes for ownership transitions.</summary>
    public IReadOnlyList<string> OwnershipSuffixes { get; init; } = new[]
    {
        "ownership_changed", "owner_transferred", "owner_changed"
    };
    
    /// <summary>Suffixes for destruction.</summary>
    public IReadOnlyList<string> DestructionSuffixes { get; init; } = new[]
    {
        "destroyed", "killed", "removed", "despawned"
    };
    
    /// <summary>
    /// Optional regex patterns that override suffix matching. If both are
    /// configured, regex matches take precedence.
    /// </summary>
    public LifecycleRegexPatterns? Regex { get; init; }
}

public sealed record LifecycleRegexPatterns(string? Spawn, string? Ownership, string? Destruction);
```

### 9.2 Exposing the Config to the Frontend

The frontend needs to know the classification rules to apply them in `EntityLifecycleRibbon`. A new endpoint:

```
GET /api/config/lifecycle-classification
```

```csharp
namespace Tracer.WebApi.Endpoints;

public static class ConfigEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/config/lifecycle-classification", HandleAsync).WithOpenApi();
    }

    public static Ok<LifecycleConfigDto> HandleAsync(
        [FromServices] ObserverConfig config)
    {
        return TypedResults.Ok(new LifecycleConfigDto
        {
            SpawnSuffixes = config.LifecycleClassification.SpawnSuffixes,
            OwnershipSuffixes = config.LifecycleClassification.OwnershipSuffixes,
            DestructionSuffixes = config.LifecycleClassification.DestructionSuffixes,
            SpawnRegex = config.LifecycleClassification.Regex?.Spawn,
            OwnershipRegex = config.LifecycleClassification.Regex?.Ownership,
            DestructionRegex = config.LifecycleClassification.Regex?.Destruction,
        });
    }
}
```

The frontend's `lifecycleClassifier.ts` (Phase 7) fetches this once at startup and stores in a Pinia store:

```typescript
// src/stores/lifecycleConfigStore.ts
import { defineStore } from 'pinia';
import { useApi } from '@/api/useApi';
import type { LifecycleConfigDto } from '@/types/lifecycle';

export const useLifecycleConfigStore = defineStore('lifecycleConfig', {
  state: () => ({
    config: null as LifecycleConfigDto | null,
    loaded: false,
  }),
  actions: {
    async load() {
      if (this.loaded) return;
      const api = useApi();
      try { this.config = await api.getLifecycleConfig(); }
      catch { /* fall back to hardcoded defaults */ }
      this.loaded = true;
    },
    classifyTopic(topic: string): 'spawn' | 'ownership' | 'destruction' | null {
      const c = this.config;
      if (!c) return classifyDefault(topic);
      
      if (c.spawnRegex && new RegExp(c.spawnRegex).test(topic)) return 'spawn';
      if (c.ownershipRegex && new RegExp(c.ownershipRegex).test(topic)) return 'ownership';
      if (c.destructionRegex && new RegExp(c.destructionRegex).test(topic)) return 'destruction';
      
      const tail = topic.split('.').pop() ?? '';
      if (c.spawnSuffixes.includes(tail)) return 'spawn';
      if (c.ownershipSuffixes.includes(tail)) return 'ownership';
      if (c.destructionSuffixes.includes(tail)) return 'destruction';
      return null;
    }
  }
});
```

Phase 7's classifier function is replaced by `lifecycleConfigStore.classifyTopic`. The bundle-mode offline viewer reads the same endpoint, served from a config baked into the bundle metadata (see §9.3).

### 9.3 Bundle Inclusion

When the aggregator builds a bundle, it captures the live Observer's lifecycle classification config into bundle metadata so offline viewing matches the analysis context:

```csharp
// In MetadataWriter.WriteAsync (Phase 4 §6), additions:
var lifecycleConfig = new
{
    spawnSuffixes = config.LifecycleClassification.SpawnSuffixes,
    ownershipSuffixes = config.LifecycleClassification.OwnershipSuffixes,
    destructionSuffixes = config.LifecycleClassification.DestructionSuffixes,
    regex = config.LifecycleClassification.Regex
};
metadata.LifecycleClassification = lifecycleConfig;
```

The offline viewer's `ConfigEndpoints` reads from the bundle metadata (or falls back to defaults if the bundle predates Phase 8).

---

## 10. Test Plan for Phase 8

### 10.1 Backend Unit Tests

**Annotations/SqliteAnnotationStoreTests.cs**
- `InitializeAsync` creates the schema; subsequent calls are idempotent
- `CreateAsync` with empty `AnnotationId`: generates a ULID
- `CreateAsync` with empty `CreatedAtUtc`: sets to now
- `GetAsync` with unknown id: returns null
- `UpdateAsync` with unknown id: returns null
- `UpdateAsync` updates `ModifiedAtUtc`
- `DeleteAsync` with unknown id: returns false
- `ListAsync` with various filter combinations
- `ListAsync` orders by created_at DESC
- `ListAsync` respects limit
- Tags serialization round-trips
- Multi-line body and special characters preserved

**Annotations/BundleAnnotationStoreTests.cs**
- `ListAsync` on bundle with no annotations.json: returns empty
- `ListAsync` on bundle with annotations.json: returns parsed records
- Write operations (`CreateAsync`, `UpdateAsync`, `DeleteAsync`): throw InvalidOperationException
- `ExportAllForSessionAsync` filters by session

**SavedViews/SqliteSavedViewStoreTests.cs**
- CRUD parity with annotations
- Open-count increment via `RecordOpenedAsync`
- `last_opened_at` updated on open
- Filter by persona returns only matching
- Filter by kind (SavedView vs Bookmark)

**WebApi/AnnotationEndpointsTests.cs**
- POST with valid body: 201 Created with Location header
- POST with empty body: 400
- POST with multiple target identifiers: 400
- POST with no target identifiers: 400
- POST with invalid kind: 400
- POST in bundle (read-only) mode: 405
- PUT non-existent: 404
- PUT in bundle mode: 405
- DELETE non-existent: 404
- DELETE in bundle mode: 405
- GET list with multiple filters
- GET single by ID

**WebApi/SavedViewEndpointsTests.cs**
- POST creates a saved view
- GET list filtered by persona, kind
- POST /{id}/opened increments counter
- DELETE removes
- GET list orders by recency when requested

**WebApi/TriggerEvalServiceTests.cs**
- Returns evaluations matching `topic='scenario.trigger_evaluated'`
- Filters by triggerId
- Filters by result (fired/not-fired)
- Time range respected
- Payload parsing: extracts triggerId, label, inputs, result, nextEventId
- Malformed payload: returns evaluation with "(malformed payload)" indicator
- Empty result: empty list, no exception

**WebApi/TriggerEvalEndpointsTests.cs**
- GET with valid sessionId: 200
- Unknown sessionId: 404
- Invalid result value: ignored (returns all)
- Limit clamped

**Aggregator/AnnotationsExporterTests.cs**
- With no annotations in the store: doesn't create the file
- With annotations: writes correct JSON
- Filters to the target session ID
- File location is `{bundleStaging}/annotations/annotations.json`

### 10.2 Backend Integration Tests

**AnnotationsRoundTripTests.cs**
- Start Observer, create 3 annotations via API
- Trigger bundle build
- Open bundle in offline viewer
- GET /api/annotations: returns same 3 annotations
- Attempt POST: 405

**SavedViewsRoundTripTests.cs**
- Same pattern: create saved views live, bundle, verify in offline viewer

**TriggerEvalIntegrationTests.cs**
- Push synthetic `scenario.trigger_evaluated` events with various trigger IDs and results
- Query the endpoint with various filters
- Verify expected results

### 10.3 Frontend Unit Tests (Vitest)

**annotationStore.spec.ts**
- `useAnnotations` loads annotations on entity change
- `create` adds to local list
- `update` modifies the local entry
- `remove` removes from local list

**usePersona.spec.ts**
- Default is 'engineer' if localStorage empty
- `set` persists to localStorage
- Reading after set: returns set value

**useBookmarks.spec.ts**
- Loads per current viewType + persona
- Reloads on persona change

### 10.4 E2E Tests (Playwright)

```typescript
test('create annotation on event', async ({ page }) => {
  await page.goto('http://localhost:5300/v/timeline/test-session');
  await page.locator('.timeline-canvas').click({ position: { x: 500, y: 200 } });
  await page.waitForSelector('.event-inspector');
  await page.locator('.event-inspector__add-note').click();
  await page.locator('.annotation-editor textarea').fill('This is a suspicious event.');
  await page.locator('.annotation-editor__save').click();
  // Reload and verify annotation marker visible
  await page.reload();
  await page.waitForSelector('.timeline-canvas');
  await expect(page.locator('.annotation-marker')).toBeVisible();
});

test('save view and restore', async ({ page }) => {
  await page.goto('http://localhost:5300/v/timeline/test-session?topic=weapons.fire');
  await page.locator('.save-view-btn__save').click();
  await page.locator('.save-view-dialog input').fill('Weapons fire across the session');
  await page.locator('.save-view-dialog button:has-text("Save")').click();
  // Navigate away
  await page.goto('http://localhost:5300/sessions');
  // Open saved views
  await page.goto('http://localhost:5300/v/saved-views/test-session');
  await page.locator('.saved-views__item:has-text("Weapons fire")').click();
  await expect(page).toHaveURL(/topic=weapons.fire/);
});

test('persona switcher changes default landing', async ({ page }) => {
  await page.goto('http://localhost:5300/sessions');
  await page.locator('.persona-switcher__btn:has-text("Engineer")').click();
  await page.locator('.session-card').first().click();
  await expect(page).toHaveURL(/\/v\/timeline\//);
  
  await page.goto('http://localhost:5300/sessions');
  await page.locator('.persona-switcher__btn:has-text("Scenario Author")').click();
  await page.locator('.session-card').first().click();
  await expect(page).toHaveURL(/\/scenario\//);
});

test('trigger evaluation log filters', async ({ page }) => {
  await page.goto('http://localhost:5300/v/triggers/test-session');
  await page.locator('select').nth(1).selectOption('not-fired');
  // Only "not-fired" rows should be visible
  await expect(page.locator('.trigger-eval-view__row--Fired')).toHaveCount(0);
});
```

### 10.5 Performance Tests

- Annotation list query (200 annotations): < 100 ms
- Saved-views list (50 entries): < 50 ms
- Trigger eval list (5000 evaluations): < 300 ms
- Annotations exporter on 100 annotations: < 50 ms
- Page load with annotations rendered: < 1.5 s

---

## 11. Phase 8 Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| SQLite write contention with concurrent annotation creates | Low | Low | SemaphoreSlim around writes; SQLite handles single-writer-multi-reader natively. At Phase 8's expected user count (< 5 concurrent editors), no real risk. |
| Bundle annotation read-only behavior confuses users | Medium | Low | Clear 405 message; UI disables "Add note" affordance when in bundle mode (read from a `useBundleMode` composable that exposes `isReadOnly`). |
| Saved view URLs go stale when feature URLs change | Medium | Medium | Saved view URLs include only stable query parameter names. Document URL stability as a contract. Major URL changes require a migration step. |
| Trigger evaluation events from the simulation don't follow the documented payload shape | High | Medium | The service tolerates malformed payloads (returns a degraded `(malformed payload)` row). Document the expected schema in `docs/trigger-payload-schema.md` for the integration project. |
| Persona switching mid-session disorients the user | Low | Low | Persona switching is rare in practice and explicit. No automated switching. |
| Annotation indicators clutter the timeline at high zoom | Medium | Low | Marker is small (8px); only shows on hover/click below a density threshold. |
| Existing bundles lack `annotations/saved_views.json`; offline viewer must handle gracefully | High | Low | The `BundleAnnotationStore` returns empty when the file doesn't exist. Tested explicitly. |
| Sqlite database location collides between Observer instances | Low | High | The `{DataRoot}/annotations.db` is per-Observer-instance; multiple observers means multiple DBs. Document the assumption that one Observer owns its DataRoot. |
| Annotations on time-points are hard to surface visually | Medium | Low | Time-point annotations appear in the events strip as a vertical line at their wallclock. Sidebar list shows them too. |
| Trigger evaluation log shows duplicate evaluations from multiple nodes (same trigger evaluated by multiple agents) | Medium | Medium | Document that scenario triggers typically evaluate on a single authoritative node. If the customer has cross-node trigger evaluation, the view shows them all (correctly); user filters by publisher_node if needed. |

---

## 12. Definition of Done for Phase 8

### Build & Run

- [ ] `Tracer.Storage.Annotations` and `Tracer.Storage.SavedViews` build clean
- [ ] All endpoints registered in both Observer and Offline Viewer
- [ ] OpenAPI spec includes new endpoints; TypeScript client regenerates
- [ ] `Microsoft.Data.Sqlite` resolves correctly on Windows targets

### Annotations Backend

- [ ] SQLite store initializes; schema is idempotent
- [ ] All CRUD operations work in live mode
- [ ] Bundle store reads correctly; writes throw
- [ ] Annotations export to bundle on aggregation; included in manifest checksums
- [ ] Read-only-mode writes return 405 ProblemDetails

### Annotations Frontend

- [ ] Annotation editor modal: create, edit, delete
- [ ] AnnotationMarker overlay appears on Timeline event markers with annotations
- [ ] AnnotationMarker overlay appears on Causal Tree nodes with annotations
- [ ] AnnotationMarker overlay appears on Entity History entries
- [ ] EventInspector shows linked annotations
- [ ] Author display name set in user preferences (Settings tab); falls back to "anonymous"
- [ ] Bundle mode disables "Add note" button gracefully

### Saved Views

- [ ] SaveViewButton in toolbar of Timeline, Causal Tree, Entity History, Scenario View, Trigger Eval View
- [ ] Save dialog captures label + description
- [ ] Bookmark button (one-click) generates auto-label
- [ ] BookmarkBar shows recent bookmarks per viewType + persona
- [ ] SavedViewsView lists saved views grouped by type
- [ ] Filter by persona works
- [ ] Click opens URL; open-count increments
- [ ] Delete removes
- [ ] Saved views exported to bundle; readable in offline viewer

### Persona Switcher

- [ ] Three personas: Engineer, Scenario Author, Operator
- [ ] Stored in localStorage
- [ ] Default is Engineer
- [ ] Session card default click target depends on persona

### Trigger Evaluation Log

- [ ] `/v/triggers/{sessionId}` lists evaluations
- [ ] Filter by trigger ID works
- [ ] Filter by result (fired/not-fired) works
- [ ] Click "Timeline": pivots to TimelineView focused on evaluation time
- [ ] Click "Tree": pivots to CausalTreeView
- [ ] Inline expansion shows full input JSON
- [ ] Empty session returns empty table (no error)
- [ ] Malformed payload rows displayed defensively

### Lifecycle Topic Configuration

- [ ] `LifecycleClassificationConfig` in ObserverConfig
- [ ] `/api/config/lifecycle-classification` endpoint
- [ ] Frontend store loads on app init and uses for `EntityLifecycleRibbon`
- [ ] Falls back to defaults when bundle/observer doesn't expose config
- [ ] Bundle metadata includes the live config

### Testing

- [ ] All Phase 1-7 tests pass
- [ ] Phase 8 backend unit tests pass (target: 40+)
- [ ] Phase 8 backend integration tests pass (annotations round-trip, saved views round-trip, trigger eval)
- [ ] Phase 8 frontend unit tests pass
- [ ] At least three Playwright E2E tests pass

### Performance

- [ ] Annotation list 200 entries: < 100 ms
- [ ] Saved view list 50 entries: < 50 ms
- [ ] Trigger eval 5000 entries: < 300 ms
- [ ] Page load with annotations: < 1.5 s

### Documentation

- [ ] `docs/annotations.md` explains the data model and bundle behavior
- [ ] `docs/saved-views.md` documents saved view vs bookmark distinction
- [ ] `docs/trigger-payload-schema.md` documents the expected payload shape for `scenario.trigger_evaluated`
- [ ] `docs/lifecycle-configuration.md` documents the per-deployment override mechanism
- [ ] `docs/personas.md` clarifies that personas are UI defaults, not access control
- [ ] CHANGELOG entry

---

## 13. Handoff to Phase 9

What Phase 9 inherits from Phase 8:

- **The saved-view pattern**: Phase 9's replication latency view will participate in the same saved-view/bookmark system. The `viewType` enum gains `replication-latency`.
- **The persona machinery**: Phase 9's latency analysis is engineer-facing; the persona's preferred views list grows.
- **The annotation pattern**: latency outliers can be annotated for future reference, using the same store and UI.
- **The trigger eval query pattern**: filtering and parsing payload JSON via `JSON_EXTRACT_STRING` is the same technique Phase 9 may use for per-message latency analysis.

What Phase 9 must address that Phase 8 deferred:

- **Replication latency calculation**: per-message receive-time analysis across nodes — requires the real DDS adapter (architecture §18 Phase 9 prerequisite) since mock data doesn't model network behavior realistically
- **Gap detection**: missing-message detection per topic per subscriber
- **Network topology view**: visualization of which nodes subscribe to which topics from which publishers
- **Per-node receive-time distribution**: histograms, percentiles, outlier highlighting
- **Latency-over-time series**: see how latency trends shift through the session

What's now possible after Phase 8:

The complete user-content layer is in place. Engineers can attach notes, save complex queries, and bookmark their workflows. Scenario authors have their dedicated trigger view. The diagnostic toolkit (Phases 1-7) is now also a **collaboration substrate** — analyses can be shared, recalled, and built upon over time.

By Phase 8, Tracer is no longer a "single-session tool" — it's an **artifact in the team's knowledge base**. A bundle from a months-old session arrives complete with the analyses someone did on it. The next engineer picks up where the last one left off.

Phases 9-11 add specialized capabilities (latency analysis, SQL escape hatch, real adapters). The architectural shape is complete; remaining work is depth and breadth on the established foundation.
