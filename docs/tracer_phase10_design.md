# Tracer Phase 10 — Detailed Design
## SQL Console, Saved Queries, Bundle Library

*Companion to `tracer_architecture_v1.md` and `tracer_phase1_design.md` through `tracer_phase9_design.md`*
*Phase 10 of the build sequence (architecture §18)*
*C# / .NET 8 backend · Vue 3 / TypeScript frontend · May 2026*

*Phase 10 is the "escape hatch" phase. Phases 1-9 built opinionated views for the questions we knew about. Phase 10 acknowledges that engineers will always have questions we didn't anticipate. A SQL console with read-only DuckDB access lets them answer those questions without waiting for a new view. A library of saved queries turns one engineer's ad-hoc analysis into a reusable team asset.*

*Phase 10 also tidies the bundle library. Phase 4 introduced bundles; Phase 5 added a basic listing UI; Phase 9 added the metadata foundation (latency budgets, etc.). Phase 10 makes the bundle library a first-class workspace: tagging, descriptions, filtering, sorting, archival.*

*Architecturally Phase 10 is mostly UI work plus a small but important backend addition: a query-budget-enforced SQL executor that runs untrusted SQL against the bundle/observer DuckDB safely.*

---

## 1. Phase 10 Scope and Goals

### 1.1 What Phase 10 Delivers

**SQL Console**
- `SqlConsoleView.vue` at `/v/sql/{sessionId}` — the editor + result view
- Backend `/api/sql/execute` endpoint with read-only enforcement, query budget (timeout + row limit + memory limit), and prepared-parameter support
- Editor with syntax highlighting (CodeMirror 6, Vue integration), autocomplete from schema, history navigation
- Result rendering: tabular default, optional chart view (line/bar) when result shape permits
- Export results: CSV, JSON, copy-to-clipboard
- A `/api/sql/schema` endpoint returning the bundle/observer's queryable schema (tables, columns, types) for autocomplete
- A `/api/sql/explain` endpoint to surface query plans without executing

**Saved Queries Library**
- New table in the existing SQLite store (Phase 8): `saved_queries`
- API: list, create, update, delete, mark-as-favorite
- `SavedQueriesView.vue` listing all saved queries, filterable by author, tag, favorite
- Each saved query has parameters with defaults, so "show me events for entity X" becomes a reusable template
- **Built-in queries**: a curated set ships with the system covering common analyses (latency outliers by topic, trace lineage, entity event counts, etc.). These are read-only; users can clone them to make editable copies.
- Run-and-pivot: results with `event_id`, `entity_id`, `trace_id`, or `publish_wallclock` columns offer one-click pivot to the appropriate view

**Bundle Library Enhancements**
- `BundleLibraryView.vue` replaces Phase 5's basic listing
- Per-bundle metadata: label (editable), description (editable), tags (Phase 8-style)
- Filtering: by tag, by date range, by scenario
- Sorting: by created date, by session start, by size, by label
- Bundle operations: open, delete, export-as-zip (for moving between machines), import (uploading a previously exported bundle)
- Bundle archival: mark-as-archived to hide from default lists without deleting
- **Bundle annotations and saved views remain bundle-scoped** (per Phase 8) — they appear in the library card but stay scoped to the bundle they were created in

**Cross-View Polish**
- Every analytical view (Phases 5-9) gets a "Show SQL for this view" affordance that opens the SQL Console with a query equivalent to the current view's filter. Useful both as a learning tool and as the on-ramp to ad-hoc analysis.

### 1.2 What Phase 10 Does NOT Deliver

- **No write SQL**: the executor is strictly read-only. INSERT, UPDATE, DELETE, CREATE, DROP, ATTACH, COPY-into-table all rejected at parse time.
- **No multi-bundle queries**: SQL queries scope to one session/bundle at a time. Cross-bundle joins are a future capability — for now, the SQL Console targets the currently-open bundle (or current observer).
- **No query scheduling or alerting**: SQL Console is for ad-hoc exploration. Scheduled queries with alerts are not in scope.
- **No collaborative editing**: saved queries are owned by one author at a time. No simultaneous editing.
- **No query result caching**: each execution runs fresh. At Phase 10's scale this is fine.
- **No SQL-to-natural-language or AI assistance**: out of scope.
- **No bundle migration tools for schema changes**: Phase 10 assumes the schema is stable. If a future phase changes the events schema, bundle migration is a separate concern.
- **No bundle versioning**: a bundle is immutable once built. Modifications happen via re-build.
- **No persistent SQL query history per user**: a session-local history is kept in the editor (last ~50 queries) via localStorage; permanent history requires saving as a Saved Query.

### 1.3 Success Criteria

1. **Open SQL Console**: from any view's toolbar, click "SQL console" → the console opens with the current view's equivalent SQL pre-loaded. Editor is responsive.
2. **Execute a query**: write SQL, hit Cmd+Enter. Results render in < 2 seconds for typical queries on a 30-min bundle.
3. **Read-only enforcement**: writes are rejected with a clear error. ATTACH and COPY-into-table are rejected.
4. **Query budget enforcement**: queries running longer than the budget are cancelled and return a clear timeout error.
5. **Save a query**: write a working query, save with label + description + tags. Reopen in another session; the query runs.
6. **Built-in queries**: clicking a built-in template runs immediately with sensible defaults.
7. **Pivot to view**: query result with `event_id` column shows "Open in timeline" on each row.
8. **Bundle library**: list shows tags, labels, sizes; filter by tag works; sort by columns works.
9. **Edit bundle metadata**: change a bundle's label and description; reload — the change persists.
10. **Performance**:
    - `/api/sql/execute` with simple SELECT: < 500 ms p95
    - `/api/sql/schema`: < 50 ms (cached)
    - SQL Console first paint: < 1 s
    - Bundle library list (100 bundles): < 200 ms
11. **All Phase 1-9 tests pass**.

### 1.4 Estimated Duration

Two calendar weeks. Distribution:
- Week 1: SQL executor backend (read-only enforcement, budget, parameter binding); SQL Console editor; result rendering
- Week 2: saved queries (backend + UI); bundle library enhancements; cross-view "Show SQL for this view" wiring; built-in query library

---

## 2. Project Layout Additions

Building on Phase 9:

```
tracer/
  src/
    Tracer.Core/                                  (unchanged)
    Tracer.WebApi/
      Endpoints/
        SqlEndpoints.cs                           NEW
        SavedQueryEndpoints.cs                    NEW
        BundleLibraryEndpoints.cs                 NEW (extends Phase 4 bundle endpoints)
      Queries/
        SqlExecutorService.cs                     NEW — the constrained query runner
        SqlSchemaService.cs                       NEW — schema introspection for autocomplete
        SqlGuardrails.cs                          NEW — read-only AST validator
        BuiltInQueriesService.cs                  NEW — ships canonical templates
        BundleLibraryService.cs                   NEW — list/filter/sort/metadata-edit
      Contracts/Dto/
        SqlExecuteRequestDto.cs
        SqlExecuteResultDto.cs
        SqlSchemaDto.cs
        SqlColumnInfoDto.cs
        SqlExplainResultDto.cs
        SavedQueryDto.cs
        BundleLibraryEntryDto.cs
      Streaming/
        SqlResultStreamer.cs                      NEW — chunked JSON for very wide results
    Tracer.Storage.SavedQueries/                  NEW assembly
      Tracer.Storage.SavedQueries.csproj
      ISavedQueryStore.cs
      SqliteSavedQueryStore.cs                    extends Phase 8 SQLite database
      SavedQueryRecord.cs
      Schema/
        SavedQueriesSchema.cs
      BuiltIn/
        builtin-queries.json                      ships with the assembly
        BuiltInLoader.cs
  tracer-viewer/
    src/
      views/
        SqlConsoleView.vue                        NEW
        SavedQueriesView.vue                      NEW
        BundleLibraryView.vue                     NEW (replaces simpler Phase 5 version)
      components/
        SqlEditor.vue                             NEW — CodeMirror 6 wrapper
        SqlResultTable.vue                        NEW — tabular result
        SqlResultChart.vue                        NEW — optional chart
        SqlResultActions.vue                      NEW — export/copy/pivot toolbar
        SchemaPanel.vue                           NEW — left sidebar schema tree
        SavedQueryPicker.vue                      NEW — modal query browser
        BundleCard.vue                            NEW — card in the library
        BundleFilterPanel.vue                     NEW
        BundleMetadataEditor.vue                  NEW — modal for editing label/description/tags
        ShowSqlButton.vue                         NEW — small "Show SQL" affordance for analytical views
      composables/
        useSqlExecution.ts                        NEW — wraps the /api/sql/execute call with cancellation
        useSqlSchema.ts                           NEW
        useSavedQueries.ts                        NEW
        useBundleLibrary.ts                       NEW
      stores/
        sqlConsoleStore.ts                        NEW
        bundleLibraryStore.ts                     NEW
      types/
        sql.ts                                    NEW
        savedQuery.ts                             NEW
        bundle.ts                                 EXTENDED
  tests/
    Tracer.Tests.Unit/
      WebApi/
        SqlExecutorServiceTests.cs
        SqlGuardrailsTests.cs
        SqlSchemaServiceTests.cs
        BuiltInQueriesServiceTests.cs
        BundleLibraryServiceTests.cs
        SavedQueryEndpointsTests.cs
        SqlEndpointsTests.cs
    Tracer.Tests.Integration/
      SqlConsoleIntegrationTests.cs
      BundleLibraryRoundTripTests.cs
      SavedQueriesRoundTripTests.cs
  tracer-viewer/tests/
    unit/
      sqlEditor.spec.ts
      useSqlExecution.spec.ts
      useBundleLibrary.spec.ts
    e2e/
      sql-console-flow.spec.ts
      bundle-library-flow.spec.ts
      saved-queries-flow.spec.ts
```

### 2.1 Dependencies

**Backend**: no new NuGet packages. SQL parsing for guardrails uses a hand-rolled lexer that recognizes statement-level constructs (sufficient for our enforcement needs); a full SQL parser library would be overkill.

**Frontend**: CodeMirror 6 for the editor.

```json
{
  "dependencies": {
    "@codemirror/lang-sql": "^6.5.0",
    "@codemirror/state": "^6.4.0",
    "@codemirror/view": "^6.21.0",
    "@codemirror/autocomplete": "^6.13.0",
    "@codemirror/commands": "^6.3.0",
    "@codemirror/search": "^6.5.0",
    "@codemirror/theme-one-dark": "^6.1.2"
  }
}
```

CodeMirror 6 ships its own SQL dialect support; minor configuration for DuckDB-specific functions (`time_bucket`, `approx_quantile`) via custom completions.

---

## 3. SQL Executor: The Constrained Runner

### 3.1 What Constraints Matter

Phase 10's SQL executor sits between user-entered SQL and the bundle's DuckDB. Constraints to enforce:

| Constraint | Why | How |
|---|---|---|
| Read-only — no INSERT/UPDATE/DELETE/CREATE/DROP | Bundle integrity; live observer correctness | AST validation before execution |
| No ATTACH / DETACH | Prevent escaping to the host filesystem | AST validation |
| No COPY ... TO | Prevent file writes | AST validation |
| No `read_csv_auto` / `read_parquet` to arbitrary paths | Prevent reading host filesystem | AST validation: paths must be relative to bundle |
| Query timeout (e.g. 30s default) | Prevent runaway queries from starving the process | DuckDB query cancellation token |
| Row limit (e.g. 100,000 default) | Prevent runaway memory use in result serialization | SQL `LIMIT` injection if absent |
| Memory limit (e.g. 1 GB) | Defensive | DuckDB session pragma |
| Single-statement only | One query per execution; no script execution | Parser rejects multiple statements |

### 3.2 SqlGuardrails: AST Validation

DuckDB exposes a SQL parser via `DESCRIBE` and `EXPLAIN`, but those don't give us the AST. For Phase 10's purposes, a hand-rolled tokenizer + light parsing is sufficient — we need to recognize statement-level intent, not full grammar. The check:

```csharp
namespace Tracer.WebApi.Queries;

public static class SqlGuardrails
{
    private static readonly HashSet<string> ForbiddenKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "UPDATE", "DELETE", "MERGE",
        "CREATE", "DROP", "ALTER", "TRUNCATE", "RENAME",
        "ATTACH", "DETACH",
        "COPY", "EXPORT", "IMPORT",
        "INSTALL", "LOAD",
        "VACUUM", "ANALYZE",
        "PRAGMA",       // some pragmas are read-only but disallow all for safety
        "BEGIN", "COMMIT", "ROLLBACK",
        "GRANT", "REVOKE",
        "SET",
    };
    
    /// <summary>
    /// Allowed leading keywords for a statement.
    /// </summary>
    private static readonly HashSet<string> AllowedLeadingKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "WITH", "EXPLAIN", "DESCRIBE", "SHOW", "VALUES"
    };
    
    public static SqlValidationResult Validate(string sql)
    {
        // Lightweight tokenization: strip comments, recognize identifiers/keywords
        var stripped = StripComments(sql);
        var tokens = Tokenize(stripped);
        
        if (tokens.Count == 0)
            return SqlValidationResult.Reject("Empty query");
        
        var firstToken = tokens[0];
        if (!AllowedLeadingKeywords.Contains(firstToken.Value))
            return SqlValidationResult.Reject($"Statement must begin with one of {string.Join(", ", AllowedLeadingKeywords)}; saw '{firstToken.Value}'");
        
        // Reject any subsequent semicolon-separated statement
        var semicolonCount = tokens.Count(t => t.Kind == TokenKind.Punctuation && t.Value == ";");
        if (semicolonCount > 1 || (semicolonCount == 1 && !tokens[^1].Equals(new Token(TokenKind.Punctuation, ";"))))
            return SqlValidationResult.Reject("Only single statements are allowed");
        
        // Forbidden keywords anywhere (catches WITH clauses that contain DDL, etc.)
        foreach (var t in tokens)
        {
            if (t.Kind == TokenKind.Keyword && ForbiddenKeywords.Contains(t.Value))
            {
                // Special case: SET is forbidden as a leading keyword but appears in some
                // context-sensitive contexts. We're strict — reject anywhere.
                return SqlValidationResult.Reject($"Forbidden keyword: {t.Value}");
            }
        }
        
        // Reject read_csv_auto/read_parquet with absolute paths or path traversal
        var pathFunctions = new[] { "read_csv_auto", "read_csv", "read_parquet", "read_json", "read_json_auto" };
        for (int i = 0; i < tokens.Count - 1; i++)
        {
            if (tokens[i].Kind == TokenKind.Identifier
                && pathFunctions.Contains(tokens[i].Value, StringComparer.OrdinalIgnoreCase)
                && tokens[i + 1].Kind == TokenKind.Punctuation && tokens[i + 1].Value == "(")
            {
                return SqlValidationResult.Reject(
                    $"Function {tokens[i].Value} is not allowed in user queries; use the existing schema tables instead");
            }
        }
        
        return SqlValidationResult.Accept();
    }
    
    private static string StripComments(string sql)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < sql.Length)
        {
            // Line comment
            if (i + 1 < sql.Length && sql[i] == '-' && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                continue;
            }
            // Block comment
            if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/')) i++;
                i += 2;
                continue;
            }
            sb.Append(sql[i++]);
        }
        return sb.ToString();
    }
    
    private static List<Token> Tokenize(string sql)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            
            // String literal (single-quoted)
            if (c == '\'')
            {
                int start = i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'' && i + 1 < sql.Length && sql[i + 1] == '\'') { i += 2; continue; }  // escape
                    if (sql[i] == '\'') { i++; break; }
                    i++;
                }
                tokens.Add(new Token(TokenKind.StringLiteral, sql.Substring(start, i - start)));
                continue;
            }
            
            // Quoted identifier (double-quoted)
            if (c == '"')
            {
                int start = i++;
                while (i < sql.Length && sql[i] != '"') i++;
                if (i < sql.Length) i++;
                tokens.Add(new Token(TokenKind.Identifier, sql.Substring(start + 1, Math.Max(i - start - 2, 0))));
                continue;
            }
            
            // Number
            if (char.IsDigit(c))
            {
                int start = i;
                while (i < sql.Length && (char.IsDigit(sql[i]) || sql[i] == '.')) i++;
                tokens.Add(new Token(TokenKind.Number, sql.Substring(start, i - start)));
                continue;
            }
            
            // Identifier or keyword
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_')) i++;
                var word = sql.Substring(start, i - start);
                tokens.Add(IsKeyword(word) ? new Token(TokenKind.Keyword, word) : new Token(TokenKind.Identifier, word));
                continue;
            }
            
            // Single-char punctuation
            tokens.Add(new Token(TokenKind.Punctuation, c.ToString()));
            i++;
        }
        return tokens;
    }
    
    private static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "GROUP", "BY", "ORDER", "LIMIT", "OFFSET",
        "INNER", "OUTER", "LEFT", "RIGHT", "FULL", "JOIN", "ON", "USING",
        "AS", "AND", "OR", "NOT", "IN", "IS", "NULL", "BETWEEN", "LIKE",
        "WITH", "RECURSIVE", "UNION", "INTERSECT", "EXCEPT", "ALL", "DISTINCT",
        "CASE", "WHEN", "THEN", "ELSE", "END", "EXISTS",
        "EXPLAIN", "DESCRIBE", "SHOW", "VALUES",
        // Plus forbidden ones for token classification
        "INSERT", "UPDATE", "DELETE", "MERGE",
        "CREATE", "DROP", "ALTER", "TRUNCATE", "RENAME",
        "ATTACH", "DETACH", "COPY", "EXPORT", "IMPORT",
        "INSTALL", "LOAD", "VACUUM", "ANALYZE", "PRAGMA",
        "BEGIN", "COMMIT", "ROLLBACK", "GRANT", "REVOKE", "SET",
        "OVER", "PARTITION", "FOLLOWING", "PRECEDING", "ROW", "ROWS", "RANGE",
        "ASC", "DESC", "INTERVAL", "DATE", "TIME", "TIMESTAMP",
    };
    
    private static bool IsKeyword(string word) => SqlKeywords.Contains(word);
}

public enum TokenKind { Keyword, Identifier, Number, StringLiteral, Punctuation }
public sealed record Token(TokenKind Kind, string Value);

public sealed record SqlValidationResult(bool IsValid, string? RejectionReason)
{
    public static SqlValidationResult Accept() => new(true, null);
    public static SqlValidationResult Reject(string reason) => new(false, reason);
}
```

**Limits of this approach**: a hand-rolled tokenizer doesn't catch every edge case. A determined attacker could write something like `SELECT * FROM events; /* sneaky */ DROP TABLE events`, but:

1. We reject multi-statement
2. Even if a write somehow squeezed through, the bundle DuckDB is opened in read-only mode (Phase 4 §4 — bundles are immutable)
3. The observer's DuckDB attachment also uses read-only mode (Phase 3 §3)

Three layers of defense: AST validation, multi-statement rejection, and read-only file mode. Phase 10 ships with this combination as adequate. A more principled future approach: integrate DuckDB's internal SQL parser via FFI, get a real AST, validate that.

### 3.3 SqlExecutorService

```csharp
namespace Tracer.WebApi.Queries;

public sealed class SqlExecutorService
{
    private readonly LiveMultiIntervalReader _reader;
    private readonly ILogger<SqlExecutorService> _logger;
    private readonly SqlExecutorConfig _config;

    public SqlExecutorService(
        LiveMultiIntervalReader reader,
        SqlExecutorConfig config,
        ILogger<SqlExecutorService> logger)
    {
        _reader = reader;
        _config = config;
        _logger = logger;
    }

    public async Task<SqlExecutionResult> ExecuteAsync(
        SqlExecutionRequest request, CancellationToken outerCt)
    {
        // 1. Validate
        var validation = SqlGuardrails.Validate(request.Sql);
        if (!validation.IsValid)
            return SqlExecutionResult.Rejected(validation.RejectionReason ?? "Invalid query");
        
        // 2. Inject row limit if absent
        var sqlToExecute = EnsureLimit(request.Sql, request.MaxRows ?? _config.DefaultMaxRows);
        
        // 3. Execute with timeout
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds ?? _config.DefaultTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt, timeoutCts.Token);
        
        var sw = Stopwatch.StartNew();
        try
        {
            await using var conn = await _reader.AcquireAsync(linkedCts.Token);
            
            // Apply memory limit for this query
            await SetMemoryLimitAsync(conn, _config.MaxMemoryMb, linkedCts.Token);
            
            await using var cmd = conn.Connection.CreateCommand();
            cmd.CommandText = sqlToExecute;
            cmd.CommandTimeout = (int)(request.TimeoutSeconds ?? _config.DefaultTimeoutSeconds);
            
            // Bind named parameters
            foreach (var (name, value) in request.Parameters ?? new Dictionary<string, object?>())
                cmd.Parameters.Add(new DuckDBParameter(name, value ?? DBNull.Value));
            
            var columns = new List<SqlColumnInfo>();
            var rows = new List<IReadOnlyList<object?>>();
            
            await using var reader = await cmd.ExecuteReaderAsync(linkedCts.Token);
            
            for (int i = 0; i < reader.FieldCount; i++)
                columns.Add(new SqlColumnInfo(reader.GetName(i), reader.GetDataTypeName(i)));
            
            while (await reader.ReadAsync(linkedCts.Token))
            {
                var row = new object?[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                    row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }
            
            sw.Stop();
            return new SqlExecutionResult
            {
                State = SqlExecutionState.Succeeded,
                Columns = columns,
                Rows = rows,
                ElapsedMs = sw.ElapsedMilliseconds,
                Truncated = false,    // we let the SQL LIMIT do the work; explicit ROW count check below
            };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            sw.Stop();
            return new SqlExecutionResult
            {
                State = SqlExecutionState.Timeout,
                ErrorMessage = $"Query exceeded the {request.TimeoutSeconds ?? _config.DefaultTimeoutSeconds}-second budget",
                ElapsedMs = sw.ElapsedMilliseconds,
            };
        }
        catch (DuckDBException dex)
        {
            sw.Stop();
            return new SqlExecutionResult
            {
                State = SqlExecutionState.Failed,
                ErrorMessage = dex.Message,
                ElapsedMs = sw.ElapsedMilliseconds,
            };
        }
    }
    
    public async Task<SqlExplainResult> ExplainAsync(string sql, CancellationToken ct)
    {
        var validation = SqlGuardrails.Validate(sql);
        if (!validation.IsValid)
            return new SqlExplainResult { Failed = true, ErrorMessage = validation.RejectionReason };
        
        await using var conn = await _reader.AcquireAsync(ct);
        await using var cmd = conn.Connection.CreateCommand();
        cmd.CommandText = $"EXPLAIN {sql}";
        
        var sb = new StringBuilder();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            for (int i = 0; i < reader.FieldCount; i++)
                sb.AppendLine(reader.GetValue(i)?.ToString() ?? "");
        }
        return new SqlExplainResult { Failed = false, PlanText = sb.ToString() };
    }
    
    private static string EnsureLimit(string sql, int maxRows)
    {
        // Cheap heuristic: if the SQL ends (after trailing semicolons/whitespace) without "LIMIT" anywhere,
        // append. Otherwise leave it; user-supplied LIMIT may be smaller than ours.
        var trimmed = sql.TrimEnd().TrimEnd(';').TrimEnd();
        var upper = trimmed.ToUpperInvariant();
        if (upper.Contains("LIMIT", StringComparison.Ordinal))
            return sql;
        return $"{trimmed} LIMIT {maxRows}";
    }
    
    private static async Task SetMemoryLimitAsync(PooledMultiIntervalConnection conn, int memoryMb, CancellationToken ct)
    {
        // DuckDB session pragma — internal use; SqlGuardrails rejects PRAGMA from user queries
        await using var cmd = conn.Connection.CreateCommand();
        cmd.CommandText = $"PRAGMA memory_limit='{memoryMb}MB'";
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

public sealed record SqlExecutorConfig
{
    public int DefaultTimeoutSeconds { get; init; } = 30;
    public int DefaultMaxRows { get; init; } = 100_000;
    public int MaxMemoryMb { get; init; } = 1024;
}

public sealed record SqlExecutionRequest
{
    public required string Sql { get; init; }
    public IReadOnlyDictionary<string, object?>? Parameters { get; init; }
    public int? TimeoutSeconds { get; init; }
    public int? MaxRows { get; init; }
}

public enum SqlExecutionState { Succeeded, Failed, Timeout, Rejected }

public sealed record SqlExecutionResult
{
    public required SqlExecutionState State { get; init; }
    public IReadOnlyList<SqlColumnInfo>? Columns { get; init; }
    public IReadOnlyList<IReadOnlyList<object?>>? Rows { get; init; }
    public string? ErrorMessage { get; init; }
    public long ElapsedMs { get; init; }
    public bool Truncated { get; init; }

    public static SqlExecutionResult Rejected(string reason) => new()
    {
        State = SqlExecutionState.Rejected,
        ErrorMessage = reason
    };
}

public sealed record SqlColumnInfo(string Name, string DuckType);

public sealed record SqlExplainResult
{
    public required bool Failed { get; init; }
    public string? PlanText { get; init; }
    public string? ErrorMessage { get; init; }
}
```

### 3.4 Schema Introspection

The autocomplete and Schema Panel need the queryable schema. We introspect once on app boot and cache.

```csharp
namespace Tracer.WebApi.Queries;

public sealed class SqlSchemaService
{
    private readonly LiveMultiIntervalReader _reader;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private SqlSchemaSnapshot? _cache;

    public SqlSchemaService(LiveMultiIntervalReader reader) { _reader = reader; }

    public async Task<SqlSchemaSnapshot> GetAsync(CancellationToken ct)
    {
        if (_cache is not null) return _cache;
        await _refreshLock.WaitAsync(ct);
        try
        {
            if (_cache is not null) return _cache;
            _cache = await BuildAsync(ct);
            return _cache;
        }
        finally { _refreshLock.Release(); }
    }

    public async Task InvalidateAsync()
    {
        await _refreshLock.WaitAsync();
        try { _cache = null; }
        finally { _refreshLock.Release(); }
    }

    private async Task<SqlSchemaSnapshot> BuildAsync(CancellationToken ct)
    {
        await using var conn = await _reader.AcquireAsync(ct);
        
        // Phase 5+ uses multi-interval attachments. For schema introspection we query
        // information_schema once against the connection; the schema is identical across
        // attached intervals (Phase 1 §4 guarantees this).
        
        var tables = new List<SqlTableInfo>();
        
        // Get table list from the first attached interval
        var firstAlias = conn.Intervals.FirstOrDefault()?.Alias;
        if (firstAlias is null) return new SqlSchemaSnapshot { Tables = Array.Empty<SqlTableInfo>(), RefreshedAtUtc = DateTimeOffset.UtcNow };
        
        await using var cmd = conn.Connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT table_name FROM {firstAlias}.information_schema.tables
            WHERE table_schema = 'main'
            ORDER BY table_name;
            """;
        var tableNames = new List<string>();
        await using (var rdr = await cmd.ExecuteReaderAsync(ct))
            while (await rdr.ReadAsync(ct))
                tableNames.Add(rdr.GetString(0));
        
        foreach (var tableName in tableNames)
        {
            var columns = new List<SqlColumnInfo>();
            await using var colCmd = conn.Connection.CreateCommand();
            colCmd.CommandText = $"DESCRIBE {firstAlias}.{tableName}";
            await using var colRdr = await colCmd.ExecuteReaderAsync(ct);
            while (await colRdr.ReadAsync(ct))
                columns.Add(new SqlColumnInfo(colRdr.GetString(0), colRdr.GetString(1)));
            tables.Add(new SqlTableInfo(tableName, columns));
        }
        
        return new SqlSchemaSnapshot
        {
            Tables = tables,
            RefreshedAtUtc = DateTimeOffset.UtcNow,
            DialectNotes = ConstructDialectNotes()
        };
    }

    private static IReadOnlyList<string> ConstructDialectNotes() => new[]
    {
        "Use `events`, `slow_state`, `fast_state` as table names (these are exposed as views over the underlying interval storage)",
        "Functions: time_bucket, approx_quantile, json_extract_string, list_aggregate",
        "Use APPROX_QUANTILE for fast percentile estimates on large data",
        "Use time_bucket(INTERVAL '5 seconds', publish_wallclock) for time series grouping",
    };
}

public sealed record SqlSchemaSnapshot
{
    public required IReadOnlyList<SqlTableInfo> Tables { get; init; }
    public required DateTimeOffset RefreshedAtUtc { get; init; }
    public IReadOnlyList<string> DialectNotes { get; init; } = Array.Empty<string>();
}

public sealed record SqlTableInfo(string Name, IReadOnlyList<SqlColumnInfo> Columns);
```

### 3.5 Exposed-Schema vs Underlying Schema

The user-facing schema is simpler than the actual storage. Phase 5's `MultiIntervalReader` attaches intervals as `iv_*` aliases; queries use `UNION ALL` over them. The SQL Console shouldn't expose this complexity.

**Solution**: Phase 10 creates **DuckDB views** at attachment time that hide the union:

```csharp
// In LiveMultiIntervalReader.BuildAttachedConnectionAsync (Phase 5 §3.3), after attaching:
await using var cmd = conn.CreateCommand();
cmd.CommandText = $"""
    CREATE OR REPLACE VIEW events AS {BuildEventsUnionSql(/* no extra where */)};
    CREATE OR REPLACE VIEW slow_state AS {BuildSlowStateUnionSql(/* no extra where */)};
    """;
await cmd.ExecuteNonQueryAsync(ct);
```

User queries select from `events` and `slow_state` directly. The views are recreated each time the interval set changes (which means on every rebuild).

`fast_state` is more complex because it's split into many Parquet files per (topic, entity). Phase 10 doesn't expose `fast_state` as a unified view — the SQL Console can read directly from `read_parquet('fast_state/{topic}/{entity}/samples.parquet')` for the bundle mode. Documented in dialect notes.

---

## 4. SQL API Endpoints

### 4.1 Endpoint Surface

```
POST /api/sql/execute                            run a SQL query
GET  /api/sql/schema                             tables and columns for autocomplete
POST /api/sql/explain                            EXPLAIN output for the query
```

### 4.2 SqlEndpoints

```csharp
namespace Tracer.WebApi.Endpoints;

public static class SqlEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/sql/execute", HandleExecuteAsync).WithOpenApi();
        app.MapGet ("/api/sql/schema",  HandleSchemaAsync).WithOpenApi();
        app.MapPost("/api/sql/explain", HandleExplainAsync).WithOpenApi();
    }

    public static async Task<Results<Ok<SqlExecuteResultDto>, ProblemHttpResult>> HandleExecuteAsync(
        [FromBody] SqlExecuteRequestDto dto,
        [FromServices] SqlExecutorService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Sql))
            return TypedResults.Problem(new ProblemDetails { Title = "SQL required", Status = 400 });
        
        var result = await service.ExecuteAsync(new SqlExecutionRequest
        {
            Sql = dto.Sql,
            Parameters = dto.Parameters,
            TimeoutSeconds = dto.TimeoutSeconds,
            MaxRows = dto.MaxRows,
        }, ct);
        
        return TypedResults.Ok(SqlDtoMapper.Map(result));
    }

    public static async Task<Ok<SqlSchemaDto>> HandleSchemaAsync(
        [FromServices] SqlSchemaService service,
        CancellationToken ct)
    {
        var snap = await service.GetAsync(ct);
        return TypedResults.Ok(SqlDtoMapper.Map(snap));
    }

    public static async Task<Results<Ok<SqlExplainResultDto>, ProblemHttpResult>> HandleExplainAsync(
        [FromBody] SqlExplainRequestDto dto,
        [FromServices] SqlExecutorService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Sql))
            return TypedResults.Problem(new ProblemDetails { Title = "SQL required", Status = 400 });
        
        var result = await service.ExplainAsync(dto.Sql, ct);
        return result.Failed
            ? TypedResults.Problem(new ProblemDetails { Title = "Cannot explain", Detail = result.ErrorMessage, Status = 400 })
            : TypedResults.Ok(new SqlExplainResultDto { PlanText = result.PlanText ?? "" });
    }
}
```

### 4.3 DTOs

```csharp
namespace Tracer.WebApi.Contracts.Dto;

public sealed record SqlExecuteRequestDto
{
    public required string Sql { get; init; }
    public IReadOnlyDictionary<string, object?>? Parameters { get; init; }
    public int? TimeoutSeconds { get; init; }
    public int? MaxRows { get; init; }
}

public sealed record SqlExecuteResultDto
{
    public required string State { get; init; }    // "Succeeded" | "Failed" | "Timeout" | "Rejected"
    public IReadOnlyList<SqlColumnInfoDto>? Columns { get; init; }
    public IReadOnlyList<IReadOnlyList<object?>>? Rows { get; init; }
    public string? ErrorMessage { get; init; }
    public required long ElapsedMs { get; init; }
    public required bool Truncated { get; init; }
}

public sealed record SqlColumnInfoDto
{
    public required string Name { get; init; }
    public required string DuckType { get; init; }
}

public sealed record SqlSchemaDto
{
    public required IReadOnlyList<SqlTableInfoDto> Tables { get; init; }
    public required DateTimeOffset RefreshedAtUtc { get; init; }
    public required IReadOnlyList<string> DialectNotes { get; init; }
}

public sealed record SqlTableInfoDto
{
    public required string Name { get; init; }
    public required IReadOnlyList<SqlColumnInfoDto> Columns { get; init; }
}

public sealed record SqlExplainRequestDto { public required string Sql { get; init; } }
public sealed record SqlExplainResultDto  { public required string PlanText { get; init; } }
```

### 4.4 Wiring

In `ObserverHostBuilder` and `OfflineViewerHostBuilder`:

```csharp
builder.Services.AddSingleton<SqlExecutorService>();
builder.Services.AddSingleton<SqlSchemaService>();
builder.Services.AddSingleton(new SqlExecutorConfig
{
    DefaultTimeoutSeconds = 30,
    DefaultMaxRows = 100_000,
    MaxMemoryMb = 1024,
});

// In ConfigureMiddleware:
SqlEndpoints.Map(app);
```

---

## 5. SQL Console: Frontend

### 5.1 SqlConsoleView Layout

```
+----------------------------------------------------------------+
| AppHeader                                                       |
+----------------------------------------------------------------+
| Toolbar: [Run] [Save] [Explain] [Cancel]      [Built-in queries] |
+------------+----------------------------------+-----------------+
|            |                                  |                 |
|  Schema    |    SQL Editor                    |   History       |
|  Panel     |    (CodeMirror 6)                |   (last 50)     |
|            |                                  |                 |
|  events    |                                  |                 |
|   trace_id |                                  |                 |
|   ...      +----------------------------------+                 |
|  slow_state|    Results                       |                 |
|   ...      |    (table or chart)              |                 |
|            |                                  |                 |
|            |    [Export CSV] [Export JSON]    |                 |
|            |    [⛏ Open in timeline] (row)    |                 |
|            |                                  |                 |
+------------+----------------------------------+-----------------+
```

- **Schema Panel** (left): collapsible tree of tables and columns. Click a table → inserts `FROM table_name` at cursor. Click a column → inserts the column name.
- **Editor** (top center): CodeMirror 6 with SQL syntax highlighting, autocomplete from schema, Cmd+Enter to run.
- **History** (right): the last 50 queries run in this session (localStorage-backed). Click → load into editor.
- **Results** (bottom center): tabular by default. Tabs to switch to chart view when result has plottable shape.

### 5.2 SqlEditor.vue

CodeMirror 6 wrapped as a Vue component.

```vue
<!-- src/components/SqlEditor.vue -->
<script setup lang="ts">
import { ref, onMounted, watch, onBeforeUnmount } from 'vue';
import { EditorState } from '@codemirror/state';
import { EditorView, keymap, lineNumbers, highlightActiveLine } from '@codemirror/view';
import { sql, SQLite } from '@codemirror/lang-sql';
import { autocompletion, type CompletionContext } from '@codemirror/autocomplete';
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands';
import { oneDark } from '@codemirror/theme-one-dark';
import { searchKeymap } from '@codemirror/search';
import type { SqlSchemaDto } from '@/types/sql';

const props = defineProps<{
  modelValue: string;
  schema: SqlSchemaDto | null;
}>();

const emit = defineEmits<{
  'update:modelValue': [v: string];
  run: [];
}>();

const editorContainer = ref<HTMLDivElement | null>(null);
let editor: EditorView | null = null;

function customCompletions(context: CompletionContext) {
  const word = context.matchBefore(/\w+/);
  if (!word || (word.from === word.to && !context.explicit)) return null;
  
  const completions: { label: string; type: string; info?: string }[] = [];
  
  // Tables and columns from schema
  if (props.schema) {
    for (const t of props.schema.tables) {
      completions.push({ label: t.name, type: 'class', info: `Table (${t.columns.length} columns)` });
      for (const c of t.columns) {
        completions.push({ label: c.name, type: 'property', info: `${t.name}.${c.name} (${c.duckType})` });
      }
    }
  }
  
  // DuckDB function highlights
  for (const fn of [
    { label: 'time_bucket', type: 'function', info: 'Group timestamps into buckets' },
    { label: 'approx_quantile', type: 'function', info: 'Streaming quantile estimate' },
    { label: 'json_extract_string', type: 'function', info: 'Extract a string from JSON' },
  ]) completions.push(fn);
  
  return { from: word.from, options: completions };
}

onMounted(() => {
  if (!editorContainer.value) return;
  
  const state = EditorState.create({
    doc: props.modelValue,
    extensions: [
      lineNumbers(),
      highlightActiveLine(),
      history(),
      sql({ dialect: SQLite }),  // DuckDB is SQLite-compatible enough for highlighting
      autocompletion({ override: [customCompletions] }),
      oneDark,
      keymap.of([
        ...defaultKeymap,
        ...historyKeymap,
        ...searchKeymap,
        {
          key: 'Mod-Enter',
          run: () => { emit('run'); return true; }
        },
      ]),
      EditorView.updateListener.of((update) => {
        if (update.docChanged) {
          emit('update:modelValue', update.state.doc.toString());
        }
      }),
    ],
  });
  
  editor = new EditorView({ state, parent: editorContainer.value });
});

watch(() => props.modelValue, (v) => {
  if (editor && editor.state.doc.toString() !== v) {
    editor.dispatch({
      changes: { from: 0, to: editor.state.doc.length, insert: v }
    });
  }
});

onBeforeUnmount(() => {
  editor?.destroy();
  editor = null;
});

defineExpose({
  focus: () => editor?.focus(),
  getSelection: () => editor?.state.selection.main,
});
</script>

<template>
  <div ref="editorContainer" class="sql-editor" />
</template>

<style lang="scss">
.sql-editor {
  min-height: 240px;
  border-radius: 8px;
  overflow: hidden;
  font-size: 0.875rem;
  
  .cm-editor {
    background: var(--c-bg-surface);
    height: 100%;
  }
  .cm-scroller { font-family: var(--font-mono); }
}
</style>
```

### 5.3 SqlConsoleView.vue

The view orchestrates editor, execution, results, history, and save-query interactions.

```vue
<!-- src/views/SqlConsoleView.vue -->
<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useApi } from '@/api/useApi';
import { useSqlExecution } from '@/composables/useSqlExecution';
import { useSqlSchema } from '@/composables/useSqlSchema';
import SqlEditor from '@/components/SqlEditor.vue';
import SqlResultTable from '@/components/SqlResultTable.vue';
import SqlResultChart from '@/components/SqlResultChart.vue';
import SchemaPanel from '@/components/SchemaPanel.vue';
import SavedQueryPicker from '@/components/SavedQueryPicker.vue';
import type { SqlExecuteResultDto } from '@/types/sql';

const route = useRoute();
const router = useRouter();
const api = useApi();
const sessionId = computed(() => route.params.sessionId as string);

const editor = ref<InstanceType<typeof SqlEditor> | null>(null);
const sql = ref<string>(loadInitialSql());
const { schema } = useSqlSchema();
const { result, loading, error, run, cancel } = useSqlExecution();
const showSavedQueries = ref(false);
const resultTab = ref<'table' | 'chart'>('table');
const history = ref<string[]>(loadHistory());

function loadInitialSql(): string {
  // Prefer URL ?sql= param (used by Show-SQL-for-this-view affordance)
  if (route.query.sql) return route.query.sql as string;
  // Fall back to last history entry or a placeholder
  const h = loadHistory();
  return h[0] ?? '-- Try: SELECT topic, COUNT(*) FROM events GROUP BY topic ORDER BY 2 DESC LIMIT 10;\n';
}

function loadHistory(): string[] {
  try {
    return JSON.parse(localStorage.getItem('tracer:sqlHistory') ?? '[]');
  } catch { return []; }
}

function persistHistory() {
  localStorage.setItem('tracer:sqlHistory', JSON.stringify(history.value.slice(0, 50)));
}

async function execute() {
  if (!sql.value.trim()) return;
  await run(sql.value, { timeoutSeconds: 30, maxRows: 100_000 });
  if (result.value?.state === 'Succeeded') {
    // Add to history
    history.value = [sql.value, ...history.value.filter(s => s !== sql.value)].slice(0, 50);
    persistHistory();
  }
}

async function explain() {
  try {
    const explainResult = await api.explainSql({ sql: sql.value });
    alert(explainResult.planText);  // TODO: better UI; modal panel
  } catch (err: any) {
    alert(err.message ?? 'Failed to explain');
  }
}

function loadQuery(s: string) { sql.value = s; editor.value?.focus(); }
function loadSavedQuery(q: { sql: string }) { sql.value = q.sql; showSavedQueries.value = false; editor.value?.focus(); }

function isChartable(r: SqlExecuteResultDto | null): boolean {
  if (!r?.columns || r.columns.length < 2) return false;
  // Needs at least one numeric column
  const numericTypes = ['DOUBLE', 'FLOAT', 'INTEGER', 'BIGINT', 'DECIMAL', 'HUGEINT'];
  return r.columns.some(c => numericTypes.some(t => c.duckType.toUpperCase().includes(t)));
}

onMounted(() => editor.value?.focus());
</script>

<template>
  <div class="sql-console">
    <header class="sql-console__toolbar">
      <button class="sql-console__btn sql-console__btn--primary" @click="execute" :disabled="loading">
        {{ loading ? 'Running…' : 'Run (Cmd+Enter)' }}
      </button>
      <button v-if="loading" class="sql-console__btn" @click="cancel">Cancel</button>
      <button class="sql-console__btn" @click="explain">Explain</button>
      <button class="sql-console__btn" @click="showSavedQueries = true">Saved queries…</button>
      <span class="sql-console__elapsed" v-if="result?.elapsedMs">{{ result.elapsedMs }} ms</span>
    </header>
    
    <div class="sql-console__grid">
      <SchemaPanel :schema="schema" @insert="text => sql += text" class="sql-console__schema" />
      <SqlEditor
        ref="editor"
        v-model="sql"
        :schema="schema"
        class="sql-console__editor"
        @run="execute"
      />
      <aside class="sql-console__history">
        <h4>History</h4>
        <ul>
          <li v-for="(h, i) in history" :key="i" @click="loadQuery(h)" :title="h">
            {{ h.split('\n')[0].slice(0, 60) }}{{ h.length > 60 ? '…' : '' }}
          </li>
        </ul>
      </aside>
      
      <section v-if="result" class="sql-console__results">
        <div v-if="result.state === 'Succeeded'">
          <nav class="sql-console__tabs">
            <button :class="{ active: resultTab === 'table' }" @click="resultTab = 'table'">Table</button>
            <button :class="{ active: resultTab === 'chart' }"
                    @click="resultTab = 'chart'"
                    :disabled="!isChartable(result)">
              Chart
            </button>
          </nav>
          <SqlResultTable
            v-if="resultTab === 'table'"
            :result="result"
            :session-id="sessionId"
            class="sql-console__table"
          />
          <SqlResultChart
            v-else
            :result="result"
            class="sql-console__chart"
          />
        </div>
        <div v-else-if="result.state === 'Failed'" class="sql-console__error">
          {{ result.errorMessage }}
        </div>
        <div v-else-if="result.state === 'Timeout'" class="sql-console__error">
          {{ result.errorMessage }}
        </div>
        <div v-else-if="result.state === 'Rejected'" class="sql-console__error">
          Rejected: {{ result.errorMessage }}
        </div>
      </section>
    </div>
    
    <SavedQueryPicker
      v-if="showSavedQueries"
      @select="loadSavedQuery"
      @cancel="showSavedQueries = false"
    />
  </div>
</template>

<style lang="scss">
.sql-console {
  max-width: 1800px;
  margin: 0 auto;
  padding: 1.5rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
  height: calc(100vh - 4rem);
  
  &__toolbar {
    display: flex;
    align-items: center;
    gap: 0.5rem;
  }
  
  &__btn {
    padding: 0.5rem 1rem;
    background: var(--c-bg-subtle);
    border: none;
    border-radius: 6px;
    color: var(--c-text);
    cursor: pointer;
    
    &--primary { background: var(--c-accent); color: white; }
    &:disabled { opacity: 0.5; cursor: not-allowed; }
  }
  
  &__elapsed {
    margin-left: auto;
    color: var(--c-text-muted);
    font-family: var(--font-mono);
    font-size: 0.875rem;
  }
  
  &__grid {
    display: grid;
    grid-template-columns: 240px 1fr 240px;
    grid-template-rows: minmax(240px, 1fr) 2fr;
    grid-template-areas:
      "schema editor history"
      "schema results history";
    gap: 1rem;
    flex: 1;
    min-height: 0;
  }
  
  &__schema  { grid-area: schema; overflow: auto; }
  &__editor  { grid-area: editor; }
  &__history { grid-area: history; overflow: auto; }
  &__results { grid-area: results; overflow: auto; background: var(--c-bg-surface); border-radius: 8px; padding: 1rem; }
  
  &__history ul { list-style: none; padding: 0; margin: 0; }
  &__history li {
    padding: 0.25rem 0.5rem;
    border-radius: 4px;
    cursor: pointer;
    font-family: var(--font-mono);
    font-size: 0.75rem;
    color: var(--c-text-muted);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    &:hover { background: var(--c-bg-subtle); color: var(--c-text); }
  }
  
  &__tabs {
    display: flex;
    gap: 0.5rem;
    margin-bottom: 0.5rem;
    button {
      background: none;
      border: none;
      padding: 0.5rem 1rem;
      color: var(--c-text-muted);
      cursor: pointer;
      border-bottom: 2px solid transparent;
      &.active { color: var(--c-text); border-bottom-color: var(--c-accent); }
      &:disabled { opacity: 0.5; cursor: not-allowed; }
    }
  }
  
  &__error {
    padding: 1rem;
    background: rgba(232, 92, 92, 0.08);
    border: 1px solid var(--c-danger);
    border-radius: 6px;
    color: var(--c-danger);
    font-family: var(--font-mono);
    font-size: 0.875rem;
    white-space: pre-wrap;
  }
}
</style>
```

### 5.4 SqlResultTable

A scrollable, sortable table. Pivot affordances for rows that contain known columns.

```vue
<!-- src/components/SqlResultTable.vue -->
<script setup lang="ts">
import { computed, ref } from 'vue';
import { useRouter } from 'vue-router';
import type { SqlExecuteResultDto } from '@/types/sql';

const props = defineProps<{
  result: SqlExecuteResultDto;
  sessionId: string;
}>();

const router = useRouter();
const sortColumn = ref<number | null>(null);
const sortDescending = ref(false);

const PIVOT_COLUMNS = ['event_id', 'entity_id', 'trace_id', 'publish_wallclock'];
const pivotableColumns = computed(() =>
  props.result.columns?.map((c, i) =>
    PIVOT_COLUMNS.includes(c.name.toLowerCase()) ? i : -1
  ).filter(i => i >= 0) ?? []
);

const sortedRows = computed(() => {
  if (sortColumn.value === null || !props.result.rows) return props.result.rows;
  const col = sortColumn.value;
  const rows = [...props.result.rows];
  rows.sort((a, b) => {
    const va = a[col], vb = b[col];
    if (va === null && vb === null) return 0;
    if (va === null) return 1;
    if (vb === null) return -1;
    if (typeof va === 'number' && typeof vb === 'number') return va - vb;
    return String(va).localeCompare(String(vb));
  });
  if (sortDescending.value) rows.reverse();
  return rows;
});

function toggleSort(col: number) {
  if (sortColumn.value === col) sortDescending.value = !sortDescending.value;
  else { sortColumn.value = col; sortDescending.value = false; }
}

function pivot(row: any[], colIdx: number) {
  const col = props.result.columns![colIdx];
  const value = row[colIdx];
  const colNameLower = col.name.toLowerCase();
  
  if (colNameLower === 'event_id') {
    router.push({ name: 'timeline', params: { sessionId: props.sessionId }, query: { select: String(value) } });
  } else if (colNameLower === 'entity_id') {
    router.push({ name: 'entity-history', params: { entityId: String(value) }, query: { session: props.sessionId } });
  } else if (colNameLower === 'trace_id') {
    router.push({ name: 'causal-by-trace', params: { traceId: String(value) } });
  } else if (colNameLower === 'publish_wallclock') {
    const t = new Date(String(value)).getTime();
    router.push({
      name: 'timeline', params: { sessionId: props.sessionId },
      query: { from: new Date(t - 2000).toISOString(), to: new Date(t + 2000).toISOString() }
    });
  }
}

function exportCsv() {
  if (!props.result.columns || !props.result.rows) return;
  const header = props.result.columns.map(c => csvEscape(c.name)).join(',');
  const lines = props.result.rows.map(row =>
    row.map(v => csvEscape(v === null ? '' : String(v))).join(',')
  );
  const csv = [header, ...lines].join('\n');
  const blob = new Blob([csv], { type: 'text/csv' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url; a.download = 'query-result.csv';
  a.click();
  URL.revokeObjectURL(url);
}

function csvEscape(v: string): string {
  if (/[,"\n]/.test(v)) return `"${v.replace(/"/g, '""')}"`;
  return v;
}
</script>

<template>
  <div class="sql-result-table">
    <header class="sql-result-table__header">
      <span>{{ result.rows?.length ?? 0 }} rows</span>
      <button class="sql-result-table__export" @click="exportCsv">Export CSV</button>
    </header>
    <div class="sql-result-table__scroll">
      <table>
        <thead>
          <tr>
            <th v-for="(c, i) in result.columns" :key="i" @click="toggleSort(i)">
              {{ c.name }}
              <span class="sql-result-table__type">({{ c.duckType }})</span>
              <span v-if="sortColumn === i" class="sql-result-table__sort">
                {{ sortDescending ? '↓' : '↑' }}
              </span>
            </th>
            <th v-if="pivotableColumns.length > 0">⛏</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(row, i) in sortedRows" :key="i">
            <td v-for="(v, j) in row" :key="j">
              {{ v === null ? '∅' : v }}
            </td>
            <td v-if="pivotableColumns.length > 0">
              <button
                v-for="colIdx in pivotableColumns"
                :key="colIdx"
                class="sql-result-table__pivot"
                @click="pivot(row, colIdx)"
                :title="`Pivot to ${result.columns![colIdx].name}`"
              >
                {{ result.columns![colIdx].name.split('_')[0] }} →
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
```

---

## 6. Saved Queries

### 6.1 Data Model

```csharp
namespace Tracer.Storage.SavedQueries;

public sealed record SavedQueryRecord
{
    public required string SavedQueryId { get; init; }       // ULID
    public required string Label { get; init; }
    public string? Description { get; init; }
    public required string Sql { get; init; }
    public IReadOnlyList<SavedQueryParameter> Parameters { get; init; } = Array.Empty<SavedQueryParameter>();
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public required bool IsBuiltIn { get; init; }            // true for the curated set
    public required bool IsFavorite { get; init; }
    public string? Author { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? LastRunAtUtc { get; init; }
    public required int RunCount { get; init; }
}

public sealed record SavedQueryParameter
{
    public required string Name { get; init; }
    public required string DuckType { get; init; }       // e.g., "VARCHAR", "BIGINT"
    public required string DefaultValueText { get; init; }  // text form; UI parses
    public string? Description { get; init; }
}
```

A saved query may have **parameters** — named placeholders that the SQL references via `$paramName`. For example:

```sql
SELECT * FROM events
WHERE topic = $topic
  AND publish_wallclock >= $from
  AND publish_wallclock <  $to
ORDER BY publish_wallclock
LIMIT 100;
```

With parameters:
- `topic` (VARCHAR, default: `weapons.fire`)
- `from` (TIMESTAMP, default: `now - 1 hour`)
- `to` (TIMESTAMP, default: `now`)

When the user opens this query, they see a parameter panel with inputs pre-filled with defaults. They edit values, hit Run, the executor binds the parameters.

### 6.2 Schema Extension

Add to the Phase 8 SQLite database (lives in the same `annotations.db`):

```sql
CREATE TABLE IF NOT EXISTS saved_queries (
    saved_query_id    TEXT PRIMARY KEY,
    label             TEXT NOT NULL,
    description       TEXT,
    sql_text          TEXT NOT NULL,
    parameters_json   TEXT NOT NULL DEFAULT '[]',
    tags_json         TEXT NOT NULL DEFAULT '[]',
    is_built_in       INTEGER NOT NULL DEFAULT 0,
    is_favorite       INTEGER NOT NULL DEFAULT 0,
    author            TEXT,
    created_at        TEXT NOT NULL,
    last_run_at       TEXT,
    run_count         INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_saved_queries_label ON saved_queries (label);
CREATE INDEX IF NOT EXISTS idx_saved_queries_favorite ON saved_queries (is_favorite);
```

The store implementation (`SqliteSavedQueryStore`) follows the Phase 8 pattern; details elided.

### 6.3 Built-in Queries

Ships with the system at install time. JSON file at `Tracer.Storage.SavedQueries/BuiltIn/builtin-queries.json`:

```json
[
  {
    "id": "builtin-top-topics-by-volume",
    "label": "Top topics by event count",
    "description": "Lists the most prolific topics in the current session",
    "sql": "SELECT topic, COUNT(*) AS event_count\nFROM events\nWHERE publish_wallclock >= $from AND publish_wallclock < $to\nGROUP BY topic\nORDER BY event_count DESC\nLIMIT 20;",
    "parameters": [
      { "name": "from", "duckType": "TIMESTAMP", "defaultValueText": "session_start", "description": "Time range start" },
      { "name": "to",   "duckType": "TIMESTAMP", "defaultValueText": "session_end",   "description": "Time range end" }
    ],
    "tags": ["overview", "topics"]
  },
  {
    "id": "builtin-events-by-trace",
    "label": "Events on a trace",
    "description": "Lists all events sharing a trace_id, ordered chronologically",
    "sql": "SELECT event_id, publisher_node, topic, publish_wallclock\nFROM events\nWHERE trace_id = $trace_id\nORDER BY publish_wallclock;",
    "parameters": [
      { "name": "trace_id", "duckType": "UBIGINT", "defaultValueText": "0", "description": "16-char hex trace ID, or decimal" }
    ],
    "tags": ["traces", "lineage"]
  },
  {
    "id": "builtin-event-counts-per-node",
    "label": "Event counts per node",
    "description": "Volume of events published per node",
    "sql": "SELECT publisher_node, COUNT(*) AS event_count\nFROM events\nWHERE publish_wallclock >= $from AND publish_wallclock < $to\nGROUP BY publisher_node\nORDER BY event_count DESC;",
    "parameters": [
      { "name": "from", "duckType": "TIMESTAMP", "defaultValueText": "session_start" },
      { "name": "to",   "duckType": "TIMESTAMP", "defaultValueText": "session_end" }
    ],
    "tags": ["overview", "nodes"]
  },
  {
    "id": "builtin-latency-distribution-by-topic",
    "label": "Latency distribution by topic (bundle only)",
    "description": "Per-topic percentiles. Bundle mode only; in live mode the events have no per-subscriber receive times.",
    "sql": "SELECT topic,\n  COUNT(*) AS samples,\n  APPROX_QUANTILE((EXTRACT(EPOCH FROM receive_wallclock - publish_wallclock) * 1000), 0.50) AS p50_ms,\n  APPROX_QUANTILE((EXTRACT(EPOCH FROM receive_wallclock - publish_wallclock) * 1000), 0.99) AS p99_ms\nFROM events\nWHERE publisher_node != subscriber_node\n  AND publish_wallclock >= $from AND publish_wallclock < $to\nGROUP BY topic\nORDER BY p99_ms DESC\nLIMIT 30;",
    "parameters": [
      { "name": "from", "duckType": "TIMESTAMP", "defaultValueText": "session_start" },
      { "name": "to",   "duckType": "TIMESTAMP", "defaultValueText": "session_end" }
    ],
    "tags": ["latency", "performance"]
  },
  {
    "id": "builtin-entity-events",
    "label": "Events touching an entity",
    "description": "All events referencing a particular entity_id",
    "sql": "SELECT event_id, topic, publisher_node, publish_wallclock\nFROM events\nWHERE entity_id = $entity_id\n  AND publish_wallclock >= $from AND publish_wallclock < $to\nORDER BY publish_wallclock;",
    "parameters": [
      { "name": "entity_id", "duckType": "VARCHAR", "defaultValueText": "" },
      { "name": "from",      "duckType": "TIMESTAMP", "defaultValueText": "session_start" },
      { "name": "to",        "duckType": "TIMESTAMP", "defaultValueText": "session_end" }
    ],
    "tags": ["entities"]
  }
]
```

Loader on startup:

```csharp
namespace Tracer.Storage.SavedQueries.BuiltIn;

public static class BuiltInLoader
{
    public static async Task EnsureLoadedAsync(ISavedQueryStore store, CancellationToken ct)
    {
        var existing = await store.ListAsync(new SavedQueryFilter { IsBuiltIn = true }, ct);
        var existingIds = existing.Select(q => q.SavedQueryId).ToHashSet();
        
        var resourceStream = typeof(BuiltInLoader).Assembly.GetManifestResourceStream(
            "Tracer.Storage.SavedQueries.BuiltIn.builtin-queries.json");
        if (resourceStream is null) return;
        
        var dtos = await JsonSerializer.DeserializeAsync<List<BuiltInQueryDto>>(resourceStream, cancellationToken: ct);
        if (dtos is null) return;
        
        foreach (var dto in dtos)
        {
            if (existingIds.Contains(dto.Id)) continue;
            await store.CreateAsync(new SavedQueryRecord
            {
                SavedQueryId = dto.Id,
                Label = dto.Label,
                Description = dto.Description,
                Sql = dto.Sql,
                Parameters = dto.Parameters,
                Tags = dto.Tags,
                IsBuiltIn = true,
                IsFavorite = false,
                Author = "tracer",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                RunCount = 0,
            }, ct);
        }
    }
    
    private sealed record BuiltInQueryDto(
        string Id, string Label, string? Description, string Sql,
        IReadOnlyList<SavedQueryParameter> Parameters, IReadOnlyList<string> Tags);
}
```

Built-in queries are loaded into the SQLite store on first observer/viewer startup. The user sees them in the saved queries list but cannot edit or delete them (the API rejects modifications when `is_built_in = 1`). They can **clone** to an editable copy.

### 6.4 Saved Query Endpoints

```
GET    /api/saved-queries                          list (filterable by tag, favorite, builtIn)
POST   /api/saved-queries                          create
GET    /api/saved-queries/{id}                     read
PUT    /api/saved-queries/{id}                     update (rejected for built-ins)
DELETE /api/saved-queries/{id}                     delete (rejected for built-ins)
POST   /api/saved-queries/{id}/favorite            toggle favorite
POST   /api/saved-queries/{id}/clone               clone a query (creates an editable copy)
POST   /api/saved-queries/{id}/run                 increments run_count, updates last_run_at
```

The shape mirrors Phase 8's `/api/saved-views` (same patterns, same SQLite store). Details elided.

### 6.5 Parameter Default Resolution

The default value strings support a few special tokens evaluated server-side at execution time:

| Token | Resolves to |
|---|---|
| `session_start` | The session's `startUtc` |
| `session_end` | The session's `endUtc` or `now()` |
| `now` | `DateTimeOffset.UtcNow` |
| `1 hour ago`, `1 day ago`, etc. | Relative time |
| Any literal (e.g., `"weapons.fire"`, `42`, `2026-05-21T14:00:00Z`) | Parsed directly |

The frontend resolves these client-side when populating the parameter panel; the user can override before running.

---

## 7. Bundle Library Enhancements

### 7.1 The Bundle Card

```vue
<!-- src/components/BundleCard.vue -->
<script setup lang="ts">
import { computed } from 'vue';
import { useRouter } from 'vue-router';
import type { BundleLibraryEntryDto } from '@/types/bundle';
import { formatBytes, formatRelative } from '@/utils/format';

const props = defineProps<{ bundle: BundleLibraryEntryDto }>();
const emit = defineEmits<{
  open: [];
  edit: [];
  delete: [];
  archive: [];
  export: [];
}>();

const router = useRouter();

const isStale = computed(() => {
  if (!props.bundle.lastOpenedAtUtc) return true;
  const ageDays = (Date.now() - new Date(props.bundle.lastOpenedAtUtc).getTime()) / 86400_000;
  return ageDays > 30;
});
</script>

<template>
  <article class="bundle-card" :class="{ 'bundle-card--archived': bundle.isArchived }">
    <header class="bundle-card__header">
      <h3 class="bundle-card__label">{{ bundle.label ?? '(unlabeled)' }}</h3>
      <span class="bundle-card__archive-badge" v-if="bundle.isArchived">Archived</span>
    </header>
    
    <p v-if="bundle.description" class="bundle-card__description">{{ bundle.description }}</p>
    
    <dl class="bundle-card__meta">
      <div>
        <dt>Session</dt>
        <dd>{{ formatRange(bundle.sessionStartUtc, bundle.sessionEndUtc) }}</dd>
      </div>
      <div>
        <dt>Built</dt>
        <dd>{{ formatRelative(bundle.builtAtUtc) }}</dd>
      </div>
      <div>
        <dt>Size</dt>
        <dd>{{ formatBytes(bundle.sizeBytes) }}</dd>
      </div>
      <div v-if="bundle.lastOpenedAtUtc">
        <dt>Last opened</dt>
        <dd :class="{ 'bundle-card__stale': isStale }">{{ formatRelative(bundle.lastOpenedAtUtc) }}</dd>
      </div>
    </dl>
    
    <div v-if="bundle.tags.length > 0" class="bundle-card__tags">
      <span v-for="t in bundle.tags" :key="t" class="bundle-card__tag">{{ t }}</span>
    </div>
    
    <footer class="bundle-card__actions">
      <button class="bundle-card__open" @click="emit('open')">Open</button>
      <button @click="emit('edit')" title="Edit label, description, tags">Edit</button>
      <button @click="emit('export')" title="Download as zip">Export</button>
      <button v-if="!bundle.isArchived" @click="emit('archive')">Archive</button>
      <button class="bundle-card__delete" @click="emit('delete')">Delete</button>
    </footer>
  </article>
</template>
```

### 7.2 BundleLibraryView

```vue
<!-- src/views/BundleLibraryView.vue -->
<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useApi } from '@/api/useApi';
import BundleCard from '@/components/BundleCard.vue';
import BundleFilterPanel from '@/components/BundleFilterPanel.vue';
import BundleMetadataEditor from '@/components/BundleMetadataEditor.vue';
import type { BundleLibraryEntryDto } from '@/types/bundle';

const api = useApi();
const bundles = ref<BundleLibraryEntryDto[]>([]);
const editing = ref<BundleLibraryEntryDto | null>(null);
const filter = ref({
  tags: [] as string[],
  fromDate: null as Date | null,
  toDate: null as Date | null,
  showArchived: false,
  query: '',
});
const sort = ref<{ field: 'builtAt' | 'sessionStart' | 'size' | 'label'; descending: boolean }>({
  field: 'builtAt', descending: true
});

const filtered = computed(() => {
  let list = [...bundles.value];
  if (!filter.value.showArchived) list = list.filter(b => !b.isArchived);
  if (filter.value.tags.length > 0)
    list = list.filter(b => filter.value.tags.every(t => b.tags.includes(t)));
  if (filter.value.fromDate)
    list = list.filter(b => new Date(b.sessionStartUtc) >= filter.value.fromDate!);
  if (filter.value.toDate)
    list = list.filter(b => new Date(b.sessionStartUtc) <= filter.value.toDate!);
  if (filter.value.query) {
    const q = filter.value.query.toLowerCase();
    list = list.filter(b =>
      (b.label?.toLowerCase().includes(q)) ||
      (b.description?.toLowerCase().includes(q)) ||
      (b.tags.some(t => t.toLowerCase().includes(q))));
  }
  
  list.sort((a, b) => {
    const f = sort.value.field;
    let cmp = 0;
    switch (f) {
      case 'builtAt':       cmp = new Date(a.builtAtUtc).getTime() - new Date(b.builtAtUtc).getTime(); break;
      case 'sessionStart':  cmp = new Date(a.sessionStartUtc).getTime() - new Date(b.sessionStartUtc).getTime(); break;
      case 'size':          cmp = a.sizeBytes - b.sizeBytes; break;
      case 'label':         cmp = (a.label ?? '').localeCompare(b.label ?? ''); break;
    }
    return sort.value.descending ? -cmp : cmp;
  });
  
  return list;
});

const allTags = computed(() => {
  const set = new Set<string>();
  for (const b of bundles.value) for (const t of b.tags) set.add(t);
  return Array.from(set).sort();
});

async function load() {
  const result = await api.listBundleLibrary();
  bundles.value = result.entries;
}

async function openBundle(b: BundleLibraryEntryDto) {
  await api.recordBundleOpened(b.bundleId);
  window.location.href = `/v/scenario/${b.sessionId}`;  // routes the user to their session
}

async function saveMetadata(payload: { label?: string; description?: string; tags?: string[] }) {
  if (!editing.value) return;
  await api.updateBundleMetadata(editing.value.bundleId, payload);
  await load();
  editing.value = null;
}

async function archive(b: BundleLibraryEntryDto) {
  await api.archiveBundle(b.bundleId, true);
  await load();
}

async function deleteBundle(b: BundleLibraryEntryDto) {
  if (!confirm(`Delete bundle "${b.label ?? b.bundleId}"? This cannot be undone.`)) return;
  await api.deleteBundle(b.bundleId);
  await load();
}

async function exportBundle(b: BundleLibraryEntryDto) {
  window.location.href = `/api/bundles/${b.bundleId}/download`;
}

onMounted(load);
</script>

<template>
  <div class="bundle-library">
    <header class="bundle-library__header">
      <h1>Bundle library</h1>
      <input v-model="filter.query" placeholder="Search…" class="bundle-library__search" />
      <select v-model="sort.field">
        <option value="builtAt">Sort: Built date</option>
        <option value="sessionStart">Sort: Session date</option>
        <option value="size">Sort: Size</option>
        <option value="label">Sort: Label</option>
      </select>
      <button @click="sort.descending = !sort.descending">{{ sort.descending ? '↓' : '↑' }}</button>
    </header>
    
    <div class="bundle-library__grid">
      <BundleFilterPanel
        :tags="allTags"
        v-model:filter="filter"
        class="bundle-library__filter"
      />
      <main class="bundle-library__main">
        <div v-if="filtered.length === 0" class="bundle-library__empty">
          {{ bundles.length === 0 ? 'No bundles yet.' : 'No bundles match the filter.' }}
        </div>
        <div v-else class="bundle-library__cards">
          <BundleCard
            v-for="b in filtered"
            :key="b.bundleId"
            :bundle="b"
            @open="openBundle(b)"
            @edit="editing = b"
            @export="exportBundle(b)"
            @archive="archive(b)"
            @delete="deleteBundle(b)"
          />
        </div>
      </main>
    </div>
    
    <BundleMetadataEditor
      v-if="editing"
      :bundle="editing"
      @save="saveMetadata"
      @cancel="editing = null"
    />
  </div>
</template>
```

### 7.3 BundleLibraryService

The backend service that supports listing, filtering, and metadata editing. Bundle metadata persists in the bundle directory itself — a small `bundle-metadata.json` file alongside the existing `metadata.json` (the one written by the aggregator).

Why two metadata files? `metadata.json` is **built by the aggregator** (Phase 4) — immutable, contains derived facts (topology, time range, scenario context, latency budgets). `bundle-metadata.json` is **user-editable** (label, description, tags, archived flag, last-opened timestamp). Keeping them separate preserves the immutability of the aggregator's output.

```csharp
namespace Tracer.WebApi.Queries;

public sealed class BundleLibraryService
{
    private readonly string _bundlesRoot;
    private readonly ILogger<BundleLibraryService> _logger;

    public BundleLibraryService(string bundlesRoot, ILogger<BundleLibraryService> logger)
    {
        _bundlesRoot = bundlesRoot;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BundleLibraryEntry>> ListAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_bundlesRoot)) return Array.Empty<BundleLibraryEntry>();
        
        var entries = new List<BundleLibraryEntry>();
        foreach (var dir in Directory.EnumerateDirectories(_bundlesRoot))
        {
            var entry = await TryLoadEntryAsync(dir, ct);
            if (entry is not null) entries.Add(entry);
        }
        return entries;
    }

    public async Task<bool> UpdateMetadataAsync(
        string bundleId, BundleMetadataUpdate update, CancellationToken ct)
    {
        var bundleDir = Path.Combine(_bundlesRoot, bundleId);
        if (!Directory.Exists(bundleDir)) return false;
        
        var path = Path.Combine(bundleDir, "bundle-metadata.json");
        var current = File.Exists(path)
            ? await ReadUserMetadataAsync(path, ct)
            : new BundleUserMetadata();
        
        var updated = current with
        {
            Label       = update.Label        ?? current.Label,
            Description = update.Description  ?? current.Description,
            Tags        = update.Tags         ?? current.Tags,
            IsArchived  = update.IsArchived   ?? current.IsArchived,
            LastOpenedAtUtc = update.LastOpenedAtUtc ?? current.LastOpenedAtUtc,
        };
        await WriteUserMetadataAsync(path, updated, ct);
        return true;
    }

    public async Task<bool> DeleteAsync(string bundleId, CancellationToken ct)
    {
        var bundleDir = Path.Combine(_bundlesRoot, bundleId);
        if (!Directory.Exists(bundleDir)) return false;
        Directory.Delete(bundleDir, recursive: true);
        return true;
    }

    private async Task<BundleLibraryEntry?> TryLoadEntryAsync(string bundleDir, CancellationToken ct)
    {
        // Aggregator metadata (immutable)
        var aggPath = Path.Combine(bundleDir, "metadata.json");
        if (!File.Exists(aggPath)) return null;
        var agg = await ReadAggregatorMetadataAsync(aggPath, ct);
        
        // User-editable metadata
        var userPath = Path.Combine(bundleDir, "bundle-metadata.json");
        var user = File.Exists(userPath)
            ? await ReadUserMetadataAsync(userPath, ct)
            : new BundleUserMetadata();
        
        var sizeBytes = ComputeDirectorySize(bundleDir);
        
        return new BundleLibraryEntry
        {
            BundleId         = Path.GetFileName(bundleDir),
            SessionId        = agg.SessionId,
            Label            = user.Label,
            Description      = user.Description,
            Tags             = user.Tags,
            IsArchived       = user.IsArchived,
            SessionStartUtc  = agg.SessionStartUtc,
            SessionEndUtc    = agg.SessionEndUtc,
            BuiltAtUtc       = agg.BuiltAtUtc,
            LastOpenedAtUtc  = user.LastOpenedAtUtc,
            SizeBytes        = sizeBytes,
        };
    }

    private static long ComputeDirectorySize(string dir) =>
        new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

    // ReadAggregatorMetadataAsync, ReadUserMetadataAsync, WriteUserMetadataAsync details elided
}

public sealed record BundleUserMetadata
{
    public string? Label { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public bool IsArchived { get; init; }
    public DateTimeOffset? LastOpenedAtUtc { get; init; }
}

public sealed record BundleMetadataUpdate
{
    public string? Label { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public bool? IsArchived { get; init; }
    public DateTimeOffset? LastOpenedAtUtc { get; init; }
}

public sealed record BundleLibraryEntry
{
    public required string BundleId { get; init; }
    public required string SessionId { get; init; }
    public string? Label { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required bool IsArchived { get; init; }
    public required DateTimeOffset SessionStartUtc { get; init; }
    public required DateTimeOffset SessionEndUtc { get; init; }
    public required DateTimeOffset BuiltAtUtc { get; init; }
    public DateTimeOffset? LastOpenedAtUtc { get; init; }
    public required long SizeBytes { get; init; }
}
```

### 7.4 Endpoint Extension

The Phase 4 bundle endpoints (`GET /api/bundles`, `GET /api/bundles/{id}/download`, `POST /api/bundles/build`, `GET /api/bundles/{id}/status`) are extended:

```
GET    /api/bundles/library                       full library entries with user metadata
PUT    /api/bundles/{id}/metadata                 update label/description/tags/archived
POST   /api/bundles/{id}/opened                   record last-opened timestamp
DELETE /api/bundles/{id}                          delete the bundle directory
POST   /api/bundles/import                        upload an exported bundle zip
```

The `/api/bundles/import` endpoint accepts a multipart form upload of a `.bundle.zip` file, validates it (checks the manifest, verifies file hashes per Phase 4 §6), and unpacks into the bundles root. Import is rejected if a bundle with the same ID already exists.

---

## 8. Cross-View "Show SQL for this view" Affordance

Every analytical view (Timeline, Causal Tree, Entity History, Replication Latency, etc.) gets a small "Show SQL" button in its toolbar. Clicking opens the SQL Console with the equivalent SQL pre-loaded.

### 8.1 SQL Generation Per View

Each view knows what its current filter is and can generate an approximation of the SQL behind it. The frontend handles this — generating SQL strings is straightforward.

```typescript
// src/utils/showSqlGenerators.ts

export interface TimelineFilterForSql {
  from: Date; to: Date;
  topic?: string;
  publisherNode?: string;
  subscriberNode?: string;
  traceId?: string;
  entityId?: string;
}

export function timelineFilterToSql(f: TimelineFilterForSql): string {
  const clauses = [
    `publish_wallclock >= TIMESTAMP '${f.from.toISOString()}'`,
    `publish_wallclock <  TIMESTAMP '${f.to.toISOString()}'`,
  ];
  if (f.topic) clauses.push(`topic = '${sqlEscape(f.topic)}'`);
  if (f.publisherNode) clauses.push(`publisher_node = '${sqlEscape(f.publisherNode)}'`);
  if (f.subscriberNode) clauses.push(`subscriber_node = '${sqlEscape(f.subscriberNode)}'`);
  if (f.traceId) clauses.push(`trace_id = ${parseHexAsBigInt(f.traceId)}`);
  if (f.entityId) clauses.push(`entity_id = '${sqlEscape(f.entityId)}'`);
  
  return `SELECT publish_wallclock, publisher_node, topic, event_id\nFROM events\nWHERE ${clauses.join('\n  AND ')}\nORDER BY publish_wallclock\nLIMIT 1000;`;
}

function sqlEscape(s: string): string { return s.replace(/'/g, "''"); }
function parseHexAsBigInt(hex: string): string { return BigInt('0x' + hex).toString(); }
```

Each view imports the appropriate generator. The "Show SQL" button:

```vue
<!-- inside any analytical view's toolbar -->
<ShowSqlButton :sql="timelineFilterToSql(currentFilter)" :session-id="sessionId" />
```

```vue
<!-- src/components/ShowSqlButton.vue -->
<script setup lang="ts">
import { useRouter } from 'vue-router';

const props = defineProps<{ sql: string; sessionId: string }>();
const router = useRouter();

function open() {
  router.push({
    name: 'sql-console',
    params: { sessionId: props.sessionId },
    query: { sql: props.sql }
  });
}
</script>

<template>
  <button class="show-sql-btn" @click="open" title="Open the current filter as SQL">
    Show SQL
  </button>
</template>
```

The SQL Console reads `?sql=` from its URL query and pre-populates the editor (§5.3 already does this).

### 8.2 Educational Value

The generated SQL is intentionally **shape-equivalent**, not literally what the view runs. The view itself runs more complex SQL (multi-interval unions, aggregation, etc.) but the generator returns the user-friendly equivalent — what a human would write to answer "what's on the timeline right now?"

This is the on-ramp from "I know what I want to see, but my view doesn't show it" to "let me write the query myself".

---

## 9. Test Plan for Phase 10

### 9.1 Backend Unit Tests

**WebApi/SqlGuardrailsTests.cs**
- Plain SELECT: accepted
- WITH ... SELECT: accepted
- EXPLAIN / DESCRIBE: accepted
- INSERT INTO ...: rejected
- UPDATE ...: rejected
- DELETE ...: rejected
- CREATE TABLE ...: rejected
- DROP TABLE ...: rejected
- ATTACH 'file.db': rejected
- COPY (SELECT ...) TO 'file.csv': rejected
- PRAGMA threads = 4: rejected
- Multi-statement (`SELECT 1; SELECT 2`): rejected
- Block comment hiding DDL (`/* */ DROP TABLE ...`): rejected (we tokenize after stripping comments)
- `read_csv_auto('foo.csv')`: rejected
- `read_parquet('bar.parquet')`: rejected
- Quoted identifier matching forbidden keyword (e.g., `"INSERT"`): accepted (it's an identifier, not a keyword)

**WebApi/SqlExecutorServiceTests.cs**
- Simple SELECT against fixture data: returns expected rows
- Query with parameters: bindings honored
- Default row limit injected when missing
- Explicit `LIMIT 10`: not modified
- Timeout: query exceeding timeout returns Timeout state
- Cancellation via outer token: respected
- Invalid SQL: returns Failed with error message
- Memory limit applied per query

**WebApi/SqlSchemaServiceTests.cs**
- Returns expected tables (events, slow_state, plus any test fixtures)
- Returns columns with correct types
- Cache: second call doesn't re-query
- Invalidate forces re-query

**WebApi/BuiltInQueriesServiceTests.cs** (via Storage.SavedQueries)
- On first load: built-ins inserted
- On subsequent loads: built-ins NOT duplicated
- Built-in queries marked `is_built_in = true`
- Update attempt on built-in: rejected
- Delete attempt on built-in: rejected
- Clone of built-in: creates editable copy with new ID

**WebApi/BundleLibraryServiceTests.cs**
- List with no bundles: empty
- List with N bundles: returns N entries
- Bundle without user-metadata.json: returns entry with null label/description, empty tags
- UpdateMetadata: writes to bundle-metadata.json
- UpdateMetadata: leaves aggregator's metadata.json untouched
- Delete: removes the bundle directory
- Size calculation: includes nested files

**WebApi/SqlEndpointsTests.cs**
- POST `/api/sql/execute`: 200 with results for valid query
- POST `/api/sql/execute` with empty body: 400
- POST `/api/sql/execute` with rejected SQL: 200 with state=Rejected
- POST `/api/sql/explain`: 200 with plan text
- GET `/api/sql/schema`: 200 with tables list

### 9.2 Backend Integration Tests

**SqlConsoleIntegrationTests.cs**
- Set up a bundle with known data
- Run via API: `SELECT COUNT(*) FROM events` — assert correct count
- Run a parameterized query — assert parameters bind correctly
- Run a query that exceeds timeout — assert Timeout state
- Run an invalid query — assert Failed state with DuckDB error

**BundleLibraryRoundTripTests.cs**
- Create a bundle (via aggregator)
- List the library: bundle appears
- Update label and tags
- Reload: changes persist
- Archive: bundle no longer appears in default list
- Delete: bundle directory removed

**SavedQueriesRoundTripTests.cs**
- Create a saved query
- List: appears
- Run via SQL endpoint: increment run_count, set last_run_at
- Clone built-in: creates editable copy

### 9.3 Frontend Unit Tests (Vitest)

**sqlEditor.spec.ts**
- Mounts a CodeMirror 6 editor
- Emits update:modelValue on document change
- Cmd+Enter triggers run event
- Schema autocomplete: typing partial column name suggests matches

**useSqlExecution.spec.ts**
- Run sets loading
- Result available after run completes
- Cancellation via cancel() aborts the in-flight request
- Error sets error.value

**useBundleLibrary.spec.ts**
- Load fetches the list
- Filter by tag narrows the list
- Sort by size descending: heaviest first

### 9.4 E2E Tests (Playwright)

```typescript
test('execute simple SQL query', async ({ page }) => {
  await page.goto('http://localhost:5300/v/sql/test-session');
  // Editor pre-populated; clear and type new query
  await page.locator('.cm-editor').click();
  await page.keyboard.press('Control+A');
  await page.keyboard.type('SELECT topic, COUNT(*) FROM events GROUP BY topic');
  await page.keyboard.press('Control+Enter');
  // Result table appears
  await expect(page.locator('.sql-result-table table')).toBeVisible();
  await expect(page.locator('.sql-result-table thead th').first()).toContainText('topic');
});

test('rejected query shows clear error', async ({ page }) => {
  await page.goto('http://localhost:5300/v/sql/test-session');
  await page.locator('.cm-editor').click();
  await page.keyboard.press('Control+A');
  await page.keyboard.type('DROP TABLE events');
  await page.keyboard.press('Control+Enter');
  await expect(page.locator('.sql-console__error')).toContainText('Forbidden keyword');
});

test('Show SQL from timeline', async ({ page }) => {
  await page.goto('http://localhost:5300/v/timeline/test-session?topic=weapons.fire');
  await page.locator('.show-sql-btn').click();
  await expect(page).toHaveURL(/\/v\/sql\//);
  // Editor pre-loaded with WHERE topic = 'weapons.fire'
  await expect(page.locator('.cm-editor')).toContainText("topic = 'weapons.fire'");
});

test('save and reload query', async ({ page }) => {
  await page.goto('http://localhost:5300/v/sql/test-session');
  await page.locator('.cm-editor').click();
  await page.keyboard.press('Control+A');
  await page.keyboard.type('SELECT * FROM events LIMIT 5');
  // Save
  await page.locator('button:has-text("Save")').click();
  await page.locator('.save-query-dialog input').fill('My query');
  await page.locator('.save-query-dialog button:has-text("Save")').click();
  // Open Saved Queries
  await page.goto('http://localhost:5300/v/saved-queries');
  await expect(page.locator('text=My query')).toBeVisible();
});

test('bundle library: filter by tag', async ({ page }) => {
  await page.goto('http://localhost:5300/v/bundles');
  await expect(page.locator('.bundle-card').first()).toBeVisible();
  // Edit a bundle and add a tag
  await page.locator('.bundle-card button:has-text("Edit")').first().click();
  await page.locator('.bundle-metadata-editor input[placeholder="Add tag"]').fill('test-tag');
  await page.keyboard.press('Enter');
  await page.locator('.bundle-metadata-editor button:has-text("Save")').click();
  // Filter by the tag
  await page.locator('.bundle-filter-panel input[type="checkbox"]:near(:text("test-tag"))').check();
  // Only matching cards visible
  await expect(page.locator('.bundle-card')).toHaveCount(1);
});
```

### 9.5 Security Tests

A small set of dedicated security tests for SqlGuardrails. Each test attempts to inject a forbidden statement via various tricks:

- Comment injection
- Quoted identifier evasion
- Mixed-case keywords (`InSeRt InTo`)
- Unicode lookalikes
- Multi-line statements
- WITH clause with hidden write
- SELECT INTO (rejected because INTO + CREATE/INSERT semantics)
- Nested ATTACH inside subquery

All variants should be rejected. The test list is deliberately growable — if a new evasion is discovered, add it as a regression test before fixing.

### 9.6 Performance Tests

- SQL execute (simple SELECT, 1000 rows): < 500 ms p95
- SQL execute (aggregate, 100k rows): < 2 s p95
- SQL schema first call: < 200 ms; subsequent: < 5 ms (cache)
- Bundle library list (100 bundles): < 200 ms
- SQL Console first paint: < 1 s
- Saved Queries list (50 entries): < 50 ms

---

## 10. Phase 10 Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Hand-rolled tokenizer misses a SQL-injection vector | Medium | High | Three layers of defense (validator, multi-statement rejection, read-only file mode); growing security-test suite; documented as "best-effort filtering, not formal proof". A determined attacker with local access can read the bundle anyway — the executor's read-only enforcement is about preventing accidental damage, not adversarial security. |
| User runs a query that consumes all DuckDB memory | Medium | Medium | Per-query PRAGMA memory_limit; 1 GB default; timeout will fire long before OOM in practice |
| Query timeout fires while the query is at 99% complete | Medium | Low | User can re-run with higher timeoutSeconds (up to a hard cap like 5 minutes) |
| CodeMirror 6 dependency footprint inflates frontend bundle | Low | Low | CodeMirror 6 is modular; we import only what's needed. Tree-shaking handles the rest. |
| Schema cache becomes stale during long Observer sessions | Low | Low | Cache is bounded to "current attached intervals"; schema is identical across them per Phase 1 §4. Cache invalidation on `IntervalSetTracker.SetChanged` (Phase 5 §3) keeps it fresh. |
| Saved query SQL becomes invalid after a hypothetical schema change | Low | Medium | Phase 10 assumes schema stability. Major schema changes would invalidate saved queries; that's a known cost of database-shape changes and is out of Phase 10's scope. |
| Built-in queries depend on bundle-mode features (e.g., per-subscriber receive times) and fail in live mode | High | Low | Built-in queries are labeled with mode requirements ("Bundle only"); UI shows a hint. Failure mode is a clear DuckDB error, not a crash. |
| Bundle export zip is very large (multi-GB) | Medium | Low | Streamed HTTP response (already supported by ASP.NET Core); user's browser handles the download. No backend memory pressure. |
| Bundle import: malicious zip with path traversal could write outside the bundles directory | Medium | High | Aggressively validate the zip's entries: reject any entry with `..` or absolute paths; only allow files matching the expected bundle layout. Standard zip-slip defense. |
| Bundle library directory grows unbounded | Medium | Low | The archive feature lets users hide without deleting; delete is also there. No automatic GC — operators retain control. |
| Show SQL generators produce SQL that doesn't exactly match what the view runs | High | Low | Documented as "shape-equivalent, not literal". The user knows the difference; if they want the exact query, EXPLAIN is available. |

---

## 11. Definition of Done for Phase 10

### Build & Run

- [ ] `Tracer.Storage.SavedQueries` builds clean
- [ ] All new endpoints registered in Observer and Offline Viewer
- [ ] OpenAPI document includes new endpoints
- [ ] TypeScript client regenerates cleanly
- [ ] Frontend builds with CodeMirror 6 dependency installed
- [ ] Frontend bundle size remains under 3 MB gzipped (CodeMirror adds ~150 KB)

### SQL Executor

- [ ] SqlGuardrails rejects all enumerated forbidden constructs (security test suite passes)
- [ ] SqlExecutorService applies row limit if absent
- [ ] Timeout fires correctly and surfaces as `state=Timeout`
- [ ] Memory limit applied per query via PRAGMA
- [ ] Multi-statement queries rejected
- [ ] Parameter binding via DuckDBParameter works

### SQL Console UI

- [ ] CodeMirror editor mounts and is responsive
- [ ] Cmd+Enter runs the query
- [ ] Schema autocomplete suggests tables and columns
- [ ] Results render in a table; rows sortable by column
- [ ] Chart view available for results with at least one numeric column
- [ ] Pivot buttons appear for rows with `event_id`, `entity_id`, `trace_id`, or `publish_wallclock`
- [ ] Pivot navigates correctly to Timeline/Entity History/Causal Tree
- [ ] Export CSV works
- [ ] History (last 50 queries) persists in localStorage
- [ ] Error states render clearly with the DuckDB error message

### Saved Queries

- [ ] CRUD via API works
- [ ] Built-in queries loaded on first startup; not duplicated on subsequent startups
- [ ] Built-in queries cannot be modified or deleted
- [ ] Clone of built-in creates an editable copy
- [ ] Run-count and last-run-at updated on execution
- [ ] Favorite toggle works
- [ ] SavedQueriesView lists queries grouped by tag

### Bundle Library

- [ ] BundleLibraryView lists all bundles with metadata
- [ ] Edit metadata: label, description, tags persist to `bundle-metadata.json`
- [ ] Filter by tag works
- [ ] Sort by builtAt/sessionStart/size/label works
- [ ] Archive hides from default list; "show archived" toggle reveals them
- [ ] Delete removes the bundle directory
- [ ] Export downloads a zip
- [ ] Import accepts an exported bundle and validates against zip-slip

### Cross-View Show SQL

- [ ] Timeline view has Show SQL button; generates correct WHERE filter
- [ ] Causal Tree view: Show SQL generates trace_id query
- [ ] Entity History view: Show SQL generates entity_id query
- [ ] Replication Latency view: Show SQL generates the latency aggregate
- [ ] SQL Console URL `?sql=` parameter populates the editor on open

### Testing

- [ ] All Phase 1-9 tests pass
- [ ] Phase 10 backend unit tests pass (target: 50+, including the security suite)
- [ ] Phase 10 backend integration tests pass
- [ ] Phase 10 frontend unit tests pass
- [ ] At least five Playwright E2E tests pass

### Performance

- [ ] SQL execute (simple SELECT, 1000 rows): < 500 ms p95
- [ ] SQL execute (aggregate, 100k rows): < 2 s p95
- [ ] Schema query (cached): < 5 ms
- [ ] Bundle library list (100 bundles): < 200 ms
- [ ] SQL Console first paint: < 1 s
- [ ] Saved Queries list: < 50 ms

### Documentation

- [ ] `docs/sql-console.md` documents allowed syntax, parameters, dialect notes
- [ ] `docs/saved-queries.md` documents the built-in queries and how to author parameters
- [ ] `docs/bundle-library.md` documents bundle metadata, archival, export, import
- [ ] `docs/sql-security.md` documents the read-only enforcement and its limits (NOT a security proof)
- [ ] CHANGELOG entry

---

## 12. Handoff to Phase 11

What Phase 11 inherits from Phase 10:

- **Complete view set with SQL escape hatch**: Phase 11 doesn't need to add new views; it adapts the existing ones to real data. The SQL Console becomes the diagnostic tool when something unexpected emerges from real data.
- **Bundle library as artifact catalog**: real DDS-captured bundles flow into the same library, with the same metadata/tagging/archival.
- **Saved queries as institutional knowledge**: Phase 11's adapter team can write saved queries that test their integration ("are events appearing on all expected topics?", "is sequence numbering monotonic?")

What Phase 11 must address that Phase 10 deferred:

- **Real DDS adapter** (`Tracer.Adapters.DDS`): replaces the FakeNode-based testing with actual loopback subscribers that translate DDS samples to `DiagnosticRecord`s
- **Real sync system integration** (`Tracer.Adapters.Sync`): per-node upload of per-interval data via the customer's existing sync system (Telemetry category, per the sync addendum)
- **Real shared-memory transport** (`Tracer.Adapters.SharedMemory`): production-grade IPC between the simulation engine and the TracerAgent
- **Integration testing with the live simulation**: end-to-end validation against the actual customer environment, not synthetic data
- **Operational hardening**: any operational concerns surfaced by real use (resource limits, error recovery edge cases, etc.)

What's now possible after Phase 10:

The complete *user-facing* system is in place. Engineers and scenario authors have:

- **Six opinionated analytical views** answering known questions (timeline, scenario, causal tree, entity history, replication latency, network topology, gap detection, trigger evaluations)
- **A SQL escape hatch** answering unknown questions
- **A saved-queries library** turning successful ad-hoc queries into reusable assets
- **A bundle library** organizing accumulated analyses over time
- **Annotations and saved views** capturing analytical context

Phase 11 brings this system into contact with real data. The work shifts from "designing the right tool" to "validating the tool against the customer's environment". If Phases 1-10 are designed well, Phase 11 is integration and hardening — not redesign.
