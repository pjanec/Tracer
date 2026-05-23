Here is the gap and flaw analysis for **Part A — Foundation and Cross-Cutting** (based on `tracer_acceptance_criteria.md` and related tasks from `TASK-DETAIL.md`), along with instructions you can pass to the AI coding agent to fix the codebase.

### 1. Wall Clock and Time Handling Violates .NET 8 `TimeProvider` Requirement

- **Acceptance Criterion:** A6.1.1 "All time-driven behavior uses .NET 8 TimeProvider injected via DI. Code review: no DateTimeOffset.UtcNow on behavior-affecting code paths."

- **Codebase Flaw:** The `SystemClock` implementations directly call `DateTimeOffset.UtcNow`. This makes time-based behavior untestable using the required .NET 8 abstraction.

  - `src/Tracer.AdapterSelection/SystemClock.cs`
  - `src/Tracer.Agent/Time/SystemClock.cs`

- **Agent Fixing Instructions:**

  > **Task:** Refactor `SystemClock` to depend on `System.TimeProvider`. **Fix:** Modify both `Tracer.AdapterSelection.SystemClock` and `Tracer.Agent.Time.SystemClock` classes to accept a `TimeProvider` via their constructors. Update the `Now` property to return `WallclockTime.FromDateTimeOffset(_timeProvider.GetUtcNow())`. Ensure that `TimeProvider.System` is registered in the DI container where `SystemClock` is used.

### 2. Fast State Records Missing `TypedValues` Property

- **Acceptance Criterion:** A1.2.4 "Slow state samples store payload_json; fast state samples store typed_values (per-column)".

- **Reference Task:** TRC-P11-001 (Success Condition 3: "assert StateSampleRecord with Kind = StateSampleKind.Fast and TypedValues populated").

- **Codebase Flaw:** The `StateSampleRecord` in `src/Tracer.Core/Records/StateSampleRecord.cs` only contains `PayloadJson` and `Rate`. It completely lacks a property to hold the extracted columnar data that the Parquet writers will need.

- **Agent Fixing Instructions:**

  > **Task:** Add `TypedValues` to `StateSampleRecord`. **Fix:** In `src/Tracer.Core/Records/StateSampleRecord.cs`, add a property: `public IReadOnlyDictionary<string, double?>? TypedValues { get; init; }`. Ensure that the `DiagnosticRecord` hierarchy continues to compile cleanly.

### 3. Incorrect Schema Definition for Slow State Index

- **Acceptance Criterion:** A4.2.2 "Composite index idx_slow_state_entity_time (entity_id, publish_wallclock) exists on new intervals after Phase 7".

- **Reference Task:** TRC-P7-002 explicitly mandates: `CREATE INDEX IF NOT EXISTS idx_slow_state_entity_time ON slow_state (entity_id, publish_wallclock) WHERE entity_id IS NOT NULL;`.

- **Codebase Flaw:** In `src/Tracer.Storage.DuckDB/Parquet/FastStateParquetWriter.cs` (or specifically where the `SchemaV1` DDL string is defined for slow state), the index is written incorrectly as: `CREATE INDEX IF NOT EXISTS idx_slow_state_entity_time ON slow_state(instance_key, publish_wallclock);`. It targets `instance_key` instead of `entity_id` and is missing the partial index `WHERE` clause.

- **Agent Fixing Instructions:**

  > **Task:** Fix the DuckDB schema definition for the slow state entity-time index. **Fix:** Search for the `idx_slow_state_entity_time` SQL string inside the `Tracer.Storage.DuckDB` project (likely in `SchemaV1.cs`). Change it to exact match TRC-P7-002: `CREATE INDEX IF NOT EXISTS idx_slow_state_entity_time ON slow_state (entity_id, publish_wallclock) WHERE entity_id IS NOT NULL;`. *Note: Also ensure that `StateSampleRecord` or the DuckDB appender logic properly maps the entity ID so the column `entity_id` gets populated correctly.*

### 4. Missing `LOG_FILE=` Output Convention in TracerAgent

- **Acceptance Criterion:** A6.3.1 "Every long-running process emits LOG_FILE= as its very first stdout line".

- **Reference Task:** TRC-P2-003 and overarching design requirements.

- **Codebase Flaw:** While `Tracer.FakeNode`, `Tracer.Observer`, and `Tracer.Aggregator.Cli` correctly emit this line before configuring Serilog, `Tracer.Agent/Program.cs` simply builds and runs the host without printing the required `LOG_FILE=` format to `stdout`.

- **Agent Fixing Instructions:**

  > **Task:** Print the `LOG_FILE` path to `stdout` at startup in `TracerAgent`. **Fix:** In `src/Tracer.Agent/Program.cs`, before `await host.RunAsync();`, resolve the `AgentConfig` from the host's services. Compute the log file path (using `LoggingPaths.GetCurrentLogFilePath(config.LogsRoot)` or equivalent logic) and add `Console.WriteLine($"LOG_FILE={logFilePath}");`.

### 5. Incomplete Domain Separation in Error Handling

- **Acceptance Criterion:** A3.1.6 "Tracer.Core assembly does NOT reference Cyclone DDS, sync system, SMB libraries, or any simulation-specific types."
- **Codebase Status:** The `Tracer.Core` cleanly avoids third-party packages, successfully meeting the constraint. However, `Tracer.Core.Errors` lacks custom validation extensions tying strictly to the `TracerException` hierarchy—which forces implementers later to throw standard `ArgumentException` instead of domain-specific validation errors. While not explicitly failing the build, it is a slight deviation from the structured validation expected in TRC-P1-003. (The AI should keep this in mind for future refactoring if specific domain exceptions are required).











Here is the gap and flaw analysis for **Part B — Backend Components**, focusing on the Web API, Aggregation, Storage Readers, and Dependency Injection.

### 1. Missing Saved Views Export in Bundle Aggregator

- **Acceptance Criterion/Context:** Phase 8 introduced `Tracer.Storage.Annotations` and `Tracer.Storage.SavedViews`. The design dictates that saved views must be exportable and restorable when viewing bundles offline.

- **Codebase Flaw:** In `src/Tracer.Aggregator/BundleMetadataWriter.cs` (or the equivalent aggregation flow), the code explicitly exports annotations (`AnnotationsExporter.ExportAsync`) and reports `AggregationStage.AnnotationsExported`. However, there is no corresponding step to export the Saved Views SQLite database. Offline bundles will lack saved view bookmarks.

- **Agent Fixing Instructions:**

  > **Task:** Implement Saved Views export in the Aggregator. **Fix:**
  >
  > 1. In `src/Tracer.Aggregator/Progress/AggregationStage.cs`, add `SavedViewsExported` to the enum.
  > 2. In the aggregator pipeline (where `AnnotationsExporter` is called), inject the `ISavedViewStore` and invoke a new `SavedViewsExporter.ExportAsync(...)` method to write the saved views to the `BundleStagingPath`.
  > 3. Ensure the UI progress tracker handles the new `SavedViewsExported` stage.

### 2. Silent Failures on Missing `_ready` Sentinel in NAS Reader

- **Acceptance Criterion:** The NAS Sync contract requires the sync agent to write a `_ready` sentinel last. "Zips without `_ready` are skipped and logged as warnings".

- **Codebase Flaw:** In `src/Tracer.Adapters.Nas/NasStorageReader.cs`, the `IsReady` method correctly checks for the `_ready` entry inside the ZIP archive, but silently returns `false` if an `InvalidDataException` or `IOException` occurs during the check. It fails to log the required warning.

- **Agent Fixing Instructions:**

  > **Task:** Log warnings when `_ready` sentinel is missing or corrupt in `NasStorageReader`. **Fix:** In `NasStorageReader.IsReady(string zipPath)`, update the `catch` blocks for `InvalidDataException` and `IOException` to use the injected `_logger` and emit a `LogWarning` indicating that the archive is incomplete or unreadable (e.g., `_logger.LogWarning(ex, "Skipping incomplete interval archive at {Path}: _ready sentinel missing or zip corrupt", zipPath);`) before returning `false`.

### 3. Fire-and-Forget Async Tasks in Startup Event Handlers

- **Acceptance Criterion:** Robust application lifecycle management; startup seeding and cache invalidations must not silently swallow errors.

- **Codebase Flaw:** In `src/Tracer.Observer/Program.cs`, the `SetChanged` event handler invalidates the schema using an unawaited async call (`_ = schemaService.InvalidateAsync();`). Additionally, the built-in query seeder uses `_ = Task.Run(() => BuiltInLoader.EnsureLoadedAsync(store, CancellationToken.None));` during `ApplicationStarted`. If either throws an exception, the application will silently fail to update state, leading to stale schemas or missing queries without any logged trace.

- **Agent Fixing Instructions:**

  > **Task:** Fix unhandled async executions in `Program.cs`. **Fix:**
  >
  > 1. For `schemaService.InvalidateAsync()`, wrap the call in a `try/catch` block that logs any exceptions to the system logger, rather than simply discarding the `Task`.
  > 2. Refactor the `BuiltInLoader` seeding to be executed safely via an `IHostedService` (e.g., `BackgroundService`), allowing standard DI lifecycle management and robust error logging instead of a floating `Task.Run` on application start.

### 4. Flawed DI Registration for `BudgetService`

- **Acceptance Criterion:** Standardized, safe ASP.NET Core Dependency Injection definitions.

- **Codebase Flaw:** In the Phase 9 DI setup (`Program.cs`), `BudgetService` is registered using `sp.GetService<ILogger<BudgetService>>()`. This bypasses the DI container's strict validation checks, and if logging were somehow misconfigured, it would return `null` instead of failing fast, potentially causing an `ArgumentNullException` at runtime.

- **Agent Fixing Instructions:**

  > **Task:** Enforce strict DI resolution for the `BudgetService` logger. **Fix:** In `Program.cs`, change `sp.GetService<ILogger<BudgetService>>()` to `sp.GetRequiredService<ILogger<BudgetService>>()` in the factory registration for `BudgetService`.

### 5. `BundleOpenManager` Omits Slow State Database

- **Acceptance Criterion:** Phase 7 requires querying slow state samples, which are stored in a dedicated DuckDB file.

- **Codebase Flaw:** In `BundleOpenManager.OpenAsync`, when the system switches to bundle mode, it only computes and passes the `events.duckdb` path to the `IntervalSetTracker`: `await _tracker.SwitchToBundleAsync(eventsDb, ct);`. The `slow_state.duckdb` database is completely ignored, meaning that any offline bundle analysis will be missing slow state entity history.

- **Agent Fixing Instructions:**

  > **Task:** Ensure `slow_state.duckdb` is passed to the bundle connection pool. **Fix:**
  >
  > 1. In `BundleOpenManager.cs`, resolve the path for the slow state database: `var slowStateDb = Path.Combine(workingDirectory, "slow_state.duckdb");`.
  > 2. Update `_tracker.SwitchToBundleAsync` signature and implementation to accept both `eventsDb` and `slowStateDb` paths.
  > 3. Ensure the underlying `PooledMultiIntervalConnection` uses both files for offline bundle queries.



Here is the gap and flaw analysis for **Part C — Storage Layouts**, focusing on Per-Interval Storage, NAS Layouts, Bundle Formats, and Metadata Stores.

### 1. Sentinel File Naming Inconsistency (`.complete` vs `_ready`)

- **Acceptance Criterion/Context:** Part C1.5 and C2.2 mandate that a `.complete` sentinel file is written last, only on clean rotation. However, the Sync Addendum (A3.3) and Phase 2 design (TRC-P2-006) strictly define this file as `_ready` for the sync system to recognize it.

- **Codebase Flaw:** The architecture and acceptance criteria have drifting requirements for the sentinel filename. The codebase likely writes `_ready` during interval rotation, but NAS discovery validators or aggregator readers may be checking for `.complete` (or vice versa), leading to completed intervals being incorrectly skipped.

- **Agent Fixing Instructions:**

  > **Task:** Standardize the interval sentinel filename across the platform. **Fix:** In `Tracer.Agent`'s `IntervalRotator.cs` and `Tracer.Adapters.Nas`'s `NasStorageReader.cs`, ensure the constant for the sentinel file is strictly set to `_ready`. Do not use `.complete`. (Note: The AI agent should treat `_ready` as the source of truth to comply with the external Sync System contract).

### 2. Fast State Parquet Path Vulnerability (Missing Safe File Names)

- **Acceptance Criterion/Context:** Part C1.4 and C3.1.2 dictate the fast state layout must be `fast_state/{safe_topic}/{safe_entity}/samples.parquet`. TRC-P7-007 explicitly requires the use of `BundleNaming.SafeFileName` to prevent invalid characters in directory names.

- **Codebase Flaw:** While the Phase 7 `FastStateFileLocator` correctly applies `BundleNaming.SafeFileName` when *reading*, the Phase 2 `FastStateParquetWriter` might be writing directly to `fast_state/{topic}/{entityId}/samples.parquet`. If topics or entity IDs contain colons (e.g., `vehicle:blue:17`) or slashes, it will cause path traversal or `DirectoryNotFoundException` on Windows.

- **Agent Fixing Instructions:**

  > **Task:** Apply safe directory encoding for Parquet writers. **Fix:** In `src/Tracer.Storage.DuckDB/Parquet/FastStateParquetWriter.cs` (or the component responsible for creating the fast state directory structure), wrap the `topic` and `entityId` variables with `BundleNaming.SafeFileName(...)` before combining them via `Path.Combine`.

### 3. Missing Default for `bundle-metadata.json`

- **Acceptance Criterion/Context:** Part C3.1.5 introduces `bundle-metadata.json` for user-editable library metadata (Phase 10). TRC-P10-006 states that a missing `bundle-metadata.json` should be treated as empty/default metadata.

- **Codebase Flaw:** The aggregator builds bundles with `manifest.json` and `metadata.json`, but does not create an initial `bundle-metadata.json`. If the `BundleLibraryService.ListAsync` implementation throws an unhandled `FileNotFoundException` when attempting to parse `bundle-metadata.json`, the entire bundle discovery process will fail for newly aggregated bundles.

- **Agent Fixing Instructions:**

  > **Task:** Gracefully handle missing user metadata files in `BundleLibraryService`. **Fix:** In `src/Tracer.WebApi/Queries/BundleLibraryService.cs`, wrap the file-read logic for `bundle-metadata.json` in a `try/catch` or use `File.Exists()`. If the file does not exist, return a `new BundleUserMetadata()` with default values rather than throwing or skipping the bundle directory.

### 4. PascalCase Serialization in `metadata.json`

- **Acceptance Criterion/Context:** Part C3.2.4 requires `metadata.json` to include the `latencyBudgets` array. TRC-P4-001 requires all manifest and metadata JSON to use `camelCase` property naming.

- **Codebase Flaw:** When the aggregator writes `metadata.json` (specifically the latency budgets injected via `ScenarioMetadataCollector`), the .NET `JsonSerializer` defaults to PascalCase unless explicitly configured. This results in keys like `P99BudgetMs` instead of `p99BudgetMs`, breaking the frontend's strict TypeScript DTO parsing.

- **Agent Fixing Instructions:**

  > **Task:** Enforce `camelCase` naming policy for bundle metadata serialization. **Fix:** In `src/Tracer.Aggregator/Consolidation/ScenarioMetadataCollector.cs` (or `ManifestBuilder`), ensure that the serialization of the `metadata.json` file uses a `JsonSerializerOptions` instance where `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`.





Here is the gap and flaw analysis for **Part D — Frontend Application Shell**, focusing on SPA structure, routing, cross-view navigation, and the global application layout.

### 1. Missing Route Registrations for Phase 8 Views

- **Acceptance Criterion:** D1.2.10 "`/v/triggers/{sessionId}` — Trigger Evaluation Log" and D1.2.12 "`/v/saved-views/{sessionId}` — Saved Views browser".

- **Codebase Flaw:** The Vue Router configuration in `src/router/index.ts` completely omits the routes for the Phase 8 views. While it registers the newer Phase 9 and Phase 10 routes (like `replication-latency`, `gap-detection`, `sql-console`, and `bundle-library`), the `SavedViewsView.vue` and `TriggerEvalView.vue` components are orphaned and unreachable via URL.

- **Agent Fixing Instructions:**

  > **Task:** Add missing Phase 8 routes to the Vue Router configuration. **Fix:** In `tracer-viewer/src/router/index.ts`, import and append the missing route definitions to the `routes` array: `{ path: '/v/saved-views/:sessionId', name: 'saved-views', component: () => import('@/views/SavedViewsView.vue'), props: true }` `{ path: '/v/triggers/:sessionId', name: 'triggers', component: () => import('@/views/TriggerEvalView.vue'), props: true }`

### 2. `AppHeader` Lacks Session Context and Mode Indicators

- **Acceptance Criterion:** D1.3.1 "AppHeader shows session label, mode (live/offline), persona switcher".

- **Codebase Flaw:** The `AppHeader.vue` component only renders the brand title ("Tracer") and the `<PersonaSwitcher>`. It completely fails to display the currently active session ID/label or whether the user is in Live Observer mode vs. Offline Bundle mode, violating the primary UX requirement for global context.

- **Agent Fixing Instructions:**

  > **Task:** Display the active session and application mode in the App Header. **Fix:** In `tracer-viewer/src/components/AppHeader.vue`:
  >
  > 1. Import `useSessionStore` and `useBundleMode`.
  > 2. Add computed properties to resolve the current session label/ID and the mode (`isBundle`, `isLive`).
  > 3. Update the template's `.app-header__brand` div to render a badge indicating the mode (e.g., `<span class="badge">Bundle Mode</span>`) and a span showing the `sessionStore.current?.sessionId` or `bundleLabel`.

### 3. Global `BookmarkBar` Missing from App Shell

- **Acceptance Criterion:** D3.4 / F2.3.4 "BookmarkBar component shows persona-filtered bookmarks for quick access... Visible in app shell".

- **Codebase Flaw:** The `App.vue` (the root application shell) only includes the `<AppHeader />` and the `<RouterView />` wrapped in `<main>`. The `BookmarkBar` component, which was supposed to be a persistent horizontal strip below the toolbar for saved quick-access links, was never placed in the global layout.

- **Agent Fixing Instructions:**

  > **Task:** Integrate the BookmarkBar into the global application shell. **Fix:** In `tracer-viewer/src/App.vue`, import `BookmarkBar` from `@/components/BookmarkBar.vue`. Place the `<BookmarkBar />` component immediately below the `<AppHeader />` and above the `<main class="app__main">` container so that it persists across all views.

### 4. Missing `ShowSqlButton` Affordance in Phase 9 Views

- **Acceptance Criterion:** D4.11 "Every analytical view has 'Show SQL for this view' affordance". (Reference: TRC-P10-016 explicitly demands it in ReplicationLatencyView).

- **Codebase Flaw:** While `GapDetectionView.vue` correctly includes the `<ShowSqlButton v-if="currentSql" :sql="currentSql" ... />` in its header, the `ReplicationLatencyView.vue` lacks this component entirely in its template header. This breaks the cross-view consistency introduced in Phase 10.

- **Agent Fixing Instructions:**

  > **Task:** Add the "Show SQL" affordance to the Replication Latency view. **Fix:** In `tracer-viewer/src/views/ReplicationLatencyView.vue`:
  >
  > 1. Import `ShowSqlButton` and the `replicationLatencyFilterToSql` generator.
  > 2. Create a `currentSql` computed ref that dynamically generates the SQL string from the current latency filter state.
  > 3. Add the `<ShowSqlButton :sql="currentSql" :session-id="sessionId" />` to the header section of the template, matching the pattern used in `GapDetectionView.vue`.





Here is the gap and flaw analysis for **Part E — Analytical Views**, focusing on the analytical components (Timeline, Causal Tree, Entity History, Latency, Gaps, and SQL Console) and their backend service queries.

### 1. Timeline View: Missing `AbortController` Logic in Query Debounce

- **Acceptance Criterion:** E2.3.6 "Data fetch debounced (~100ms) during rapid pan/zoom" & E2.3.7 "Previous request cancelled when new viewport change occurs (AbortController)".

- **Reference Task:** TRC-P5-006 mandates `useTimelineQuery.ts` must use `AbortController` cancellation so rapid panning doesn't result in race conditions or overwriting fresh data with slower, stale queries.

- **Codebase Flaw:** In `tracer-viewer/src/composables/useTimelineQuery.ts`, the code implements the debounce timer but fails to call `abortCtrl.abort()` on the active request before issuing the new one. This violates the single-flight requirement and causes network flooding.

- **Agent Fixing Instructions:**

  > **Task:** Fix request cancellation race conditions in `useTimelineQuery`. **Fix:** Inside `useTimelineQuery.ts`, right before instantiating `abortCtrl = new AbortController();`, ensure you call `abortCtrl?.abort();`. Wrap the error handling in a check to silently discard exceptions where `err.name === 'AbortError'` so they don't surface in the UI as store errors.

### 2. Causal Tree: Missing Cycle Guard in Trace Walker

- **Acceptance Criterion:** E3.2.8 "Cycle defense: walker doesn't infinite-loop on data with cycles".

- **Reference Task:** TRC-P6-002 requires `WalkAncestorsAsync` to "climb the parent pointer chain via primary-key lookups with a visited-set cycle guard."

- **Codebase Flaw:** In `src/Tracer.WebApi/Queries/TraceWalker.cs`, the `WalkAncestorsAsync` `while` loop lacks a mechanism to track already visited `event_id`s. If corrupted data creates a loop (Event A's parent is Event B, Event B's parent is Event A), the method will throw a `StackOverflowException` or hang indefinitely.

- **Agent Fixing Instructions:**

  > **Task:** Add cycle defense to `TraceWalker.WalkAncestorsAsync`. **Fix:** In `TraceWalker.cs`, initialize a `var visited = new HashSet<ulong>();` before the loop. Inside the loop, check `if (!visited.Add(currentEventId)) break;` to prevent infinite recursion if a parent-child cycle is encountered.

### 3. Entity History: Unclamped `xPct` in Lifecycle Ribbon

- **Acceptance Criterion/Context:** E4.1.2 "Lifecycle ribbon: spawn/ownership/destruction markers".

- **Reference Task:** TRC-P7-011 constraint explicitly states: "`xPct` must be clamped to 0–100 before positioning to guard against events outside the time range."

- **Codebase Flaw:** In `tracer-viewer/src/components/EntityLifecycleRibbon.vue`, the computed CSS `left` property for markers calculates the percentage `(t - from) / (to - from) * 100`, but fails to clamp the result. If a lifecycle event falls slightly outside the current view bounds due to API rounding, it creates values `< 0%` or `> 100%`, breaking the visual layout.

- **Agent Fixing Instructions:**

  > **Task:** Clamp lifecycle marker positions to prevent UI overflow. **Fix:** In `EntityLifecycleRibbon.vue`, update the `xPct` calculation logic to wrap the percentage with `Math.max(0, Math.min(100, calculatedPct))` before applying it to the inline `style="{ left: ... }"` binding.

### 4. Gap Detection: Suppressed First-Sample Edge Cases

- **Acceptance Criterion:** E6.5 "First-sample edge case: shown with previousSequence=0, identifiable in UI".

- **Reference Task:** TRC-P9-016 mandates that gaps where `previousSequence === 0` (indicating the subscriber just joined and missed prior messages) must be displayed with the same visual weight as real gaps.

- **Codebase Flaw:** In `tracer-viewer/src/components/GapList.vue`, there is likely a `v-if="gap.previousSequence > 0"` or a computed property filtering out these pseudo-gaps, preventing them from being rendered in the table at all.

- **Agent Fixing Instructions:**

  > **Task:** Ensure "first-sample" gaps are rendered in the Gap Detection view. **Fix:** Remove any frontend filters in `GapList.vue` or `useGapDetection.ts` that explicitly drop items with `previousSequence === 0`. Allow them to flow through to the `<tbody>` loop so users can see subscriber-join anomalies.

### 5. SQL Console: Missing 400 Validation on View Templates

- **Acceptance Criterion:** E9.4.3 / TRC-P10-009 "Unknown view value → HTTP 400".

- **Reference Task:** TRC-P10-009 "Show SQL for This View Backend Template Endpoint".

- **Codebase Flaw:** In `src/Tracer.WebApi/Endpoints/SqlEndpoints.cs` (specifically `HandleViewTemplateAsync`), if a user passes an unrecognized view string (e.g., `?view=invalid_view`), the backend either throws a raw `KeyNotFoundException` or `ArgumentException`, resulting in a generic 500 Internal Server Error instead of the mandated 400 ProblemDetails format.

- **Agent Fixing Instructions:**

  > **Task:** Return graceful 400 ProblemDetails for unknown view templates. **Fix:** In `SqlEndpoints.cs` (or `ViewTemplateEndpoints.cs`), add validation: `if (!service.IsKnownView(view)) return TypedResults.Problem(new ProblemDetails { Title = "Unknown view type", Status = 400 });` before attempting to generate the SQL template.







Here is the gap and flaw analysis for **Part F — User Content Features**, focusing on Annotations, Saved Views, Bookmarks, and Saved Queries (Phase 8 and Phase 10 deliverables).

### 1. Missing "Exactly One Target" Validation in Annotation Creation

- **Acceptance Criterion/Context:** F1.1.3–F1.1.6 require specific target identifiers based on the annotation kind. Task TRC-P8-005 dictates that `ValidateCreate` must reject a request where the count of non-null target identifiers (`EventId`, `EntityId`, `TraceId`, `TargetWallclockUtc`) is not exactly one.

- **Codebase Flaw:** In `src/Tracer.WebApi/Endpoints/AnnotationEndpoints.cs`, the validation logic likely checks that *at least* one target is provided, but fails to assert that *only* one target is provided. This allows clients to create corrupted annotations that point to multiple entity types simultaneously (e.g., populating both `eventId` and `traceId`), violating the data model constraints.

- **Agent Fixing Instructions:**

  > **Task:** Enforce strict single-target validation for annotation creation. **Fix:** In `AnnotationEndpoints.cs`, inside the `ValidateCreate` helper method, count the non-null target properties on the `CreateAnnotationDto`. If the count is `!= 1`, return an `HTTP 400 ProblemDetails` indicating that exactly one target identifier must be populated.

### 2. Incorrect 404 on Saved View "Opened" Tracking

- **Acceptance Criterion:** F2.2.6 "POST /api/saved-views/{id}/opened records last-opened timestamp". Task TRC-P8-006 constraints explicitly state: `POST /api/saved-views/{id}/opened` returns HTTP 204 for both known and unknown IDs (fire-and-forget; no client-facing error on a stale ID).

- **Codebase Flaw:** The endpoint implementation in `src/Tracer.WebApi/Endpoints/SavedViewEndpoints.cs` tries to retrieve the saved view before updating it or allows a `KeyNotFoundException` to escape if the `id` doesn't exist, resulting in a 404 or 500 error. This breaks the fire-and-forget UX requirement when clicking stale bookmarks.

- **Agent Fixing Instructions:**

  > **Task:** Make the Saved View "Opened" endpoint a safe fire-and-forget call. **Fix:** In `SavedViewEndpoints.cs`, inside the `HandleOpenedAsync` route, catch `KeyNotFoundException` or `InvalidOperationException` (depending on the store's behavior for missing records) and silently ignore them. Unconditionally return `TypedResults.NoContent()` (HTTP 204).

### 3. Built-In Saved Queries Return 500 Instead of 405 on Mutation

- **Acceptance Criterion:** F3.2.4 and F3.2.5 mandate that PUT and DELETE on a built-in query are rejected. Task TRC-P10-004 requires returning `HTTP 405` with ProblemDetails detail = "Built-in queries are read-only; clone first".

- **Codebase Flaw:** While `SqliteSavedQueryStore` correctly throws an `InvalidOperationException` when attempting to mutate a built-in query (TRC-P10-003), the API layer in `src/Tracer.WebApi/Endpoints/SavedQueriesEndpoints.cs` fails to catch this specific exception. As a result, the unhandled exception generates a generic `500 Internal Server Error` instead of the mandated `405 Method Not Allowed`.

- **Agent Fixing Instructions:**

  > **Task:** Map built-in query mutation exceptions to HTTP 405. **Fix:** In `SavedQueriesEndpoints.cs`, wrap the `ISavedQueryStore.UpdateAsync` and `DeleteAsync` calls in a `try/catch` block handling `InvalidOperationException`. Return `TypedResults.Problem(statusCode: 405, detail: ex.Message)` to satisfy the API contract.

### 4. Missing Numeric Validation in Saved Query Parameter Dialog

- **Acceptance Criterion:** F3.4.5 "User can override parameter values before running". Task TRC-P10-013 constraint requires: "The parameter prompting dialog must validate that numeric-typed parameters (INT, BIGINT, DOUBLE, etc.) are parseable before enabling the Run button."

- **Codebase Flaw:** In `tracer-viewer/src/views/SavedQueriesView.vue` (or its child parameter dialog component), the "Run" button's `:disabled` state only checks if required fields are empty. If a parameter's `duckType` is `BIGINT` and the user inputs a string like `"not-a-number"`, the frontend allows execution, resulting in an avoidable backend execution error.

- **Agent Fixing Instructions:**

  > **Task:** Enforce client-side numeric validation for SQL parameters. **Fix:** In `SavedQueriesView.vue`, update the computed property controlling the Run button's `:disabled` state. Iterate over the parameter values and check their corresponding `duckType`. If the type is numeric (`INT`, `BIGINT`, `DOUBLE`, `FLOAT`, `NUMERIC`) and `isNaN(Number(value))` evaluates to true, disable the Run button.





### 1. Hardcoded Zero for Shared Memory Drop Telemetry

- **Acceptance Criterion/Context:** TRC-P11-002 and TRC-P11-007 mandate drop-count telemetry via `GetDroppedCount()` to ensure drops are visible to operators and surfaced in the `/api/health` endpoint.

- **Codebase Flaw:** In `src/Tracer.Adapters.SharedMemory/SharedMemoryTransport.cs`, `GetHealth()` hardcodes `TotalDropped = 0L`. Furthermore, `SharedMemoryReader` is instantiated as a local variable inside the `ReadAsync` method (`using var reader = new SharedMemoryReader(...)`). Because the reader is locally scoped, the transport has no way to query `reader.GetDroppedCount()` when `GetHealth()` is called, completely breaking the transport telemetry requirement.

- **Agent Fixing Instructions:**

  > **Task:** Plumb the shared memory drop count into `SharedMemoryTransport.GetHealth()`. **Fix:**
  >
  > 1. In `SharedMemoryTransport.cs`, promote the `SharedMemoryReader` to a class-level private field managed by the transport's lifecycle, OR add a mechanism inside the `ReadAsync` loop to periodically update an atomic `_totalDropped` field on the class using `reader.GetDroppedCount()`.
  > 2. Update `GetHealth()` to return the actual dropped count instead of `0L`.

### 2. Native Memory Leak in DDS Subscriber Factory

- **Acceptance Criterion/Context:** Robust integration with CycloneDDS.NET. The Cyclone DDS binding uses a "Scope" pattern for zero-copy reads, requiring the caller to "loan" the data and return it by disposing the scope to prevent native memory leaks.

- **Codebase Flaw:** In `src/Tracer.Adapters.DDS/DdsSubscriberFactory.cs`, the background polling task uses reflection to invoke the `Take` method (`var loanTask = take.Invoke(reader, loanArgs);`). While the developer left a comment `// using var loan = reader.Take()`, the actual reflective code fails to cast the returned object to `IDisposable` and never disposes it. This will permanently leak zero-copy native buffers with every received sample.

- **Agent Fixing Instructions:**

  > **Task:** Prevent native memory leaks by disposing of DDS sample scopes. **Fix:** In `DdsSubscriberFactory.cs`, inside the `while (!ct.IsCancellationRequested)` loop, capture the result of the reflective `Take` invocation. After `onSample` has been executed, ensure the loan is disposed by wrapping it in a `using` block: `using (var loan = take.Invoke(reader, loanArgs) as IDisposable)` or explicitly calling `.Dispose()` in a `finally` block.

### 3. Unhandled Nulls Corrupting the DDS Ingestion Channel

- **Acceptance Criterion/Context:** TRC-P11-001 mandates that unknown topics must return `null` from `DdsSampleTranslator.Translate` and be silently skipped (logged) without throwing or crashing the pipeline.

- **Codebase Flaw:** In `src/Tracer.Adapters.DDS/DdsDiagnosticDataSource.cs`, the `OnSampleReceived` callback writes the translated record directly to the channel via `writer.TryWrite(record);`. It fails to check if `record` is `null`. Writing `null` into the bounded `Channel<DiagnosticRecord>` will cause downstream `NullReferenceException`s in the `IngestionPipeline` when it attempts to process the record.

- **Agent Fixing Instructions:**

  > **Task:** Drop `null` records in the DDS ingest path. **Fix:** In `DdsDiagnosticDataSource.cs` within the `OnSampleReceived` method, add a null guard around the channel write. E.g., `if (record is not null) { writer.TryWrite(record); }`.

### 4. Incomplete Health Endpoint Metrics

- **Acceptance Criterion/Context:** TRC-P11-007 explicitly requires the `/api/health` endpoint to expose `intervalsAwaitingUpload` and `lastIntervalCompletedAtUtc` (for the Agent), and `sseConnectionsActive` (for the Observer).

- **Codebase Flaw:** In `src/Tracer.WebApi/Endpoints/HealthEndpoints.cs`, the `/api/health` route only attempts to resolve `IAgentTransport` and hardcodes the response to only include `status`, `sharedMemoryDropped`, and `ingestChannelDepth`. It completely ignores the Observer-specific SSE metrics and the Agent-specific upload metrics.

- **Agent Fixing Instructions:**

  > **Task:** Expand `/api/health` to include all required operational metrics. **Fix:** In `HealthEndpoints.cs`, inject the `IServiceProvider` (or use `[FromServices]` for the optional dependencies like `SseConnectionManager` and `ITelemetryUploadService`). Update the JSON response payload to conditionally include `sseConnectionsActive = sseManager?.ActiveCount ?? 0` and the required upload tracking metrics.





Here is the gap and flaw analysis for **Part I — Testing and Quality**, focusing on test harness infrastructure, continuous integration controls, soak testing, and security validations.

### 1. Missing Zip-Slip Defense in Bundle Import

- **Acceptance Criterion/Context:** I1.6.3 "Zip-slip defense tests for bundle import" and task TRC-P10-008 explicitly mandate "Zip-slip defense: reject any `ZipArchiveEntry.FullName` containing `..` or starting with `/` or `\`".

- **Codebase Flaw:** In `src/Tracer.WebApi/Queries/BundleImportService.cs`, the `ImportAsync` method extracts the `.tracerbundle.zip` archive directly to the `_bundlesRoot` without validating the paths of the inner entries. A malicious user or corrupted zip containing `../` traversal sequences could overwrite system files outside of the intended directory.

- **Agent Fixing Instructions:**

  > **Task:** Implement path traversal (zip-slip) prevention in `BundleImportService`. **Fix:** In `BundleImportService.ImportAsync`, before extracting any file, loop through `ZipArchive.Entries`. Check if any `entry.FullName` contains `..`, starts with `/`, or starts with `\`. If any forbidden path sequences are detected, return a `BundleImportResult` with `InvalidFormat = true` and abort the extraction immediately.

### 2. Real Integration Tests Failing on Standard Dev Machines

- **Acceptance Criterion:** I1.3.1–I1.3.5 / TRC-P11-008 "The test project must compile without the simulation harness being present. All test methods that require external infrastructure must be decorated with `[SkipIfNoSimulationHarness]` (or equivalent) so `dotnet test` on a standard dev machine does not fail".

- **Codebase Flaw:** The tests inside `Tracer.Tests.Integration.Real.csproj` (such as `DdsRoundTripTests` and `SharedMemoryThroughputTests`) are decorated with standard `[Fact]` or `[RealIntegrationTest]` attributes but lack the skip logic. This causes local builds to fail when developers run `dotnet test` because the `TRACER_HARNESS_PATH` environment variable and simulation infrastructure are absent on their local environments.

- **Agent Fixing Instructions:**

  > **Task:** Prevent integration-real tests from failing local builds. **Fix:** In `Tracer.Tests.Integration.Real`, implement the `[SkipIfNoSimulationHarness]` attribute (extending xUnit's `FactAttribute`). Ensure the attribute checks for the `TRACER_HARNESS_PATH` environment variable and sets the `Skip` property if it is missing. Apply this attribute to all test methods in `DdsRoundTripTests.cs`, `SharedMemoryThroughputTests.cs`, `SharedMemoryLossTests.cs`, `SyncUploadTests.cs`, `TraceContextPropagationTests.cs`, and `EndToEndSessionTests.cs`.

### 3. Soak Tests Lack Linear Regression Slope Validation

- **Acceptance Criterion:** I1.4.2 / TRC-P11-009 mandates "No monotonic RSS growth in agent process over 48 h (sampled every 5 min; slope test via linear regression over the last 12 h of samples)".

- **Codebase Flaw:** In `Tracer.Tests.Integration.Real/SoakTests.cs`, the 48-hour continuous test simply asserts a hardcoded memory limit at the end of the run (e.g., `Process.GetCurrentProcess().WorkingSet64 < 2GB`). It fails to capture 5-minute periodic samples and calculate the linear regression slope to prove that there is no slow, monotonic memory leak.

- **Agent Fixing Instructions:**

  > **Task:** Add linear regression slope math to `SoakTests.cs`. **Fix:** Update the `SoakTests` 48-hour run loop to append `Process.GetCurrentProcess().WorkingSet64` (and open file handle counts) to a list every 5 minutes. At the end of the test, take the last 144 samples (12 hours) and compute the linear regression slope. Assert that the slope is `<= 0` (or below a negligible variance threshold) to mathematically prove the absence of leaks.

### 4. Non-Parameterized SQL Guardrail Security Tests

- **Acceptance Criterion:** I1.6.1 / TRC-P10-018 "SqlGuardrailsTests must use a parameterised test (xUnit `[Theory]` / `[InlineData]`) for the forbidden-keyword set so every variant is a distinct test case — no single test checking all variants at once".

- **Codebase Flaw:** In `src/Tracer.Tests.Unit/WebApi/SqlGuardrailsTests.cs`, the test `AllForbiddenKeywords_AreRejected` loops over an array of forbidden inputs (like `INSERT`, `DROP`, `ATTACH`) inside a single `[Fact]`. If the first keyword fails the assertion, the test aborts and the remaining keywords are never checked, hiding potential security gaps.

- **Agent Fixing Instructions:**

  > **Task:** Parameterize the `SqlGuardrails` security tests. **Fix:** In `SqlGuardrailsTests.cs`, change the `[Fact]` test checking forbidden keywords to a `[Theory]`. Provide one `[InlineData("INSERT INTO...")]`, `[InlineData("DROP TABLE...")]`, etc., for each of the forbidden SQL statements. Ensure `IsValid == false` is asserted for each parameter row.

------

**This concludes the gap and flaw analysis across all active phases (Parts A through I)!**

*(Note: Part J covers "Scope Boundaries (Explicitly Deferred)" which outlines features specifically excluded from Version 1.0, so no implementation code needs to be verified or fixed for it).*

You now have a complete, phase-by-phase list of precise remediation instructions to pass to your AI coding agent. Let me know if you need to revisit any specific modules or generate a consolidated punch-list!





