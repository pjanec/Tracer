# Tracer Phase 4 — Detailed Design
## TracerAggregator, Bundle Format, Offline Viewer, Self-Contained Packaging

*Companion to `tracer_architecture_v1.md`, `tracer_phase1_design.md`, `tracer_phase2_design.md`, `tracer_phase3_design.md`*
*Phase 4 of the build sequence (architecture §18)*
*C# / .NET 8 backend · Vue 3 / TypeScript frontend · May 2026*

*Phase 4 closes the loop from data capture to portable analysis artifact. It introduces the `TracerAggregator` (both CLI and library), defines and validates the bundle format, adds bundle build/list/open endpoints to the Web API, makes the viewer work in offline-bundle mode without a live observer, and produces a self-contained viewer distribution that field-support engineers can ship to customers and unzip on any Windows machine.*

*By the end of Phase 4, the field-support workflow is demonstrable end-to-end: a customer captures a session, builds a bundle, sends it to the support team, who open it on their own laptop with no connection to the customer's cluster.*

---

## 1. Phase 4 Scope and Goals

### 1.1 What Phase 4 Delivers

- **`Tracer.Aggregator`** assembly — the aggregator as a reusable library
- **`tracer-aggregate.exe`** — CLI front-end to the aggregator
- **`Tracer.Bundle`** assembly — bundle format, manifests, schemas, validation, packaging
- **Cross-interval reader** — a `MultiIntervalReader` that the bundle viewer (and Phase 5's timeline) uses to query across multiple completed intervals
- **Bundle Build endpoint** — `POST /api/bundles/build` on the Observer's Web API
- **Bundle Open mode for the Web API** — a startup mode where the API serves queries against a bundle file rather than a live observer
- **`tracer-viewer.exe`** — a standalone "offline viewer" executable that opens a `.tracerbundle` and launches a local browser to view it
- **Self-contained distribution** — a single folder (or single .zip) containing everything needed to open a bundle on a fresh Windows machine
- **Bundle round-trip tests** — capture data live → export → open in standalone viewer → verify identical query results
- **Field-support demo workflow** documented end-to-end

### 1.2 What Phase 4 Does NOT Deliver

- No engineer timeline view (Phase 5)
- No causal tree view (Phase 6)
- No entity history view (Phase 7)
- No real sync system integration — the aggregator reads from `ITelemetryStorageReader` whose implementation is still the `LocalFileSystemStorageReader` mock from Phase 2
- No DDS adapter (Phase 11)
- **No bundle redaction or anonymization** — bundles include all captured data verbatim; redaction is deferred (architecture §1.2 out-of-scope)
- **No bundle signing or tamper-evident manifests** — same reason; deferred
- No bundle library UI — the Vue SPA gains an "open bundle" affordance but a browseable library of saved bundles is Phase 10
- No incremental bundle updates — a bundle is built once for a time range; updating it requires rebuilding
- No bundle compression beyond ZIP — bundles are stored as the format §3 defines, period

### 1.3 Success Criteria

1. **`tracer-aggregate.exe` builds a bundle** for a chosen time range. CLI accepts `--session-id`, `--time-range`, `--nodes`, `--fast-state`, `--output`. Produces a valid `.tracerbundle` file (or directory) at the output path.
2. **The bundle validates** against the schema. A separate validation step (`tracer-aggregate validate <path>`) confirms manifest correctness, file checksums match, schema version is recognized.
3. **The bundle is portable**. Copy it to a different machine with only the Tracer offline viewer installed. Open it. All Phase 3 views (Session Browser, Scenario View) work against the bundle just as they do against the live observer.
4. **Bundle round-trip integrity**. Capture data in a FakeNode+Observer session, build a bundle, open it in a separate process, run a defined set of queries. Compare to results from the live observer at the time of capture. Results must match exactly (modulo wall-clock timestamps of when queries were issued, not the data itself).
5. **Cross-interval queries work**. The bundle spans multiple capture intervals; `GET /api/sessions` and `GET /api/scenario/notables` correctly return data across interval boundaries.
6. **The Observer's `POST /api/bundles/build` works**: requests are accepted, build runs in background, status is reportable, bundle file is produced.
7. **`tracer-viewer.exe` runs on a clean machine**: extract distribution to any folder, double-click executable, browser opens to the viewer, drag-and-drop a bundle to open it.
8. **All Phase 1, 2, 3 tests pass.** New Phase 4 tests pass.
9. **CLI demo**: `tracer-aggregate build --session-id <sid> --output session.tracerbundle` produces a 100-300 MB bundle (typical) for a 30-minute scenario in under 60 seconds.
10. **Performance**: opening a bundle and rendering the Scenario View completes in under 3 seconds for a 1 GB bundle.

### 1.4 Estimated Duration

Two to three calendar weeks for one developer. The complexity is distributed:
- Week 1: Bundle format, manifest schema, aggregator library, CLI
- Week 2: Cross-interval reader, Web API additions, viewer offline mode
- Week 3: Standalone viewer packaging, integration tests, demo polish

---

## 2. Project Layout Additions

Building on Phase 3:

```
tracer/
  src/
    Tracer.Core/                                  (unchanged)
    Tracer.Storage.DuckDB/                        (unchanged)
    Tracer.Adapters.Mock/                         (unchanged)
    Tracer.Agent/                                 (unchanged)
    Tracer.FakeNode/                              (unchanged)
    Tracer.Observer/                              (additions; see §7)
    Tracer.WebApi/                                (additions; see §7)
    Tracer.Bundle/                                NEW assembly
      Tracer.Bundle.csproj
      Format/
        BundleManifest.cs                         the on-disk manifest schema
        BundleLayout.cs                           file layout constants
        BundleSchemaV1.cs                         schema version definition
        BundleNaming.cs                           safe filename helpers
      Packaging/
        BundleDirectoryWriter.cs                  writes a bundle to a directory
        BundleZipWriter.cs                        wraps directory writer; zips at the end
        BundleReader.cs                           opens a bundle (directory or zip)
        BundleExtractor.cs                        extracts a zipped bundle to a working directory
      Validation/
        BundleValidator.cs                        validates a bundle against schema and integrity
        ValidationResult.cs
        ValidationError.cs
    Tracer.Storage.DuckDB.MultiInterval/          NEW assembly (extension to storage)
      Tracer.Storage.DuckDB.MultiInterval.csproj
      MultiIntervalReader.cs                      query across multiple DuckDB files
      AttachedDatabaseManager.cs                  ATTACH/DETACH lifecycle
      IntervalDbFile.cs                           a file to attach
    Tracer.Aggregator/                            NEW assembly
      Tracer.Aggregator.csproj
      AggregationOrchestrator.cs                  the main library entrypoint
      Configuration/
        AggregationRequest.cs                     input parameters
        AggregationResult.cs                      output details
        FastStateScope.cs                         None | SelectedEntities | All
      Discovery/
        IntervalDiscovery.cs                      finds intervals overlapping a time range
        SessionResolver.cs                        resolves sessionId → time range
      Consolidation/
        EventsConsolidator.cs                     merges per-node events.duckdb files
        SlowStateConsolidator.cs                  merges per-node slow_state.duckdb files
        FastStateCopier.cs                        copies relevant Parquet files
        ScenarioMetadataCollector.cs              gathers scenario context from events
        TopologyExtractor.cs                      extracts node topology from data
        SourceIntervalsBuilder.cs
        ManifestBuilder.cs
      Staging/
        StagingDirectory.cs                       temp workspace for an in-progress build
      Progress/
        IAggregationProgressReporter.cs
        AggregationStage.cs
    Tracer.Aggregator.Cli/                        NEW assembly
      Tracer.Aggregator.Cli.csproj
      Program.cs                                  tracer-aggregate.exe entrypoint
      Commands/
        BuildCommand.cs                           default: build a bundle
        ValidateCommand.cs                        validate an existing bundle
        InspectCommand.cs                         show bundle metadata (manifest contents)
      Logging/
        CliConsoleLogger.cs                       progress to stderr, friendly format
    Tracer.OfflineViewer/                         NEW assembly
      Tracer.OfflineViewer.csproj
      Program.cs                                  tracer-viewer.exe entrypoint
      OfflineViewerHostBuilder.cs                 like ObserverHostBuilder but bundle-mode
      Lifecycle/
        BundleOpenManager.cs                      load/close active bundle
        OfflineHostedService.cs
        InertObserverStateReporter.cs
      Browser/
        BrowserLauncher.cs                        opens default browser at localhost
    Tracer.TestHarness/                           (additions)
      BundleFixture.cs
      AggregationFixture.cs
      RoundTripAssertions.cs
  tests/
    Tracer.Tests.Unit/
      Bundle/
        BundleManifestTests.cs
        BundleDirectoryWriterTests.cs
        BundleValidatorTests.cs
        BundleReaderTests.cs
      Aggregator/
        IntervalDiscoveryTests.cs
        SessionResolverTests.cs
        EventsConsolidatorTests.cs
        FastStateCopierTests.cs
        TopologyExtractorTests.cs
      MultiInterval/
        MultiIntervalReaderTests.cs
        AttachedDatabaseManagerTests.cs
      WebApi/
        BundleEndpointTests.cs
    Tracer.Tests.Integration/
      AggregatorEndToEndTests.cs
      BundleRoundTripTests.cs
      ObserverBundleBuildTests.cs
      OfflineViewerSmokeTests.cs
  tracer-viewer/                                  (additions)
    src/
      views/
        BundleOpenView.vue                        first-load view when no bundle is open
      composables/
        useBundleMode.ts                          detects live vs bundle mode
```

### 2.1 Updated Dependency Graph

```
Tracer.Core                                       (unchanged)
    ↑
Tracer.Storage.DuckDB                             (unchanged)
    ↑
Tracer.Storage.DuckDB.MultiInterval               (deps: Tracer.Core, Tracer.Storage.DuckDB)
    ↑
Tracer.Bundle                                     (deps: Tracer.Core, System.IO.Compression)
    ↑
Tracer.Adapters.Mock                              (unchanged from Phase 2)
    ↑
Tracer.Aggregator                                 (deps: Tracer.Core, Tracer.Storage.DuckDB,
                                                          Tracer.Storage.DuckDB.MultiInterval,
                                                          Tracer.Bundle, Tracer.Adapters.Mock)
    ↑
Tracer.Aggregator.Cli                             (deps: Tracer.Aggregator, System.CommandLine)
    ↑
Tracer.WebApi                                     (additions: bundle endpoints reference
                                                              Tracer.Aggregator and Tracer.Bundle)
    ↑
Tracer.Observer                                   (additions: hosts bundle build background task)
    ↑
Tracer.OfflineViewer                              (deps: Tracer.WebApi, Tracer.Bundle,
                                                          Tracer.Storage.DuckDB.MultiInterval)
```

**New NuGet packages** (added to `Directory.Packages.props`):

```xml
<PackageVersion Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
<PackageVersion Include="System.IO.Compression.ZipFile" Version="4.3.0" />
<PackageVersion Include="Ulid" Version="1.3.4" />
```

`System.CommandLine` is the standard .NET CLI parser. The 2.0-beta is the maintained line. `Ulid` provides the bundle ID generator.

---

## 3. The Bundle Format

The bundle is the on-disk artifact produced by the aggregator and consumed by the viewer. Architecture §8.3 specifies the structure at a high level; this section nails down every byte.

### 3.1 On-Disk Layout

A bundle is a **directory** with a `.tracerbundle` suffix. The directory can optionally be packaged as a ZIP archive (filename ending `.tracerbundle.zip`) — readers detect which by checking whether the path is a file or directory.

```
session_20260519_combat.tracerbundle/
  manifest.json                                    -- bundle metadata
  scenario.json                                    -- scenario context: phases, notables index
  topology.json                                    -- participating nodes
  source_intervals.json                            -- source mapping: which agent interval contributed what
  events.duckdb                                    -- consolidated events from all nodes
  slow_state.duckdb                                -- consolidated slow state from all nodes
  fast_state/                                      -- optional, per FastStateScope policy
    {topic_safe_name}/
      {entity_id_safe}/
        samples.parquet                            -- per-entity samples for that topic
  annotations/                                     -- empty in Phase 4; populated by Phase 8
    .keep
  checksums.txt                                    -- SHA-256 of every file other than this one
```

**Topic / entity name safety**: bundle directory names must avoid filesystem-hostile characters. The bundle writer replaces each character not in `[a-zA-Z0-9._-]` with `_` and appends a 4-char hex hash of the original to prevent collisions. Example: `vehicle:blue:17` becomes `vehicle_blue_17_a3f2`.

**Why a directory by default**:
- ZIP is convenient for shipping but inconvenient for inspection ("what's in this bundle?" requires extraction)
- DuckDB cannot read its database files from inside a ZIP without extraction
- The viewer needs DuckDB to open `events.duckdb` directly; bundling-as-ZIP requires unzipping on open anyway
- Operations like "build a bundle and immediately query it" want directory layout

**When to use ZIP**:
- Shipping over email or other transports that prefer single files
- Long-term archive storage
- Cases where compression matters (DuckDB files compress modestly; JSON files compress well)

The CLI `--output` argument accepts both:
- `session.tracerbundle` (no `.zip` suffix) → write as a directory
- `session.tracerbundle.zip` → write as a directory in temp, then zip

### 3.2 manifest.json Schema

This is the single source of truth about a bundle. Every other file in the bundle is described here.

```json
{
  "bundleId": "01H8XYZ7K3M4P5Q6R7S8T9V0W1",
  "schemaVersion": 1,
  "createdAtUtc": "2026-05-20T09:30:00.000Z",
  "tracerVersion": "1.0.0",
  "writer": {
    "tool": "tracer-aggregate",
    "version": "1.0.0",
    "host": "support-laptop-03"
  },
  "timeRange": {
    "startUtc": "2026-05-19T14:03:22.143Z",
    "endUtc":   "2026-05-19T14:38:51.927Z"
  },
  "sessionContext": {
    "sessionId":  "5b2f0c40-1234-5678-9abc-def012345678",
    "scenarioId": "combat_engagement_v3",
    "label":      "Tuesday morning training run"
  },
  "participatingNodes": [
    "blue-cmd-01", "blue-veh-01", "blue-veh-02",
    "red-cmd-01", "red-veh-01"
  ],
  "fastStateScope": "selected-entities",
  "fastStateEntities": [
    "vehicle:blue:17", "vehicle:red:03"
  ],
  "statistics": {
    "totalEvents": 1247831,
    "totalSlowStateSamples": 8420,
    "totalFastStateRows": 184200,
    "uncompressedBytes": 247892480
  },
  "files": [
    {
      "path": "events.duckdb",
      "sizeBytes": 41943040,
      "sha256": "a3f2b4c8d9e0f1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b1c2d3e4f5a6"
    },
    {
      "path": "slow_state.duckdb",
      "sizeBytes": 524288,
      "sha256": "b4c5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f0a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5"
    }
  ]
}
```

### 3.3 scenario.json Schema

Cached scenario-level metadata so that scenario views don't have to re-query the DuckDB for high-level structure. Built from events at aggregation time.

```json
{
  "scenarioId": "combat_engagement_v3",
  "sessionId":  "5b2f0c40-...",
  "label":      "Tuesday morning training run",
  "startUtc":   "2026-05-19T14:03:22Z",
  "endUtc":     "2026-05-19T14:38:51Z",
  "phases": [
    { "phaseName": "approach",    "startedAtUtc": "2026-05-19T14:03:22Z", "endedAtUtc": "2026-05-19T14:12:05Z" },
    { "phaseName": "engagement",  "startedAtUtc": "2026-05-19T14:12:05Z", "endedAtUtc": "2026-05-19T14:31:47Z" },
    { "phaseName": "withdrawal",  "startedAtUtc": "2026-05-19T14:31:47Z", "endedAtUtc": "2026-05-19T14:38:51Z" }
  ],
  "notablesIndex": {
    "totalCount": 87,
    "bySeverity": { "info": 71, "warning": 14, "error": 2 },
    "byPhase":    { "approach": 22, "engagement": 51, "withdrawal": 14 }
  }
}
```

The viewer reads `scenario.json` for the Scenario View's initial state without needing a SQL query. The full notables list still comes from the DuckDB (via the standard `/api/scenario/notables` endpoint, which now queries the bundle's DuckDB).

### 3.4 topology.json Schema

```json
{
  "nodes": [
    {
      "nodeId": "blue-cmd-01",
      "firstSeenUtc": "2026-05-19T14:03:22.143Z",
      "lastSeenUtc":  "2026-05-19T14:38:51.812Z",
      "eventsPublished": 412847
    }
  ]
}
```

Topology answers `GET /api/topology` directly from this file.

### 3.5 source_intervals.json Schema

Traceability information: which raw per-node intervals contributed to this bundle.

```json
{
  "sources": [
    {
      "nodeId": "blue-cmd-01",
      "intervals": [
        {
          "intervalTimestamp": "20260519T140000Z",
          "intervalSourcePath": "C:/NAS-mock/Telemetry/blue-cmd-01/20260519T140000Z.zip",
          "intervalManifestSha256": "...",
          "contributedEventCount": 412847
        }
      ]
    }
  ]
}
```

This is read primarily by operators and forensics: if a bundle looks wrong, source_intervals.json lets you trace back to the original per-node interval files. The viewer doesn't use this — it's metadata for diagnosis of diagnostics.

### 3.6 checksums.txt Format

```
a3f2b4c8d9e0f1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b1c2d3e4f5a6  events.duckdb
b4c5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f0a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5  slow_state.duckdb
c5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f0a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6  scenario.json
```

Standard `sha256sum`-compatible format: hex digest, two spaces, relative path. Allows verification via stock tools (`sha256sum -c checksums.txt`) even without Tracer installed.

The manifest's `files[].sha256` field duplicates this information for JSON-based validation. Both must agree; the validator checks consistency.

### 3.7 Schema Versioning

Bundle schema version 1 is defined here. Future versions:

- **Schema additions** (new optional fields) — bump the bundle's `tracerVersion` but not `schemaVersion`. Readers ignore unknown fields.
- **Schema breaking changes** — bump `schemaVersion`. Readers reject bundles with unrecognized `schemaVersion`.
- **Schema migrations** — a future migration tool can convert a v1 bundle to v2 in place.

The viewer reports schema version mismatches with a clear error message: "This bundle was created with a newer version of Tracer (schema v2). Please update your viewer to open it."

### 3.8 Bundle Identity

`bundleId` is a ULID (Universally Unique Lexicographically-sortable Identifier): 26 characters, time-sortable, no central authority needed. Generated at aggregation time.

The bundle's filename is **not** its identity. Two copies of the same bundle on disk have the same `bundleId`. Renaming the file changes nothing about the bundle's contents.

---

## 4. The MultiInterval Reader

Before the aggregator can write a bundle that consolidates multiple intervals, and before the bundle viewer can serve queries against a bundle's DuckDB, there's a generalized capability needed: **querying across multiple DuckDB files as if they were one logical database**.

Phase 3's `ReadOnlyConnectionPool` queries one DuckDB file (the active interval). Phase 4 adds `MultiIntervalReader` which queries across N DuckDB files via DuckDB's `ATTACH` mechanism. The aggregator uses this to read source data; the offline viewer uses it (in a degenerate one-file form) for consistency with what Phase 5 will need.

### 4.1 DuckDB ATTACH Semantics

DuckDB supports attaching additional database files to a connection:

```sql
ATTACH 'C:/intervals/blue-cmd-01/20260519T140000Z/events.duckdb' AS i1_blue_cmd_01 (READ_ONLY);
ATTACH 'C:/intervals/blue-cmd-01/20260519T150000Z/events.duckdb' AS i2_blue_cmd_01 (READ_ONLY);

-- Query unions data across attached databases
SELECT 'i1_blue_cmd_01' AS source, * FROM i1_blue_cmd_01.events
UNION ALL
SELECT 'i2_blue_cmd_01' AS source, * FROM i2_blue_cmd_01.events
ORDER BY publish_wallclock;
```

Key properties:
- ATTACH is per-connection (not per-database)
- Each attached database gets a schema alias; tables are referenced as `alias.table_name`
- Read-only attaches are supported via `(READ_ONLY)`
- Attaching the same path twice on one connection is an error
- DETACH releases the attachment but does not close the underlying file handles (those release when the connection closes)

DuckDB does not have a built-in "logical union view across attached DBs". The reader must construct UNION ALL queries explicitly.

### 4.2 AttachedDatabaseManager

```csharp
namespace Tracer.Storage.DuckDB.MultiInterval;

/// <summary>
/// Manages a set of attached read-only DuckDB databases on a single connection.
/// Aliases are generated and stable for the manager's lifetime.
/// </summary>
public sealed class AttachedDatabaseManager : IAsyncDisposable
{
    private readonly DuckDBConnection _connection;
    private readonly Dictionary<string, AttachedDatabase> _attachments = new();
    private bool _disposed;

    public AttachedDatabaseManager(DuckDBConnection connection)
    {
        _connection = connection;
    }

    /// <summary>Attaches a database file under a generated alias. Returns the alias.</summary>
    public async Task<string> AttachAsync(string dbPath, string aliasHint, CancellationToken ct)
    {
        var alias = MakeSafeAlias(aliasHint);
        if (_attachments.ContainsKey(alias))
            throw new InvalidOperationException($"Alias {alias} already attached");

        await using var cmd = _connection.CreateCommand();
        // ATTACH path is not parameterizable; sanitize alias (controlled input) and
        // escape the path defensively.
        cmd.CommandText = $"ATTACH '{EscapeSqlString(dbPath)}' AS {alias} (READ_ONLY);";
        await cmd.ExecuteNonQueryAsync(ct);

        _attachments[alias] = new AttachedDatabase(alias, dbPath);
        return alias;
    }

    public IReadOnlyDictionary<string, AttachedDatabase> Attachments => _attachments;

    public async Task DetachAsync(string alias, CancellationToken ct)
    {
        if (!_attachments.Remove(alias)) return;
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"DETACH {alias};";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        // Detach all; suppress errors during disposal
        foreach (var alias in _attachments.Keys.ToList())
        {
            try
            {
                await using var cmd = _connection.CreateCommand();
                cmd.CommandText = $"DETACH {alias};";
                await cmd.ExecuteNonQueryAsync(CancellationToken.None);
            }
            catch { /* best effort */ }
        }
        _attachments.Clear();
    }

    private static string MakeSafeAlias(string hint)
    {
        // DuckDB schema names must be valid identifiers: [a-zA-Z_][a-zA-Z0-9_]*
        var sb = new StringBuilder("db_");
        foreach (var ch in hint)
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_');
        sb.Append('_').Append(Guid.NewGuid().ToString("N").AsSpan(0, 6));
        return sb.ToString();
    }

    private static string EscapeSqlString(string s) => s.Replace("'", "''");
}

public sealed record AttachedDatabase(string Alias, string Path);
```

**Why alias generation rather than user-controlled aliases**: aliases must be valid SQL identifiers, must be unique, and must remain stable for the manager's lifetime. Letting the caller specify them creates a class of "I accidentally collided" bugs. The hint is preserved for debugging in `AttachedDatabase.Path`.

### 4.3 MultiIntervalReader

```csharp
namespace Tracer.Storage.DuckDB.MultiInterval;

/// <summary>
/// Reader that queries across multiple DuckDB files attached to one connection.
/// Builds UNION ALL queries dynamically based on attached databases.
/// </summary>
public sealed class MultiIntervalReader : IAsyncDisposable
{
    private readonly DuckDBConnection _connection;
    private readonly AttachedDatabaseManager _attachments;
    private readonly ILogger<MultiIntervalReader> _logger;
    private bool _disposed;

    public static async Task<MultiIntervalReader> CreateAsync(
        IReadOnlyList<IntervalDbFile> files,
        ILogger<MultiIntervalReader> logger,
        CancellationToken ct)
    {
        // The "primary" connection has no main DB; we work entirely in attached DBs.
        var conn = new DuckDBConnection("Data Source=:memory:");
        await conn.OpenAsync(ct);

        var manager = new AttachedDatabaseManager(conn);
        foreach (var f in files)
            await manager.AttachAsync(f.Path, f.AliasHint, ct);

        return new MultiIntervalReader(conn, manager, logger);
    }

    private MultiIntervalReader(DuckDBConnection conn, AttachedDatabaseManager attachments,
        ILogger<MultiIntervalReader> logger)
    {
        _connection = conn;
        _attachments = attachments;
        _logger = logger;
    }

    public DuckDBConnection Connection => _connection;
    public IReadOnlyDictionary<string, AttachedDatabase> Attachments => _attachments.Attachments;

    /// <summary>
    /// Builds a UNION ALL query selecting from each attached database's events table.
    /// Adds a `__source_alias` column so callers can attribute results.
    /// </summary>
    public string BuildEventsUnionSql(string whereClause = "", string orderByClause = "", int? limit = null)
    {
        if (_attachments.Attachments.Count == 0)
            return "SELECT NULL WHERE FALSE";

        var sb = new StringBuilder();
        bool first = true;
        foreach (var alias in _attachments.Attachments.Keys)
        {
            if (!first) sb.AppendLine("UNION ALL");
            sb.AppendLine($"SELECT '{alias}' as __source_alias, * FROM {alias}.events {whereClause}");
            first = false;
        }
        if (!string.IsNullOrEmpty(orderByClause)) sb.AppendLine(orderByClause);
        if (limit.HasValue) sb.AppendLine($"LIMIT {limit.Value}");
        return sb.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _attachments.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

public sealed record IntervalDbFile(string Path, string AliasHint);
```

**Design tradeoffs**:

- **UNION ALL is not free**: DuckDB optimizes UNION ALL well when underlying tables have identical schemas (they do — same `events` table everywhere). Execution is fast; planning may have measurable overhead at very large attachment counts.
- **Alias prefix in result rows** (`__source_alias`): identifies which interval each row came from. Useful for the aggregator (per-source statistics) and for debugging.
- **In-memory primary connection**: `:memory:` is the lightest primary. We use it as a host for the attached DBs.
- **Attachment count limit**: DuckDB supports many attachments (hundreds), but practical limits depend on file handle availability. The aggregator may attach ~50-200 source files for a multi-hour multi-node session. Tests verify behavior at 100+ attachments.

### 4.4 When MultiIntervalReader Is Used

| Use case | What's attached | Where |
|---|---|---|
| Aggregator reading source data | All per-node events.duckdb files for the chosen time range | `EventsConsolidator` |
| Bundle viewer querying events | Just the bundle's single `events.duckdb` (degenerate case) | offline viewer's query services |
| Phase 5 live timeline across completed intervals | Recent completed agent intervals + the current one | Phase 5 extension |

Phase 4 uses the first two. Phase 5 onward reuses the same machinery for live multi-interval queries.

---

## 5. The TracerAggregator

### 5.1 The AggregationOrchestrator (Library)

The aggregator's public API is a single class, `AggregationOrchestrator`. Both the CLI and the Web API endpoint call into it.

```csharp
namespace Tracer.Aggregator;

public sealed class AggregationOrchestrator
{
    private readonly ITelemetryStorageReader _nasReader;
    private readonly ILogger<AggregationOrchestrator> _logger;

    public AggregationOrchestrator(
        ITelemetryStorageReader nasReader,
        ILogger<AggregationOrchestrator> logger)
    {
        _nasReader = nasReader;
        _logger = logger;
    }

    public async Task<AggregationResult> RunAsync(
        AggregationRequest request,
        IAggregationProgressReporter? progress,
        CancellationToken ct)
    {
        progress?.Report(AggregationStage.Started, "Aggregation starting");
        var startedAt = DateTimeOffset.UtcNow;

        // 1. Resolve session → time range, if session ID provided
        var timeRange = await ResolveTimeRangeAsync(request, ct);
        progress?.Report(AggregationStage.TimeRangeResolved,
            $"Time range: {timeRange.StartUtc:O} to {timeRange.EndUtc:O}");

        // 2. Discover intervals from NAS overlapping the range
        var discovered = await IntervalDiscovery.FindOverlappingAsync(
            _nasReader, timeRange, request.NodeFilter, ct);
        progress?.Report(AggregationStage.IntervalsDiscovered,
            $"Found {discovered.Count} interval(s) across {discovered.NodeCount} node(s)");

        if (discovered.Count == 0)
            throw new InvalidOperationException(
                "No intervals found overlapping the requested time range");

        // 3. Extract zips to a staging directory
        await using var staging = await StagingDirectory.CreateAsync(request.OutputPath, ct);
        var extracted = await ExtractAllAsync(discovered, staging, progress, ct);
        progress?.Report(AggregationStage.IntervalsExtracted,
            $"Extracted {extracted.Count} interval(s) to {staging.Path}");

        // 4. Consolidate events DuckDB
        var eventsOutputPath = Path.Combine(staging.BundleStagingPath, "events.duckdb");
        var eventsStats = await EventsConsolidator.ConsolidateAsync(
            extracted, eventsOutputPath, timeRange, progress, ct);
        progress?.Report(AggregationStage.EventsConsolidated,
            $"Wrote {eventsStats.TotalEvents:N0} events to {eventsOutputPath}");

        // 5. Consolidate slow_state DuckDB
        var slowStatePath = Path.Combine(staging.BundleStagingPath, "slow_state.duckdb");
        var slowStateStats = await SlowStateConsolidator.ConsolidateAsync(
            extracted, slowStatePath, timeRange, progress, ct);
        progress?.Report(AggregationStage.SlowStateConsolidated,
            $"Wrote {slowStateStats.TotalSamples:N0} slow-state samples");

        // 6. Copy fast-state Parquet files per inclusion policy
        var fastStateStats = await FastStateCopier.CopyAsync(
            extracted, staging.BundleStagingPath, request.FastStateScope,
            request.FastStateEntities, timeRange, progress, ct);
        progress?.Report(AggregationStage.FastStateCopied,
            $"Copied {fastStateStats.TotalRowCount:N0} fast-state rows for {fastStateStats.EntityCount} entities");

        // 7. Build scenario, topology, source_intervals metadata files
        var scenario = await ScenarioMetadataCollector.CollectAsync(eventsOutputPath, timeRange, ct);
        var topology = TopologyExtractor.Extract(extracted, timeRange);
        var sourceIntervals = SourceIntervalsBuilder.Build(extracted);

        await BundleMetadataWriter.WriteAsync(
            staging.BundleStagingPath, scenario, topology, sourceIntervals, ct);
        progress?.Report(AggregationStage.MetadataWritten, "Metadata files written");

        // 8. Compute checksums and write manifest
        var manifest = await ManifestBuilder.BuildAsync(
            staging.BundleStagingPath, request, timeRange, scenario,
            new BundleStatistics
            {
                TotalEvents = eventsStats.TotalEvents,
                TotalSlowStateSamples = slowStateStats.TotalSamples,
                TotalFastStateRows = fastStateStats.TotalRowCount,
                UncompressedBytes = ComputeUncompressedSize(staging.BundleStagingPath)
            }, ct);
        progress?.Report(AggregationStage.ManifestWritten, $"Bundle ID: {manifest.BundleId}");

        // 9. Move staging directory to final output path, or zip if requested
        var finalPath = await FinalizeAsync(staging, request.OutputPath, ct);
        progress?.Report(AggregationStage.Completed, $"Bundle complete: {finalPath}");

        return new AggregationResult
        {
            BundleId = manifest.BundleId,
            OutputPath = finalPath,
            TimeRange = timeRange,
            Statistics = manifest.Statistics,
            Duration = DateTimeOffset.UtcNow - startedAt,
            SourceIntervalsUsed = extracted.Count
        };
    }

    private async Task<TimeRange> ResolveTimeRangeAsync(AggregationRequest request, CancellationToken ct)
    {
        if (request.TimeRange.HasValue) return request.TimeRange.Value;

        if (request.SessionId is not null)
        {
            var range = await SessionResolver.ResolveAsync(_nasReader, request.SessionId, ct);
            return range ?? throw new InvalidOperationException(
                $"Session {request.SessionId} not found in any reachable interval");
        }

        throw new ArgumentException(
            "Aggregation request must specify either TimeRange or SessionId", nameof(request));
    }
}
```

### 5.2 AggregationRequest

```csharp
namespace Tracer.Aggregator.Configuration;

public sealed record AggregationRequest
{
    /// <summary>Specify either TimeRange or SessionId; not both.</summary>
    public TimeRange? TimeRange { get; init; }
    public string? SessionId { get; init; }

    /// <summary>If null, all nodes that have data in the time range are included.</summary>
    public IReadOnlyList<string>? NodeFilter { get; init; }

    public FastStateScope FastStateScope { get; init; } = FastStateScope.None;
    public IReadOnlyList<string>? FastStateEntities { get; init; }

    /// <summary>Absolute path. Ending in .zip means produce a zipped bundle; otherwise a directory.</summary>
    public required string OutputPath { get; init; }

    /// <summary>Optional human-readable label override; otherwise pulled from session-start event.</summary>
    public string? LabelOverride { get; init; }
}

public enum FastStateScope { None, SelectedEntities, All }
```

### 5.3 IntervalDiscovery

```csharp
namespace Tracer.Aggregator.Discovery;

public static class IntervalDiscovery
{
    public static async Task<DiscoveredIntervals> FindOverlappingAsync(
        ITelemetryStorageReader reader,
        TimeRange timeRange,
        IReadOnlyList<string>? nodeFilter,
        CancellationToken ct)
    {
        var allNodes = await reader.ListNodesAsync(ct);
        var nodes = nodeFilter is null
            ? allNodes
            : allNodes.Where(n => nodeFilter.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();

        var intervals = new List<DiscoveredInterval>();
        foreach (var nodeId in nodes)
        {
            var nodeIntervals = await reader.ListIntervalsAsync(nodeId, ct);
            foreach (var iv in nodeIntervals)
            {
                if (Overlaps(iv, timeRange))
                    intervals.Add(new DiscoveredInterval(nodeId, iv));
            }
        }
        return new DiscoveredIntervals(intervals);
    }

    private static bool Overlaps(IntervalDescriptor iv, TimeRange range)
    {
        // Interval [start, end) overlaps range [from, to) iff start < to AND end > from
        return iv.StartUtc < range.EndUtc.ToDateTimeOffset()
            && iv.EndUtc   > range.StartUtc.ToDateTimeOffset();
    }
}

public sealed record DiscoveredIntervals(IReadOnlyList<DiscoveredInterval> Intervals)
{
    public int Count => Intervals.Count;
    public int NodeCount => Intervals.Select(i => i.NodeId).Distinct().Count();
}

public sealed record DiscoveredInterval(string NodeId, IntervalDescriptor Descriptor);
```

`ITelemetryStorageReader` is the abstraction from Phase 2 §10.4. Its `LocalFileSystemStorageReader` implementation lists per-node directories under the mock-NAS root and parses interval filenames to compute time ranges.

### 5.4 SessionResolver

When the caller provides `SessionId` instead of `TimeRange`, the resolver scans available interval manifests for session-start/session-end markers.

```csharp
namespace Tracer.Aggregator.Discovery;

public static class SessionResolver
{
    public static async Task<TimeRange?> ResolveAsync(
        ITelemetryStorageReader reader,
        string sessionId,
        CancellationToken ct)
    {
        // Scan manifests (small JSON files, not the DuckDB itself).
        // Phase 2 §6.7 specifies that each interval manifest includes a sessionMarkers field
        // listing session-start/session-end events observed during that interval.
        var allNodes = await reader.ListNodesAsync(ct);

        DateTimeOffset? startedAt = null;
        DateTimeOffset? endedAt = null;

        foreach (var nodeId in allNodes)
        {
            var intervals = await reader.ListIntervalsAsync(nodeId, ct);
            foreach (var iv in intervals.OrderByDescending(i => i.StartUtc))
            {
                var manifest = await reader.ReadIntervalManifestAsync(nodeId, iv, ct);
                if (manifest is null) continue;

                foreach (var marker in manifest.SessionMarkers)
                {
                    if (marker.SessionId != sessionId) continue;
                    var when = marker.Wallclock.ToDateTimeOffset();
                    if (marker.Type == SessionMarkerType.Start && (startedAt is null || when < startedAt))
                        startedAt = when;
                    if (marker.Type == SessionMarkerType.End && (endedAt is null || when > endedAt))
                        endedAt = when;
                }
            }
        }

        if (startedAt is null) return null;
        // Session still running: use now as end
        var end = endedAt ?? DateTimeOffset.UtcNow;
        return new TimeRange(
            WallclockTime.FromDateTimeOffset(startedAt.Value),
            WallclockTime.FromDateTimeOffset(end));
    }
}
```

**Efficiency note**: this scans interval manifests, which are small JSON files (~few KB each). For a fleet of 20 nodes × 24 intervals/day, that's 480 small reads — negligible. The manifests already expose session markers precisely for this use case (Phase 2 §6.7).

### 5.5 EventsConsolidator

For each source interval, this inserts rows into the bundle's consolidated `events.duckdb`. Architecture §13.3 specifies that, in a real DDS deployment, the publisher's same event appears once per subscribing node — so consolidation preserves that duplication.

In Phase 4, **mock data has only self-published events per node** (no inter-node subscription via the mock transport), so each event appears once per consolidation. Replication-latency analysis (Phase 9) will produce meaningful data only after the DDS adapter (Phase 11) introduces cross-node subscription.

```csharp
namespace Tracer.Aggregator.Consolidation;

public static class EventsConsolidator
{
    public static async Task<ConsolidationStats> ConsolidateAsync(
        IReadOnlyList<ExtractedInterval> sources,
        string outputDbPath,
        TimeRange timeRange,
        IAggregationProgressReporter? progress,
        CancellationToken ct)
    {
        // 1. Create output DB with schema
        await using var output = new DuckDBConnection($"Data Source={outputDbPath}");
        await output.OpenAsync(ct);
        await using (var cmd = output.CreateCommand())
        {
            cmd.CommandText = SchemaV1.CreateEventsTable;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 2. For each source, ATTACH and INSERT rows within the time range
        long totalEvents = 0;
        int idx = 0;
        foreach (var source in sources)
        {
            idx++;
            var srcPath = Path.Combine(source.Directory, "events.duckdb");
            var alias = $"src_{idx}";

            await using (var cmd = output.CreateCommand())
            {
                cmd.CommandText = $"ATTACH '{EscapeSql(srcPath)}' AS {alias} (READ_ONLY);";
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await using (var cmd = output.CreateCommand())
            {
                cmd.CommandText = $"""
                    INSERT INTO events
                    SELECT * FROM {alias}.events
                    WHERE publish_wallclock >= $from
                      AND publish_wallclock <  $to;
                    """;
                cmd.Parameters.Add(new DuckDBParameter("from", timeRange.StartUtc.ToDateTimeOffset()));
                cmd.Parameters.Add(new DuckDBParameter("to",   timeRange.EndUtc.ToDateTimeOffset()));
                var inserted = await cmd.ExecuteNonQueryAsync(ct);
                totalEvents += inserted;
                progress?.Report(AggregationStage.EventsConsolidating,
                    $"  {source.NodeId} {source.Descriptor.Timestamp.Value}: +{inserted:N0} ({idx}/{sources.Count})");
            }

            await using (var cmd = output.CreateCommand())
            {
                cmd.CommandText = $"DETACH {alias};";
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }

        // 3. Build indexes (Phase 1 §4.2)
        await using (var cmd = output.CreateCommand())
        {
            cmd.CommandText = SchemaV1.CreateIndexes;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 4. CHECKPOINT to flush WAL to main file (bundle should be a clean single file)
        await using (var cmd = output.CreateCommand())
        {
            cmd.CommandText = "CHECKPOINT;";
            await cmd.ExecuteNonQueryAsync(ct);
        }

        return new ConsolidationStats { TotalEvents = totalEvents };
    }

    private static string EscapeSql(string s) => s.Replace("'", "''");
}

public sealed record ConsolidationStats
{
    public required long TotalEvents { get; init; }
}
```

**Implementation note**: DuckDB's INSERT FROM attached database is fast — internally it streams rows without going through .NET round-trips. For a 100M-row consolidation, the data path is DuckDB → DuckDB direct, no marshalling cost.

**Note on `ExecuteNonQueryAsync` return value**: DuckDB.NET versions vary in whether they return affected row count. The above code assumes they do; if a particular version returns -1 instead, the implementer can compute `SELECT COUNT(*)` deltas instead.

### 5.6 FastStateCopier

Fast state is in Parquet files. The copier filters by entity per the scope policy.

```csharp
namespace Tracer.Aggregator.Consolidation;

public static class FastStateCopier
{
    public static async Task<FastStateCopyStats> CopyAsync(
        IReadOnlyList<ExtractedInterval> sources,
        string bundleStagingPath,
        FastStateScope scope,
        IReadOnlyList<string>? entityFilter,
        TimeRange timeRange,
        IAggregationProgressReporter? progress,
        CancellationToken ct)
    {
        if (scope == FastStateScope.None)
            return new FastStateCopyStats { EntityCount = 0, TotalRowCount = 0 };

        var bundleFastStateDir = Path.Combine(bundleStagingPath, "fast_state");
        Directory.CreateDirectory(bundleFastStateDir);

        long totalRows = 0;
        var entitiesSeen = new HashSet<string>();

        foreach (var source in sources)
        {
            var srcFastDir = Path.Combine(source.Directory, "fast_state");
            if (!Directory.Exists(srcFastDir)) continue;

            // Each *.parquet file is one topic for this node-interval
            foreach (var parquetFile in Directory.EnumerateFiles(srcFastDir, "*.parquet"))
            {
                var topic = Path.GetFileNameWithoutExtension(parquetFile);
                var rowsCopied = await SplitAndCopyByEntityAsync(
                    parquetFile, topic, bundleFastStateDir, scope, entityFilter,
                    timeRange, entitiesSeen, ct);
                totalRows += rowsCopied;
            }
            progress?.Report(AggregationStage.FastStateCopying,
                $"  Processed {source.NodeId} {source.Descriptor.Timestamp.Value}");
        }

        return new FastStateCopyStats
        {
            EntityCount = entitiesSeen.Count,
            TotalRowCount = totalRows
        };
    }

    private static async Task<long> SplitAndCopyByEntityAsync(
        string srcParquet, string topic, string bundleFastStateDir,
        FastStateScope scope, IReadOnlyList<string>? entityFilter,
        TimeRange timeRange, HashSet<string> entitiesSeen,
        CancellationToken ct)
    {
        await using var conn = new DuckDBConnection("Data Source=:memory:");
        await conn.OpenAsync(ct);

        // Discover entities present
        await using var distinctCmd = conn.CreateCommand();
        distinctCmd.CommandText = $"""
            SELECT DISTINCT instance_key
            FROM read_parquet('{EscapeSql(srcParquet)}')
            WHERE publish_wallclock >= $from AND publish_wallclock < $to
            """;
        distinctCmd.Parameters.Add(new DuckDBParameter("from", timeRange.StartUtc.ToDateTimeOffset()));
        distinctCmd.Parameters.Add(new DuckDBParameter("to",   timeRange.EndUtc.ToDateTimeOffset()));

        var entitiesInSource = new List<string>();
        await using var reader = await distinctCmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            entitiesInSource.Add(reader.GetString(0));

        long totalRowsWritten = 0;
        foreach (var entity in entitiesInSource)
        {
            if (scope == FastStateScope.SelectedEntities &&
                (entityFilter is null || !entityFilter.Contains(entity)))
                continue;

            entitiesSeen.Add(entity);
            var safeTopic = BundleNaming.SafeFileName(topic);
            var safeEntity = BundleNaming.SafeFileName(entity);
            var outDir = Path.Combine(bundleFastStateDir, safeTopic, safeEntity);
            Directory.CreateDirectory(outDir);
            var outPath = Path.Combine(outDir, "samples.parquet");

            long rowsThisCopy = await WriteOrAppendParquetAsync(
                conn, srcParquet, entity, outPath, timeRange, ct);
            totalRowsWritten += rowsThisCopy;
        }

        return totalRowsWritten;
    }

    private static async Task<long> WriteOrAppendParquetAsync(
        DuckDBConnection conn, string srcParquet, string entity, string outPath,
        TimeRange timeRange, CancellationToken ct)
    {
        // If outPath exists (this entity already had samples from a prior source interval),
        // we need to merge. DuckDB's COPY ... TO doesn't append. We read both, union, write to tmp,
        // atomic-replace.
        bool exists = File.Exists(outPath);

        var sql = exists
            ? $"""
                COPY (
                    SELECT * FROM read_parquet('{EscapeSql(outPath)}')
                    UNION ALL
                    SELECT * FROM read_parquet('{EscapeSql(srcParquet)}')
                    WHERE instance_key = $entity
                      AND publish_wallclock >= $from AND publish_wallclock < $to
                ) TO '{EscapeSql(outPath + ".tmp")}' (FORMAT PARQUET);
                """
            : $"""
                COPY (
                    SELECT * FROM read_parquet('{EscapeSql(srcParquet)}')
                    WHERE instance_key = $entity
                      AND publish_wallclock >= $from AND publish_wallclock < $to
                ) TO '{EscapeSql(outPath)}' (FORMAT PARQUET);
                """;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new DuckDBParameter("entity", entity));
        cmd.Parameters.Add(new DuckDBParameter("from",   timeRange.StartUtc.ToDateTimeOffset()));
        cmd.Parameters.Add(new DuckDBParameter("to",     timeRange.EndUtc.ToDateTimeOffset()));
        await cmd.ExecuteNonQueryAsync(ct);

        if (exists)
        {
            File.Delete(outPath);
            File.Move(outPath + ".tmp", outPath);
        }

        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM read_parquet('{EscapeSql(outPath)}')";
        return (long)(await countCmd.ExecuteScalarAsync(ct))!;
    }

    private static string EscapeSql(string s) => s.Replace("'", "''");
}

public sealed record FastStateCopyStats
{
    public required int EntityCount { get; init; }
    public required long TotalRowCount { get; init; }
}
```

**Performance note**: read-and-rewrite for the append case is O(N) per source. For a bundle with N source intervals and K entities, total work is O(N*K) reads. At Phase 4's expected scale (a few dozen intervals × a few dozen entities), this is seconds to a few minutes. Phase 7 may revisit if entity-history workflows demand much larger fast-state bundles.

### 5.7 Progress Reporting

```csharp
namespace Tracer.Aggregator.Progress;

public interface IAggregationProgressReporter
{
    void Report(AggregationStage stage, string message);
}

public enum AggregationStage
{
    Started,
    TimeRangeResolved,
    IntervalsDiscovered,
    IntervalsExtracted,
    EventsConsolidating,
    EventsConsolidated,
    SlowStateConsolidated,
    FastStateCopying,
    FastStateCopied,
    MetadataWritten,
    ManifestWritten,
    Completed,
    Failed
}
```

The CLI uses a console-friendly reporter (writes to stderr with stage and message); the Web API uses one that updates a status record queryable via `GET /api/bundles/{bundleId}/status`.

---

## 6. The CLI: tracer-aggregate.exe

### 6.1 Command Structure

```
tracer-aggregate <command> [options]

Commands:
  build       Build a bundle from telemetry data
  validate    Validate an existing bundle's manifest, checksums, schema
  inspect     Show bundle manifest contents

Common options:
  --nas-root <path>         Path to the (mock) NAS root containing /Telemetry/{nodeId}/*.zip
  --log-level <level>       trace, debug, information (default), warning, error
```

### 6.2 `build` Command

```
tracer-aggregate build [options]

Options (you must specify either --session-id or --time-range, not both):
  --session-id <id>             Build a bundle for a specific session
  --time-range <start>..<end>   Build a bundle for a time range (ISO 8601 UTC, inclusive..exclusive)

  --output <path>               (required) Where to write the bundle.
                                Ending in .zip produces a zipped bundle; otherwise a directory.
  --nodes <id1,id2,...>         (optional) Restrict to specific nodes. Default: all available.
  --fast-state <scope>          (optional) none | selected | all. Default: none.
  --fast-state-entities <list>  (required if --fast-state selected) Comma-separated entity IDs.
  --label <text>                (optional) Override the bundle's label.
  --force                       (optional) Overwrite output path if it exists.

Examples:
  tracer-aggregate build \
    --nas-root C:/Tracer/mock-nas \
    --session-id 5b2f0c40-1234-5678-9abc-def012345678 \
    --output C:/bundles/training_run.tracerbundle

  tracer-aggregate build \
    --nas-root C:/Tracer/mock-nas \
    --time-range "2026-05-19T14:00:00Z..2026-05-19T15:00:00Z" \
    --nodes blue-cmd-01,blue-veh-01 \
    --fast-state selected \
    --fast-state-entities vehicle:blue:17,vehicle:red:03 \
    --output C:/bundles/engagement.tracerbundle.zip
```

### 6.3 `validate` Command

```
tracer-aggregate validate <bundle-path> [--strict]

Returns exit code 0 on success, non-zero on failure.

Validation steps:
  1. Bundle path exists and is readable
  2. Manifest is well-formed JSON
  3. schemaVersion is recognized
  4. Every file listed in manifest.files exists
  5. Every file's sizeBytes matches actual file size
  6. Every file's sha256 matches actual file content (if --strict)
  7. checksums.txt and manifest.files agree
  8. scenario.json, topology.json, source_intervals.json are well-formed
  9. events.duckdb opens cleanly and has the expected events table schema
 10. slow_state.duckdb opens cleanly

Without --strict, step 6 is skipped (just verifies sizes match). With --strict, full hash
verification runs — slower but catches corruption.

Examples:
  tracer-aggregate validate C:/bundles/training_run.tracerbundle
  tracer-aggregate validate C:/bundles/training_run.tracerbundle.zip --strict
```

### 6.4 `inspect` Command

```
tracer-aggregate inspect <bundle-path>

Outputs a human-readable summary:
  - Bundle ID
  - Created at, time range, label
  - Statistics (event count, etc.)
  - Participating nodes
  - Fast-state scope and entities
  - File list with sizes

Example output:
  Bundle: training_run.tracerbundle
  ID:          01H8XYZ7K3M4P5Q6R7S8T9V0W1
  Schema:      v1 (compatible)
  Created:     2026-05-20T09:30:00Z by tracer-aggregate 1.0.0 on support-laptop-03
  Time range:  2026-05-19T14:03:22Z .. 2026-05-19T14:38:51Z (35m 29s)
  Label:       Tuesday morning training run
  Session:     5b2f0c40-1234-5678-9abc-def012345678 (combat_engagement_v3)

  Statistics:
    Events:               1,247,831
    Slow-state samples:   8,420
    Fast-state rows:      184,200
    Uncompressed bytes:   236.4 MB

  Participating nodes (5):
    blue-cmd-01, blue-veh-01, blue-veh-02, red-cmd-01, red-veh-01

  Fast-state scope: selected-entities (2 entities: vehicle:blue:17, vehicle:red:03)

  Files (8):
    events.duckdb                                             40.0 MB  a3f2b4c8...
    slow_state.duckdb                                          0.5 MB  b4c5d6e7...
    scenario.json                                              4.0 KB  c5d6e7f8...
```

### 6.5 Program.cs

```csharp
namespace Tracer.Aggregator.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var root = BuildRootCommand();
        return await root.InvokeAsync(args);
    }

    private static RootCommand BuildRootCommand()
    {
        var nasRootOption = new Option<string?>("--nas-root", "Path to (mock) NAS root");
        var logLevelOption = new Option<string>("--log-level", () => "information");

        var root = new RootCommand("Tracer Aggregator — build, validate, and inspect bundles");
        root.AddGlobalOption(nasRootOption);
        root.AddGlobalOption(logLevelOption);

        root.AddCommand(BuildCommand.Create(nasRootOption, logLevelOption));
        root.AddCommand(ValidateCommand.Create(logLevelOption));
        root.AddCommand(InspectCommand.Create());

        root.SetHandler(async (InvocationContext ctx) =>
        {
            await ctx.HelpBuilder.Write(ctx);
            ctx.ExitCode = 1;  // error: command required
        });

        return root;
    }
}
```

`Tracer.Aggregator.Cli.Commands.BuildCommand`, `ValidateCommand`, `InspectCommand` each set up their options and bind to the appropriate library calls.

### 6.6 LOG_FILE Convention

The CLI follows the convention from Phase 2 / 3:

```
$ tracer-aggregate build ...
LOG_FILE=C:/Users/support/AppData/Local/Tracer/cli-logs/tracer-aggregate-2026-05-20.json
[info] Aggregation starting
[info] Time range resolved: 2026-05-19T14:00:00Z to 2026-05-19T15:00:00Z
[info] Found 5 interval(s) across 3 node(s)
[info] Extracted 5 interval(s)
[info] Wrote 247,831 events
...
[info] Bundle complete: C:/bundles/engagement.tracerbundle
```

`LOG_FILE=` is printed to stdout first. All other output goes to stderr by default. This makes piping behave correctly: `tracer-aggregate build ... > log.txt` captures only the log-file announcement on stdout; the progress is visible on stderr.

---

## 7. Web API Additions

### 7.1 New Endpoints

```
POST   /api/bundles/build                   start a bundle build; returns bundleId
GET    /api/bundles                         list known bundles (built by this observer instance)
GET    /api/bundles/{bundleId}              metadata about a specific bundle (manifest contents)
GET    /api/bundles/{bundleId}/status       progress / result of a build
GET    /api/bundles/{bundleId}/download     download the bundle as a zip stream
DELETE /api/bundles/{bundleId}              remove a built bundle from disk
```

These endpoints exist on the **observer**'s API surface. The offline viewer (§8) does not build bundles — it serves an already-built one and exposes a different set of bundle management endpoints (open/close).

### 7.2 BundleEndpoints.cs

```csharp
namespace Tracer.WebApi.Endpoints;

public static class BundleEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/bundles/build", HandleBuildAsync).WithOpenApi();
        app.MapGet("/api/bundles", HandleListAsync).WithOpenApi();
        app.MapGet("/api/bundles/{bundleId}", HandleGetAsync).WithOpenApi();
        app.MapGet("/api/bundles/{bundleId}/status", HandleStatusAsync).WithOpenApi();
        app.MapGet("/api/bundles/{bundleId}/download", HandleDownloadAsync).WithOpenApi();
        app.MapDelete("/api/bundles/{bundleId}", HandleDeleteAsync).WithOpenApi();
    }

    public static async Task<Results<Accepted<BundleBuildAcceptedDto>, ProblemHttpResult>> HandleBuildAsync(
        [FromBody] BundleBuildRequestDto request,
        [FromServices] BundleBuildService builds,
        CancellationToken ct)
    {
        try
        {
            var bundleId = await builds.QueueBuildAsync(request, ct);
            return TypedResults.Accepted(
                $"/api/bundles/{bundleId}/status",
                new BundleBuildAcceptedDto { BundleId = bundleId });
        }
        catch (Exception ex)
        {
            return TypedResults.Problem(ProblemDetailsFactory.From(ex));
        }
    }

    public static Task<Ok<BundleBuildStatusDto>> HandleStatusAsync(
        string bundleId,
        [FromServices] BundleBuildService builds,
        CancellationToken ct)
    {
        var status = builds.GetStatus(bundleId);
        return Task.FromResult(TypedResults.Ok(status));
    }

    public static async Task<Results<Ok<BundleListDto>, ProblemHttpResult>> HandleListAsync(
        [FromServices] BundleCatalog catalog, CancellationToken ct)
    {
        var entries = await catalog.ListAsync(ct);
        return TypedResults.Ok(new BundleListDto { Bundles = entries });
    }

    public static async Task<Results<Ok<BundleManifestDto>, NotFound>> HandleGetAsync(
        string bundleId,
        [FromServices] BundleCatalog catalog, CancellationToken ct)
    {
        var manifest = await catalog.GetManifestAsync(bundleId, ct);
        return manifest is null ? TypedResults.NotFound() : TypedResults.Ok(manifest);
    }

    public static async Task<Results<FileStreamHttpResult, NotFound>> HandleDownloadAsync(
        string bundleId,
        [FromServices] BundleCatalog catalog, CancellationToken ct)
    {
        var bundle = await catalog.GetAsync(bundleId, ct);
        if (bundle is null) return TypedResults.NotFound();

        if (bundle.IsZipped)
        {
            var stream = File.OpenRead(bundle.Path);
            return TypedResults.File(stream, "application/zip", $"{bundleId}.tracerbundle.zip");
        }

        // Directory: stream-zip on the fly. ZipArchive writes through a pipe to the response.
        var pipe = new Pipe();
        _ = Task.Run(async () =>
        {
            try
            {
                using var archive = new ZipArchive(pipe.Writer.AsStream(), ZipArchiveMode.Create, leaveOpen: false);
                foreach (var file in Directory.EnumerateFiles(bundle.Path, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(bundle.Path, file);
                    var entry = archive.CreateEntry(rel, CompressionLevel.NoCompression);
                    await using var entryStream = entry.Open();
                    await using var src = File.OpenRead(file);
                    await src.CopyToAsync(entryStream, ct);
                }
            }
            finally { await pipe.Writer.CompleteAsync(); }
        }, ct);
        return TypedResults.File(pipe.Reader.AsStream(), "application/zip", $"{bundleId}.tracerbundle.zip");
    }

    public static async Task<Results<NoContent, NotFound>> HandleDeleteAsync(
        string bundleId,
        [FromServices] BundleCatalog catalog, CancellationToken ct)
    {
        var removed = await catalog.DeleteAsync(bundleId, ct);
        return removed ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
```

### 7.3 BundleBuildService

```csharp
namespace Tracer.WebApi.Bundles;

public sealed class BundleBuildService
{
    private readonly AggregationOrchestrator _aggregator;
    private readonly BundleCatalog _catalog;
    private readonly ConcurrentDictionary<string, BundleBuildStatusDto> _statuses = new();
    private readonly SemaphoreSlim _serializeBuilds = new(1, 1);  // Phase 4: one build at a time
    private readonly ILogger<BundleBuildService> _logger;

    public BundleBuildService(
        AggregationOrchestrator aggregator,
        BundleCatalog catalog,
        ILogger<BundleBuildService> logger)
    {
        _aggregator = aggregator;
        _catalog = catalog;
        _logger = logger;
    }

    public Task<string> QueueBuildAsync(BundleBuildRequestDto request, CancellationToken ct)
    {
        var bundleId = Ulid.NewUlid().ToString();
        var outputPath = Path.Combine(_catalog.BundlesRoot, $"{bundleId}.tracerbundle");

        var status = new BundleBuildStatusDto
        {
            BundleId = bundleId,
            State = "Queued",
            QueuedAtUtc = DateTimeOffset.UtcNow,
            OutputPath = outputPath
        };
        _statuses[bundleId] = status;

        // Background — don't await the build itself
        _ = Task.Run(async () => await RunBuildAsync(bundleId, request, outputPath, ct), CancellationToken.None);

        return Task.FromResult(bundleId);
    }

    private async Task RunBuildAsync(string bundleId, BundleBuildRequestDto request,
        string outputPath, CancellationToken ct)
    {
        await _serializeBuilds.WaitAsync(ct);
        try
        {
            UpdateStatus(bundleId, s => s with
            {
                State = "InProgress",
                StartedAtUtc = DateTimeOffset.UtcNow
            });

            var aggregationRequest = MapToAggregationRequest(request, outputPath);
            var progress = new StatusUpdatingProgressReporter(bundleId, _statuses);
            var result = await _aggregator.RunAsync(aggregationRequest, progress, ct);

            UpdateStatus(bundleId, s => s with
            {
                State = "Completed",
                CompletedAtUtc = DateTimeOffset.UtcNow,
                OutputPath = result.OutputPath
            });
            await _catalog.RegisterAsync(bundleId, result.OutputPath, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bundle build {BundleId} failed", bundleId);
            UpdateStatus(bundleId, s => s with
            {
                State = "Failed",
                Error = ex.Message,
                CompletedAtUtc = DateTimeOffset.UtcNow
            });
        }
        finally { _serializeBuilds.Release(); }
    }

    public BundleBuildStatusDto GetStatus(string bundleId)
        => _statuses.TryGetValue(bundleId, out var s)
            ? s
            : new BundleBuildStatusDto
            {
                BundleId = bundleId,
                State = "Unknown",
                QueuedAtUtc = DateTimeOffset.MinValue
            };

    private void UpdateStatus(string bundleId, Func<BundleBuildStatusDto, BundleBuildStatusDto> mutator)
    {
        _statuses.AddOrUpdate(bundleId,
            _ => mutator(new BundleBuildStatusDto { BundleId = bundleId, State = "Unknown", QueuedAtUtc = DateTimeOffset.UtcNow }),
            (_, existing) => mutator(existing));
    }

    private static AggregationRequest MapToAggregationRequest(BundleBuildRequestDto dto, string outputPath)
    {
        return new AggregationRequest
        {
            SessionId = dto.SessionId,
            TimeRange = dto.TimeRange is null ? null : new TimeRange(
                WallclockTime.FromDateTimeOffset(dto.TimeRange.StartUtc),
                WallclockTime.FromDateTimeOffset(dto.TimeRange.EndUtc)),
            NodeFilter = dto.NodeFilter,
            FastStateScope = Enum.Parse<FastStateScope>(dto.FastStateScope, ignoreCase: true),
            FastStateEntities = dto.FastStateEntities,
            OutputPath = outputPath,
            LabelOverride = dto.LabelOverride
        };
    }
}
```

**Phase 4 simplification**: only one bundle build runs at a time (serialized with a `SemaphoreSlim`). Bundle builds are CPU and IO heavy; queuing multiple isn't a Phase 4 concern. A capacity-limited queue is a Phase 8+ refinement.

### 7.4 DTOs for Bundle Endpoints

```csharp
namespace Tracer.WebApi.Contracts.Dto;

public sealed record BundleBuildRequestDto
{
    public string? SessionId { get; init; }
    public TimeRangeDto? TimeRange { get; init; }
    public IReadOnlyList<string>? NodeFilter { get; init; }
    public string FastStateScope { get; init; } = "None";  // None | SelectedEntities | All
    public IReadOnlyList<string>? FastStateEntities { get; init; }
    public string? LabelOverride { get; init; }
}

public sealed record BundleBuildAcceptedDto
{
    public required string BundleId { get; init; }
}

public sealed record BundleBuildStatusDto
{
    public required string BundleId { get; init; }
    public required string State { get; init; }              // Queued | InProgress | Completed | Failed | Unknown
    public required DateTimeOffset QueuedAtUtc { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public string? Error { get; init; }
    public string? CurrentStage { get; init; }
    public string? CurrentStageMessage { get; init; }
    public string? OutputPath { get; init; }
}

public sealed record BundleListDto
{
    public required IReadOnlyList<BundleListEntryDto> Bundles { get; init; }
}

public sealed record BundleListEntryDto
{
    public required string BundleId { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required TimeRangeDto TimeRange { get; init; }
    public required long SizeBytes { get; init; }
    public string? Label { get; init; }
    public string? SessionId { get; init; }
}

public sealed record TimeRangeDto
{
    public required DateTimeOffset StartUtc { get; init; }
    public required DateTimeOffset EndUtc { get; init; }
}
```

`BundleManifestDto` mirrors `BundleManifest` from `Tracer.Bundle.Format` — all manifest fields exposed as JSON-friendly types.

### 7.5 Observer DI Additions

In `ObserverHostBuilder` (Phase 3 §3.4) — additions only:

```csharp
// Bundle services
builder.Services.AddSingleton<BundleCatalog>();
builder.Services.AddSingleton<ITelemetryStorageReader>(sp =>
    new LocalFileSystemStorageReader(
        sp.GetRequiredService<ObserverConfig>().NasMockRoot,
        sp.GetRequiredService<ILogger<LocalFileSystemStorageReader>>()));
builder.Services.AddSingleton<AggregationOrchestrator>();
builder.Services.AddSingleton<BundleBuildService>();
```

In `ConfigureMiddleware`:

```csharp
BundleEndpoints.Map(app);   // new
```

`ObserverConfig` gains two fields:

```csharp
public sealed class ObserverConfig
{
    // ... existing fields ...

    /// <summary>Where built bundles are stored on the observer's disk.</summary>
    public string BundlesRoot { get; set; } = "";   // absolute

    /// <summary>Where the mock-NAS data lives (read source). Same as the FakeNode's upload destination.</summary>
    public string NasMockRoot { get; set; } = "";   // absolute
}
```

Example observer.json update:

```json
{
  "Observer": {
    ...
    "BundlesRoot": "C:/ProgramData/Tracer/observer/bundles",
    "NasMockRoot": "C:/ProgramData/Tracer/mock-nas"
  }
}
```

---

## 8. The Offline Viewer

### 8.1 Architecture

The offline viewer is a **second runnable executable** (`tracer-viewer.exe`) that serves the same Vue SPA against a bundle file instead of a live observer.

```
tracer-viewer.exe
  ├─ Single .NET 8 self-contained executable
  ├─ Embeds the Vue SPA assets (same build output as ships with the Observer)
  ├─ At startup:
  │   - Accepts a bundle path on command line, or
  │   - Opens "Open Bundle..." UI on first run
  ├─ Opens the bundle's DuckDB read-only via the connection pool
  ├─ Serves the same /api/* endpoints as the Observer, against the bundle
  ├─ Launches the default browser at localhost
  └─ Stays running until user closes browser tab AND quits the tray icon (or Ctrl+C)
```

Key differences from the Observer:
- **No ingestion pipeline.** Bundle is static.
- **No SSE live updates.** Bundle data doesn't change.
- **No interval rotation.** Bundle is one consolidated file (the bundle's `events.duckdb`).
- **No retention or upload.** Bundle is read-only.
- **Single bundle at a time.** Switching bundles closes the current one and opens the new one.

### 8.2 Program.cs

```csharp
namespace Tracer.OfflineViewer;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var bundlePath = args.Length > 0 ? args[0] : null;

            var host = OfflineViewerHostBuilder.Build(bundlePath);

            // LOG_FILE convention
            var config = host.Services.GetRequiredService<OfflineViewerConfig>();
            Console.WriteLine($"LOG_FILE={config.LogFilePath}");

            // Kestrel must be listening before we open the browser
            await host.StartAsync();
            BrowserLauncher.Open($"http://localhost:{config.HttpPort}/");

            await host.WaitForShutdownAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex}");
            return 1;
        }
    }
}
```

### 8.3 OfflineViewerHostBuilder

Similar in shape to `ObserverHostBuilder` but with a `BundleOpenManager` instead of an `IntervalRotator`, and a different set of query-service registrations.

```csharp
namespace Tracer.OfflineViewer;

public static class OfflineViewerHostBuilder
{
    public static WebApplication Build(string? initialBundlePath)
    {
        var builder = WebApplication.CreateBuilder();

        // Configuration: defaults + command-line bundle path
        var config = new OfflineViewerConfig
        {
            HttpPort = FindFreePort(5400, 5499),
            LogFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tracer", "viewer-logs",
                $"tracer-viewer-{DateTime.UtcNow:yyyy-MM-dd}.json"),
            InitialBundlePath = initialBundlePath
        };
        builder.Services.AddSingleton(config);

        ConfigureSerilog(builder, config);

        // Kestrel — localhost only, no external access
        builder.WebHost.ConfigureKestrel((ctx, options) =>
        {
            options.ListenLocalhost(config.HttpPort);
            options.AddServerHeader = false;
        });

        // Bundle management
        builder.Services.AddSingleton<BundleOpenManager>();

        // Connection pool — reused from Phase 3, with rotation API used here for bundle-open switching
        builder.Services.AddSingleton<ReadOnlyConnectionPool>();

        // Query services — same classes as the Observer uses (they take a ReadOnlyConnectionPool)
        builder.Services.AddSingleton<SessionQueryService>();
        builder.Services.AddSingleton<ScenarioQueryService>();
        builder.Services.AddSingleton<TopologyQueryService>();
        builder.Services.AddSingleton<EventLookupService>();

        // ObserverStateReporter is referenced by the API surface; we provide an inert instance
        builder.Services.AddSingleton<ObserverStateReporter>(_ => new InertObserverStateReporter());

        // SseConnectionManager exists but with no LiveEventBroadcaster background; SSE endpoint
        // returns "no live events in bundle mode" on connect
        builder.Services.AddSingleton<SseConnectionManager>();

        // Hosted service: opens the initial bundle if one was provided
        builder.Services.AddHostedService<OfflineHostedService>();

        ConfigureCorsAndOpenApi(builder);

        var app = builder.Build();
        ConfigureMiddleware(app);  // same as ObserverHostBuilder, plus BundleOpenEndpoints
        return app;
    }
}
```

### 8.4 BundleOpenManager

```csharp
namespace Tracer.OfflineViewer.Lifecycle;

public sealed class BundleOpenManager : IAsyncDisposable
{
    private readonly ReadOnlyConnectionPool _pool;
    private readonly ILogger<BundleOpenManager> _logger;
    private readonly SemaphoreSlim _switchLock = new(1, 1);

    private OpenedBundle? _current;

    public BundleOpenManager(ReadOnlyConnectionPool pool, ILogger<BundleOpenManager> logger)
    {
        _pool = pool;
        _logger = logger;
    }

    public OpenedBundle? Current => _current;
    public bool IsOpen => _current is not null;

    public async Task OpenAsync(string bundlePath, CancellationToken ct)
    {
        await _switchLock.WaitAsync(ct);
        try
        {
            // 1. Resolve path to a directory (extract if zipped)
            var workingDirectory = await ResolveBundleDirectoryAsync(bundlePath, ct);

            // 2. Read and validate manifest
            var manifest = await BundleReader.ReadManifestAsync(workingDirectory, ct);
            var validation = await BundleValidator.ValidateAsync(
                workingDirectory, manifest, strict: false, ct);
            if (!validation.IsValid)
                throw new InvalidOperationException(
                    $"Bundle validation failed: {string.Join("; ", validation.Errors.Select(e => e.Message))}");

            // 3. Open events.duckdb via the connection pool
            //    Reusing the Phase 3 rotation API: from the pool's perspective, switching
            //    bundles is identical to rotating intervals.
            var eventsDb = Path.Combine(workingDirectory, "events.duckdb");
            if (_current is not null)
            {
                await _pool.OnIntervalRotatedAsync(eventsDb, ct);
                await CleanUpPreviousAsync(_current);
            }
            else
            {
                await _pool.InitializeAsync(eventsDb, ct);
            }

            _current = new OpenedBundle
            {
                Manifest = manifest,
                WorkingDirectory = workingDirectory,
                OriginalPath = bundlePath
            };
            _logger.LogInformation("Opened bundle {BundleId} from {Path}",
                manifest.BundleId, bundlePath);
        }
        finally { _switchLock.Release(); }
    }

    public async Task CloseAsync(CancellationToken ct)
    {
        await _switchLock.WaitAsync(ct);
        try
        {
            if (_current is null) return;
            await CleanUpPreviousAsync(_current);
            _current = null;
        }
        finally { _switchLock.Release(); }
    }

    private async Task<string> ResolveBundleDirectoryAsync(string bundlePath, CancellationToken ct)
    {
        if (Directory.Exists(bundlePath)) return bundlePath;
        if (!File.Exists(bundlePath))
            throw new FileNotFoundException($"Bundle not found: {bundlePath}");

        // Treat as zip; extract to temp
        var tempDir = Path.Combine(Path.GetTempPath(), $"tracer-bundle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        await BundleExtractor.ExtractAsync(bundlePath, tempDir, ct);
        return tempDir;
    }

    private Task CleanUpPreviousAsync(OpenedBundle bundle)
    {
        // Only delete the working directory if it was an extracted temp (not the original)
        if (bundle.WorkingDirectory != bundle.OriginalPath)
        {
            try { Directory.Delete(bundle.WorkingDirectory, recursive: true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to clean up temp directory"); }
        }
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _switchLock.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed record OpenedBundle
{
    public required BundleManifest Manifest { get; init; }
    public required string WorkingDirectory { get; init; }
    public required string OriginalPath { get; init; }
}
```

**Reusing the Phase 3 connection-pool rotation API**: opening a different bundle is structurally identical to interval rotation — close current connections, open new ones against a different DB path. Phase 3's `ReadOnlyConnectionPool.OnIntervalRotatedAsync` is the right entry point.

### 8.5 OfflineHostedService

```csharp
namespace Tracer.OfflineViewer.Lifecycle;

public sealed class OfflineHostedService : IHostedService
{
    private readonly BundleOpenManager _bundleManager;
    private readonly OfflineViewerConfig _config;
    private readonly ILogger<OfflineHostedService> _logger;

    public OfflineHostedService(
        BundleOpenManager bundleManager,
        OfflineViewerConfig config,
        ILogger<OfflineHostedService> logger)
    {
        _bundleManager = bundleManager;
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (_config.InitialBundlePath is { } path)
        {
            try
            {
                await _bundleManager.OpenAsync(path, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open initial bundle at {Path}", path);
                // Don't fail startup — the viewer can show the Open Bundle view instead
            }
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await _bundleManager.CloseAsync(ct);
    }
}
```

### 8.6 Bundle Open/Close Endpoints (Offline Viewer Only)

```csharp
namespace Tracer.OfflineViewer.WebApi;

public static class BundleOpenEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/bundle/open", HandleOpenAsync).WithOpenApi();
        app.MapPost("/api/bundle/close", HandleCloseAsync).WithOpenApi();
        app.MapGet("/api/bundle/current", HandleCurrentAsync).WithOpenApi();
    }

    public static async Task<Results<Ok<OpenBundleResponseDto>, ProblemHttpResult>> HandleOpenAsync(
        [FromBody] OpenBundleRequestDto request,
        [FromServices] BundleOpenManager mgr,
        CancellationToken ct)
    {
        try
        {
            await mgr.OpenAsync(request.Path, ct);
            return TypedResults.Ok(new OpenBundleResponseDto
            {
                BundleId = mgr.Current!.Manifest.BundleId
            });
        }
        catch (Exception ex)
        {
            return TypedResults.Problem(ProblemDetailsFactory.From(ex));
        }
    }

    public static async Task<NoContent> HandleCloseAsync(
        [FromServices] BundleOpenManager mgr, CancellationToken ct)
    {
        await mgr.CloseAsync(ct);
        return TypedResults.NoContent();
    }

    public static Task<Ok<CurrentBundleDto?>> HandleCurrentAsync(
        [FromServices] BundleOpenManager mgr)
    {
        var current = mgr.Current;
        if (current is null) return Task.FromResult(TypedResults.Ok<CurrentBundleDto?>(null));
        return Task.FromResult(TypedResults.Ok<CurrentBundleDto?>(new CurrentBundleDto
        {
            BundleId = current.Manifest.BundleId,
            Label = current.Manifest.SessionContext.Label,
            TimeRange = new TimeRangeDto
            {
                StartUtc = current.Manifest.TimeRange.StartUtc,
                EndUtc = current.Manifest.TimeRange.EndUtc
            }
        }));
    }
}
```

### 8.7 useBundleMode Composable (Frontend)

The Vue SPA detects whether it's running against the Observer or the Offline Viewer. Both have the same core API; the differences are subtle.

```typescript
// src/composables/useBundleMode.ts
import { ref, computed, onMounted } from 'vue';
import { useApi } from '@/api/useApi';

interface AppMode {
  kind: 'live' | 'bundle' | 'no-bundle';
  bundleId?: string;
  bundleLabel?: string;
}

const mode = ref<AppMode>({ kind: 'live' });

export function useBundleMode() {
  const detect = async () => {
    const api = useApi();
    try {
      // /api/bundle/current is only present in offline viewer mode
      const current = await api.getCurrentBundle();
      if (current) {
        mode.value = {
          kind: 'bundle',
          bundleId: current.bundleId,
          bundleLabel: current.label
        };
      } else {
        mode.value = { kind: 'no-bundle' };
      }
    } catch {
      // Endpoint not found → live observer mode
      mode.value = { kind: 'live' };
    }
  };

  onMounted(detect);

  return {
    mode: computed(() => mode.value),
    isLive:     computed(() => mode.value.kind === 'live'),
    isBundle:   computed(() => mode.value.kind === 'bundle'),
    isNoBundle: computed(() => mode.value.kind === 'no-bundle'),
    refresh: detect
  };
}
```

The frontend uses these flags to:
- Hide the live indicator in bundle mode
- Show the bundle label and time range in the app header
- Redirect to `BundleOpenView` when no bundle is loaded

### 8.8 BundleOpenView.vue

```vue
<!-- src/views/BundleOpenView.vue -->
<script setup lang="ts">
import { ref } from 'vue';
import { useApi } from '@/api/useApi';
import { useRouter } from 'vue-router';
import { useBundleMode } from '@/composables/useBundleMode';

const api = useApi();
const router = useRouter();
const { refresh } = useBundleMode();

const filePath = ref('');
const loading = ref(false);
const error = ref<string | null>(null);

async function openBundle() {
  if (!filePath.value) return;
  loading.value = true;
  error.value = null;
  try {
    await api.openBundle({ path: filePath.value });
    await refresh();
    router.push({ name: 'sessions' });
  } catch (err: any) {
    error.value = err.message ?? 'Failed to open bundle';
  } finally {
    loading.value = false;
  }
}
</script>

<template>
  <div class="bundle-open">
    <h1>Open a Tracer bundle</h1>
    <p class="bundle-open__hint">
      Paste the absolute path to a <code>.tracerbundle</code> directory
      or <code>.tracerbundle.zip</code> file.
    </p>
    <input
      v-model="filePath"
      type="text"
      placeholder="C:\bundles\training_run.tracerbundle"
      class="bundle-open__input"
      @keyup.enter="openBundle"
    />
    <button class="bundle-open__btn" :disabled="!filePath || loading" @click="openBundle">
      {{ loading ? 'Opening…' : 'Open' }}
    </button>
    <div v-if="error" class="bundle-open__error">{{ error }}</div>
  </div>
</template>

<style lang="scss">
.bundle-open {
  max-width: 600px;
  margin: 4rem auto;
  padding: 2rem;
  
  &__hint { color: var(--c-text-muted); margin-bottom: 1.5rem; }
  &__input {
    width: 100%;
    padding: 0.75rem;
    background: var(--c-bg-subtle);
    border: 1px solid var(--c-bg-subtle);
    border-radius: 6px;
    color: var(--c-text);
    font-family: var(--font-mono);
  }
  &__btn {
    margin-top: 1rem;
    padding: 0.75rem 1.5rem;
    background: var(--c-accent);
    color: white;
    border: none;
    border-radius: 6px;
    cursor: pointer;
    &:disabled { opacity: 0.5; cursor: not-allowed; }
  }
  &__error {
    margin-top: 1rem;
    padding: 0.75rem;
    background: rgba(232, 92, 92, 0.1);
    border: 1px solid var(--c-danger);
    border-radius: 6px;
    color: var(--c-danger);
  }
}
</style>
```

### 8.9 BrowserLauncher

```csharp
namespace Tracer.OfflineViewer.Browser;

public static class BrowserLauncher
{
    public static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true   // opens with default handler
            });
        }
        catch (Exception)
        {
            // Best effort; user can copy URL from log if it fails
        }
    }
}
```

---

## 9. Self-Contained Distribution

### 9.1 What's Distributed

A single folder containing everything needed to open a bundle on a fresh Windows machine:

```
TracerViewer/
  tracer-viewer.exe                     -- self-contained .NET 8 single-file executable
  duckdb.dll                            -- DuckDB native library (Windows x64)
  wwwroot/                              -- Vue SPA assets
    index.html
    assets/
      index-abc123.js
      index-abc123.css
      ...
  README.txt                            -- "Double-click tracer-viewer.exe; paste a bundle path"
```

The whole folder is portable: copy it to any Windows 10/11 machine with no .NET installed, and `tracer-viewer.exe` runs.

Total size: ~50-80 MB. Most of that is the .NET 8 runtime (self-contained) and DuckDB.

### 9.2 Build Configuration

```xml
<!-- Tracer.OfflineViewer.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <PublishTrimmed>false</PublishTrimmed>
    <!-- Trimming disabled: DuckDB.NET reflects on types at runtime and doesn't survive trimming.
         Re-evaluate when DuckDB.NET adds trim annotations. -->
    <InvariantGlobalization>true</InvariantGlobalization>
    <!-- Cuts ~30 MB by not bundling ICU; we use Invariant culture throughout. -->
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Tracer.WebApi\Tracer.WebApi.csproj" />
    <ProjectReference Include="..\Tracer.Bundle\Tracer.Bundle.csproj" />
    <ProjectReference Include="..\Tracer.Storage.DuckDB.MultiInterval\Tracer.Storage.DuckDB.MultiInterval.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- Embed the Vue SPA build output as static files -->
    <Content Include="..\..\tracer-viewer\dist\**\*.*" Link="wwwroot\%(RecursiveDir)%(Filename)%(Extension)">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
</Project>
```

### 9.3 Build Script

`build-viewer-distribution.ps1`:

```powershell
param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "dist/TracerViewer"
)

$ErrorActionPreference = "Stop"

# 1. Build the Vue SPA
Push-Location tracer-viewer
try {
    pnpm install --frozen-lockfile
    pnpm run build   # outputs to tracer-viewer/dist/
} finally { Pop-Location }

# 2. Publish the .NET project (self-contained single-file)
dotnet publish src/Tracer.OfflineViewer `
    -c $Configuration `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $OutputDir

# 3. Verify all expected files are present
$expected = @(
    "tracer-viewer.exe",
    "wwwroot/index.html"
)
foreach ($file in $expected) {
    if (-not (Test-Path "$OutputDir/$file")) {
        throw "Distribution missing required file: $file"
    }
}

# 4. Create README.txt
@"
Tracer Offline Viewer

To open a Tracer bundle:
1. Double-click tracer-viewer.exe
2. When the browser opens, paste the path to your .tracerbundle file or directory

Or from the command line:
  tracer-viewer.exe "C:\path\to\session.tracerbundle"

No installation required. This folder is portable; copy it anywhere.
"@ | Set-Content "$OutputDir/README.txt"

# 5. Zip
Compress-Archive -Path "$OutputDir/*" -DestinationPath "$OutputDir.zip" -Force

Write-Host "Distribution built: $OutputDir.zip"
```

Run: `.\build-viewer-distribution.ps1` produces `dist/TracerViewer.zip`.

### 9.4 First-Run Experience

User unzips `TracerViewer.zip`, double-clicks `tracer-viewer.exe`:

1. Console window opens briefly, prints `LOG_FILE=...` and `Now listening on http://localhost:54xx`
2. Default browser opens at `http://localhost:54xx/`
3. Browser shows `BundleOpenView`: "Open a Tracer bundle"
4. User pastes the path to their `.tracerbundle` file or directory and clicks Open
5. Browser redirects to `/sessions` showing the bundle's sessions
6. User clicks a session → Scenario View opens

**Shutting down**: closing the browser tab does not terminate the viewer process. The user must press Ctrl+C in the console window or close it. Phase 8 adds a tray icon and "Quit" affordance.

### 9.5 Drag-and-Drop Bundle Open

A small UX upgrade in the Vue frontend: dropping a bundle's path onto the `BundleOpenView` populates the input. Phase 4 doesn't include actual file-drop handling (the browser security model makes this tricky for absolute paths), but the input field is large and clearly labeled.

A future Phase 8 refinement: a small auxiliary `.bat` file in the distribution that takes a dropped file/folder and invokes `tracer-viewer.exe <path>`. The user drags the bundle onto the `.bat`, the viewer launches with the bundle pre-loaded.

---

## 10. Test Plan for Phase 4

### 10.1 Backend Unit Tests

**Bundle/BundleManifestTests.cs**
- Round-trip: serialize a `BundleManifest` to JSON, deserialize, verify equality
- Manifest with unrecognized fields: deserializes successfully, unknown fields ignored
- Manifest with missing required fields: deserialize throws clear error
- ULID round-trip in `bundleId` field

**Bundle/BundleDirectoryWriterTests.cs**
- Write a minimal bundle to a temp directory; verify file layout matches §3.1
- Manifest file contents match expected JSON (compared via JsonDocument)
- `checksums.txt` lines match `manifest.files[].sha256` entries
- Directory cleanup on writer dispose if Finalize wasn't called

**Bundle/BundleReaderTests.cs**
- Read manifest from a directory bundle
- Read manifest from a zipped bundle
- Reading from non-existent path throws `FileNotFoundException`
- Reading a directory without `manifest.json` throws clear error

**Bundle/BundleValidatorTests.cs**
- Valid bundle: returns `IsValid = true`, no errors
- Wrong file size: validation error includes path and expected/actual sizes
- Wrong checksum (strict mode): validation error includes path
- Missing file listed in manifest: validation error
- Extra file not in manifest: validation warning (Phase 4: tolerant)
- Unknown schemaVersion: validation error with version

**Aggregator/IntervalDiscoveryTests.cs**
- Find intervals overlapping a time range, no node filter: returns all matching
- With node filter: returns only specified nodes' intervals
- Time range with no overlapping intervals: returns empty
- Boundary case: interval whose start == range end is excluded (half-open interval)
- Boundary case: interval whose end == range start is excluded

**Aggregator/SessionResolverTests.cs**
- Resolve session with both start and end markers: returns correct range
- Resolve session with only start marker (still running): returns range from start to "now"
- Resolve non-existent session: returns null
- Multiple intervals contain different markers for same session: returns earliest start, latest end

**Aggregator/EventsConsolidatorTests.cs**
- Single source: output has same row count
- Multiple sources: output has sum of row counts
- Time-range filter: only rows within range copied
- Output has correct schema (events table with all Phase 1 columns)
- Output has indexes built
- Output is checkpointed (no WAL file present)

**Aggregator/FastStateCopierTests.cs**
- Scope None: no fast_state directory created
- Scope SelectedEntities: only specified entities copied
- Scope All: all entities in source intervals copied
- Multi-source same entity: rows merged into one samples.parquet
- Topic name with colons: directory name is safe

**Aggregator/TopologyExtractorTests.cs**
- Extracts unique nodes from source intervals
- First/last seen times reflect earliest/latest publish_wallclock per node
- Empty input: returns empty topology

**MultiInterval/MultiIntervalReaderTests.cs**
- Create with zero files: query returns empty
- Create with one file: query unions a single source
- Create with N files: UNION ALL across all
- Source-alias column present in results
- 100+ attachments: query succeeds within reasonable time
- Dispose closes all attached connections

**MultiInterval/AttachedDatabaseManagerTests.cs**
- Attach the same path twice: second attach throws
- Detach: alias is removed from `Attachments`
- Dispose: all attached databases detached
- Alias generation: never collides for the same hint
- Alias generation: produces valid SQL identifiers for arbitrary input

**WebApi/BundleEndpointTests.cs**
- `POST /api/bundles/build` with valid request: returns 202 Accepted with bundleId
- `POST /api/bundles/build` with invalid request (no sessionId or timeRange): returns 400
- `GET /api/bundles/{id}/status` for unknown id: returns 200 with state=Unknown
- `GET /api/bundles/{id}/status` for in-progress build: returns 200 with state=InProgress
- `GET /api/bundles/{id}/download` for completed build: returns 200 with application/zip
- `GET /api/bundles/{id}/download` for unknown id: returns 404
- `DELETE /api/bundles/{id}` removes the bundle from disk; subsequent GET returns 404

### 10.2 Backend Integration Tests

**AggregatorEndToEndTests.cs**
- Populate a mock NAS with two nodes × three intervals of fake data
- Run `AggregationOrchestrator.RunAsync` with a time range covering all
- Verify: bundle directory exists at output path
- Verify: bundle validates with strict=true
- Verify: bundle's events.duckdb has expected row count (sum of source rows in range)
- Verify: progress reporter received events in order Started → Completed

**AggregatorEndToEndTests.cs (session-id variant)**
- Populate mock NAS with events including session-start/session-end for a known sessionId
- Run aggregator with `--session-id` instead of `--time-range`
- Verify: bundle's time range matches the session's start/end events

**BundleRoundTripTests.cs**
- Run FakeNode+Observer fixture for a fixed scenario
- Capture a known query's results from the live Observer (e.g., session list, top 50 notables)
- Build a bundle for the session via `POST /api/bundles/build`
- Wait for completion via polling `/api/bundles/{id}/status`
- Launch an offline viewer instance against the bundle
- Run the same queries through the offline viewer
- **Assert: results are bitwise identical** (modulo client-side timestamps in DTOs)

**ObserverBundleBuildTests.cs**
- Start observer + FakeNode
- After 1 simulated minute, POST a bundle build for the active session
- Verify: 202 response with bundleId
- Verify: GET /status transitions Queued → InProgress → Completed
- Verify: GET /download returns a valid zip
- Verify: downloaded zip extracts to a valid bundle

**OfflineViewerSmokeTests.cs**
- Build a small bundle via fixture
- Spawn `tracer-viewer.exe` process with bundle path as arg
- HTTP-poll `localhost:<port>/api/bundle/current` until current bundle reflects expected ID
- Issue `GET /api/sessions` against the viewer; verify response
- Send SIGINT (Ctrl+C); verify process exits cleanly

### 10.3 Frontend Unit Tests (Vitest)

```typescript
// tests/unit/useBundleMode.spec.ts
describe('useBundleMode', () => {
  it('reports live mode when /api/bundle/current is not found', async () => {
    mockApi.getCurrentBundle.mockRejectedValue(new Error('404'));
    const { mode } = useBundleMode();
    await flushPromises();
    expect(mode.value.kind).toBe('live');
  });

  it('reports bundle mode when current bundle is present', async () => {
    mockApi.getCurrentBundle.mockResolvedValue({
      bundleId: 'abc',
      label: 'Test bundle',
      timeRange: { startUtc: '2026-01-01T00:00:00Z', endUtc: '2026-01-01T01:00:00Z' }
    });
    const { mode } = useBundleMode();
    await flushPromises();
    expect(mode.value.kind).toBe('bundle');
    expect(mode.value.bundleId).toBe('abc');
  });

  it('reports no-bundle mode when current bundle is null', async () => {
    mockApi.getCurrentBundle.mockResolvedValue(null);
    const { mode } = useBundleMode();
    await flushPromises();
    expect(mode.value.kind).toBe('no-bundle');
  });
});
```

Similar shape:
- `BundleOpenView.spec.ts`: input is required, error displays on failure, success navigates to /sessions

### 10.4 Performance Tests

- Aggregate a synthetic dataset of 1M events across 5 nodes × 4 intervals: completes in < 60 seconds
- Aggregate a synthetic dataset of 10M events: completes in < 5 minutes
- Open a 1 GB bundle in offline viewer: ready for queries in < 3 seconds
- Round-trip query (Phase 3 endpoint set) against a 1 GB bundle: median latency under 200 ms

---

## 11. Phase 4 Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| DuckDB ATTACH performance degrades at 100+ attachments | Medium | High | Test early with 200 attached DBs. If problematic, batch: consolidate in chunks of 20-30, then merge the chunks. |
| `INSERT INTO ... SELECT FROM attached.events` performance is unexpectedly slow | Low | High | Spike on day 1 with realistic data. Fallback: use `COPY ... TO` Parquet intermediate. |
| Self-contained .NET single-file exe is rejected by Windows SmartScreen | Medium | Medium | Document the warning in README. Code signing is out of scope for Phase 4; document for ops/release engineering. |
| `PublishSingleFile` + DuckDB native library issues | Medium | High | DuckDB needs `duckdb.dll` extracted. Test the published exe on a clean Windows machine. If `IncludeNativeLibrariesForSelfExtract` doesn't handle it, ship `duckdb.dll` alongside the exe. |
| Browser security blocks bundle file path detection | High | Low | Already accepted: user pastes path manually. Phase 8 may add helper .bat wrapper. |
| Bundle round-trip yields different results than live (off-by-one timestamps, etc.) | Medium | High | The round-trip test is the canary. Investigate every divergence. Most likely cause: server-time fields in DTOs that should be data-time fields. |
| FastStateCopier append-via-rewrite is too slow at large entity counts | Medium | Medium | Phase 4 acceptable performance is "few minutes". If it exceeds 10 min for typical workloads, defer to Phase 7 where fast-state queries become primary. |
| Mock NAS has no `_ready` sentinel on intervals that the agent wrote but didn't finalize | Low | Medium | Reuse Phase 2 recovery logic. The aggregator skips intervals without `_ready`. |
| Trimming required for binary size, breaks DuckDB.NET | High | Low | Already accepted: trimming disabled. Size is 50-80 MB which is acceptable. |
| `Process.Start` for browser launch fails silently on unusual configurations | Low | Low | Log the URL clearly in console output so user can copy it manually. |

---

## 12. Definition of Done for Phase 4

### Build & Run

- [ ] `Tracer.Bundle`, `Tracer.Storage.DuckDB.MultiInterval`, `Tracer.Aggregator`, `Tracer.Aggregator.Cli`, `Tracer.OfflineViewer` all build with `TreatWarningsAsErrors=true`
- [ ] `tracer-aggregate.exe` runs and executes `build`, `validate`, `inspect` commands
- [ ] `tracer-viewer.exe` runs as a self-contained single-file exe on a clean Windows machine (no .NET installed)
- [ ] `build-viewer-distribution.ps1` produces a portable `TracerViewer/` folder and `TracerViewer.zip`
- [ ] LOG_FILE= convention followed by both CLI and viewer

### Bundle format

- [ ] A bundle built by the aggregator validates with `tracer-aggregate validate --strict`
- [ ] `checksums.txt` is `sha256sum -c` compatible (verified externally)
- [ ] Schema version 1 is documented and enforced; bundles with unknown schemaVersion are rejected
- [ ] `bundleId` is a valid ULID; renaming a bundle file does not change its ID
- [ ] All names in `fast_state/{topic}/{entity}/` use the safe-naming scheme (no colons, slashes, etc.)

### Aggregator

- [ ] `--session-id` lookup correctly finds the session's time range from interval manifests
- [ ] `--time-range` works with explicit ISO 8601 strings
- [ ] `--nodes` filter restricts source intervals appropriately
- [ ] `--fast-state none` produces a bundle with no `fast_state/` directory
- [ ] `--fast-state selected --fast-state-entities <list>` produces only the listed entities
- [ ] `--fast-state all` includes all entities in scope
- [ ] Multiple source intervals for the same entity merge correctly into one `samples.parquet`
- [ ] Output path ending in `.zip` produces a zipped bundle; otherwise a directory

### Cross-interval reading

- [ ] `MultiIntervalReader` constructs valid UNION ALL queries
- [ ] `AttachedDatabaseManager` cleans up all attachments on dispose
- [ ] Aggregator successfully attaches 100+ source files in a single connection

### Web API

- [ ] `POST /api/bundles/build` returns 202 with bundleId
- [ ] `GET /api/bundles/{id}/status` transitions Queued → InProgress → Completed
- [ ] `GET /api/bundles/{id}/download` streams a valid zip
- [ ] `DELETE /api/bundles/{id}` removes from disk
- [ ] Only one bundle build runs concurrently (verified by tests)
- [ ] OpenAPI document includes all new endpoints; TypeScript client regenerates cleanly

### Offline viewer

- [ ] `tracer-viewer.exe <bundle-path>` opens the bundle and serves it
- [ ] `tracer-viewer.exe` without args opens the `BundleOpenView`
- [ ] Switching bundles (via `POST /api/bundle/open`) correctly closes old, opens new
- [ ] Bundle extracted to temp from a zip is cleaned up on close
- [ ] All Phase 3 user-facing views (Session Browser, Scenario View) work against a bundle
- [ ] Live indicator is hidden in bundle mode; bundle label shown in header

### Round-trip

- [ ] Queries against a bundle return identical results to the same queries against the live observer that produced it (verified by `BundleRoundTripTests`)

### Testing

- [ ] All Phase 1, 2, 3 tests still pass
- [ ] Phase 4 backend unit tests pass (target: 40+ tests)
- [ ] Phase 4 backend integration tests pass (target: 4+ scenarios)
- [ ] Phase 4 frontend unit tests pass
- [ ] At least one E2E test passes locally: build bundle → open in standalone viewer → verify

### Performance

- [ ] 1M-event aggregation completes in < 60 seconds
- [ ] 1 GB bundle opens in < 3 seconds
- [ ] Bundle round-trip queries: median latency < 200 ms

### Documentation

- [ ] README explains the field-support workflow: capture → aggregate → ship → open
- [ ] CLI help text is complete (`tracer-aggregate --help` and per-command help)
- [ ] Bundle format documented in `docs/bundle-format.md` (extracted from this design)

---

## 13. Handoff to Phase 5

What Phase 5 inherits from Phase 4:

- **`MultiIntervalReader` and `AttachedDatabaseManager`** — Phase 5's timeline view needs to query across the active interval *plus* recently-completed intervals to render a continuous timeline. This is exactly the multi-interval pattern Phase 4 introduces.
- **`Tracer.Aggregator`** — Phase 5's "build bundle from this session" UI affordance calls into the existing aggregator.
- **The offline viewer's connection-pool reuse** — Phase 5's enhanced timeline queries work the same against a live observer or a bundle. Tests should cover both paths.
- **Bundle format** — Phase 5's saved-view feature (Phase 8) and bookmarks live in `bundles/{id}/annotations/`.

What Phase 5 must address that Phase 4 deferred:

- **Live multi-interval queries** — the Observer's `ReadOnlyConnectionPool` is single-interval (Phase 3 simplification). Phase 5 extends it to span the active interval plus the most recent N completed intervals.
- **Bundle library UI** — Phase 4 has `GET /api/bundles` but the Vue SPA doesn't visualize it. Phase 5 adds a Bundles tab on the Session Browser view.
- **Engineer timeline view** — the main Phase 5 deliverable, drawing the canvas-based multi-node timeline (architecture §16.2). Built atop the multi-interval reader pattern Phase 4 establishes.

What's now demonstrable end-to-end after Phase 4:

The complete field-support workflow:

1. **Customer** runs the simulation; FakeNodes / Agents capture per-node data and upload intervals to NAS (Phase 2).
2. **Customer's IT** runs `tracer-aggregate build --session-id <sid> --output <bundle.zip>` on a machine with NAS access.
3. **Customer** ships `bundle.zip` to support (email, file transfer, whatever).
4. **Support engineer** drops `TracerViewer.zip` and `bundle.zip` on a laptop with no special setup.
5. **Support engineer** unzips both, runs `tracer-viewer.exe <bundle path>`, and a browser opens to the scenario view.
6. **Support engineer** can see the scenario phases, notable events, participating nodes, and session timeline — without needing access to the customer's cluster, NAS, or any infrastructure.

This is the workflow Tracer was built for. Phase 4 is when it becomes real.
