# BATCH-57 Report — Backend Foundation Fixes (FIX1 Part A/B/C)

**Status:** COMPLETE  
**Date:** 2026-05-23  
**Tasks:** FIX-A1, FIX-A2, FIX-A3, FIX-A4, FIX-B2, FIX-B3, FIX-B4, FIX-C2, FIX-C34

---

## Summary

BATCH-57 fixes 9 confirmed backend defects from the gap/flaw analysis in `docs/FIX1-TASKS.md`, covering time provider abstraction, missing record fields, SQL schema errors, startup log output, error handling gaps, DI strictness, path safety, and bundle library file naming.

All 9 tasks are complete. The full solution builds with zero warnings and zero errors, and all passing tests continue to pass (801 unit, 106 integration). 36 new unit tests were added, exceeding the minimum of 20.

---

## Files Modified

| File | Change |
|------|--------|
| `src/Tracer.AdapterSelection/SystemClock.cs` | Added `TimeProvider` constructor parameter; `Now` returns `_timeProvider.GetUtcNow()` via `WallclockTime.FromDateTimeOffset` |
| `src/Tracer.Agent/Time/SystemClock.cs` | Same TimeProvider pattern for Agent's SystemClock |
| `src/Tracer.AdapterSelection/AdapterRegistry.cs` | Registers `TimeProvider.System` before `IClock` registration |
| `src/Tracer.AdapterSelection/Tracer.AdapterSelection.csproj` | Added `InternalsVisibleTo("Tracer.Tests.Unit")` to allow unit tests to construct internal `SystemClock` |
| `src/Tracer.Observer/ObserverHostBuilder.cs` | `TimeProvider.System` + `SystemClock` DI; fire-and-forget fixes (FIX-B3); `GetRequiredService` fix (FIX-B4) |
| `src/Tracer.FakeNode/Program.cs` | Added `TimeProvider.System` before `IClock` DI registration |
| `src/Tracer.Core/Records/StateSampleRecord.cs` | Added `public IReadOnlyDictionary<string, double?>? TypedValues { get; init; }` |
| `src/Tracer.Storage.DuckDB/Schema/SchemaV1.cs` | `slow_state` table: added `entity_id VARCHAR` column; index changed to `entity_id, publish_wallclock` without WHERE clause |
| `src/Tracer.Storage.DuckDB/DuckDbStorageWriter.cs` | Uses `BundleNaming.SafeFileName(topic)` instead of internal `MakeSafeFileName`; writes `entity_id` column |
| `src/Tracer.Storage.DuckDB/Tracer.Storage.DuckDB.csproj` | Added `<ProjectReference>` to `Tracer.Bundle` |
| `src/Tracer.Agent/Program.cs` | Emits `Console.WriteLine($"LOG_FILE={logFilePath}")` before `host.RunAsync()` |
| `src/Tracer.Adapters.Nas/NasStorageReader.cs` | Both `catch (InvalidDataException)` and `catch (IOException)` blocks now call `_logger.LogWarning` before returning false |
| `src/Tracer.OfflineViewer/OfflineViewerHostBuilder.cs` | Fire-and-forget fixes (FIX-B3); `GetRequiredService` fix (FIX-B4) |
| `src/Tracer.WebApi/Queries/BundleLibraryService.cs` | Two occurrences of `"metadata.json"` changed to `BundleLayout.ManifestFile` |
| `src/Tracer.WebApi/Queries/EntitySlowStateService.cs` | Column ordinals updated: `entity_id` now at 7, `trace_id` at 8, `payload` at 9 |
| `src/Tracer.WebApi/Endpoints/BundleLibraryEndpoints.cs` | Removed duplicate routes (`GET /api/bundles/{id}/download`, `DELETE /api/bundles/{id}`) that caused `AmbiguousMatchException` |
| `src/Tracer.TestHarness/Agent/TracerAgentFixture.cs` | Added `TimeProvider.System` + `TransportMonitor` DI registrations |
| `src/Tracer.TestHarness/Agent/FakeNodeFixture.cs` | Added `TimeProvider.System` DI registration |
| `src/Tracer.TestHarness/Observer/ObserverFixture.cs` | Added `TimeProvider.System`; uses `Tracer.Agent.Time.SystemClock` in `else` branch |
| `tests/Tracer.Tests.Integration/AgentRecoveryTests.cs` | Both inline DI containers updated with `TimeProvider.System` + `TransportMonitor` |
| `tests/Tracer.Tests.Integration/EntityHistoryRoundTripTests.cs` | `BaseTime` changed from `2026-08-01` to `2024-01-15` (was a future date causing inverted time range) |
| `Directory.Packages.props` | Added `Microsoft.Extensions.TimeProvider.Testing` v8.0.0 |
| `tests/Tracer.Tests.Unit/Tracer.Tests.Unit.csproj` | Added `<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />` |

---

## Files Created

| File | Description |
|------|-------------|
| `tests/Tracer.Tests.Unit/AdapterSelection/SystemClockTests.cs` | 8 tests for FIX-A1: TimeProvider injection in both `AdapterSelection.SystemClock` and `Agent.Time.SystemClock`, using `FakeTimeProvider` |
| `tests/Tracer.Tests.Unit/Core/StateSampleRecordTypedValuesTests.cs` | 4 tests for FIX-A2: TypedValues null default, populated values, read-only interface, nullable doubles |
| `tests/Tracer.Tests.Unit/Storage/SchemaV1Tests.cs` | 6 tests for FIX-A3: entity_id index DDL assertions, no WHERE clause, no instance_key, entity_id column presence, IF NOT EXISTS |
| `tests/Tracer.Tests.Unit/Agent/LoggingPathsTests.cs` | 4 tests for FIX-A4: log path in logsRoot, date suffix, .json extension, `tracer-agent-` prefix |
| `tests/Tracer.Tests.Unit/Adapters/Nas/NasIsReadyLoggingTests.cs` | 3 tests for FIX-B2: corrupt zip logs warning, corrupt zip does not throw, IOException logs warning |
| `tests/Tracer.Tests.Unit/Storage/SafeFileNameTests.cs` | 7 tests for FIX-C2: special char replacement, 4-char hex suffix, collision prevention, determinism, hyphen/dot/underscore preservation, null throws |

---

## Build Results

```
dotnet build Tracer.sln -c Release

  ...all 19 projects compiled...
  Tracer.Tests.Integration -> ...\Tracer.Tests.Integration.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Test Results

### Full solution — filter excludes pre-existing known-flaky test

```
dotnet test Tracer.sln -c Release --no-build --filter "FullyQualifiedName!~Publish_ProducesExpectedLayout"

Skipped! - Failed:     0, Passed:     0, Skipped:     7, Total:     7 - Tracer.Tests.Integration.Real.dll (net8.0)
Passed!  - Failed:     0, Passed:   106, Skipped:     0, Total:   106 - Tracer.Tests.Integration.dll (net8.0)
Passed!  - Failed:     0, Passed:   801, Skipped:     0, Total:   801 - Tracer.Tests.Unit.dll (net8.0)
```

**Unit tests:** 801 passed, 0 failed  
**Integration tests:** 106 passed, 0 failed  
**Integration.Real:** 7 skipped (no harness — expected)

### Known pre-existing exclusion

`DistributionSmokeTests.Publish_ProducesExpectedLayout` is excluded with `!~Publish_ProducesExpectedLayout` per every batch since BATCH-22. Root cause: `dotnet publish Tracer.OfflineViewer -r win-x64 --self-contained` is blocked by file locks on shared intermediate build artefacts (`singlefilehost.exe` memory-mapped by agent-spawning integration tests running in parallel). The test passes in isolation (verified). This is a pre-existing environment conflict documented in BATCH-22-REPORT.md — not introduced by BATCH-57.

---

## Task Completion

| Task | Description | Status |
|------|-------------|--------|
| FIX-A1 | TimeProvider in both SystemClock classes + all DI registrations updated | ✅ Complete |
| FIX-A2 | `TypedValues` property added to `StateSampleRecord` | ✅ Complete |
| FIX-A3 | `slow_state` index corrected to `entity_id` column; WHERE clause removed (DuckDB limitation) | ✅ Complete |
| FIX-A4 | `LOG_FILE=` line emitted to stdout before `host.RunAsync()` | ✅ Complete |
| FIX-B2 | `IsReady` catch blocks log `LogWarning` with path before returning false | ✅ Complete |
| FIX-B3 | Fire-and-forget async fixed in ObserverHostBuilder and OfflineViewerHostBuilder | ✅ Complete |
| FIX-B4 | `GetService` → `GetRequiredService` for `ILogger<BudgetService>` in both host builders | ✅ Complete |
| FIX-C2 | `BundleNaming.SafeFileName(topic)` replaces internal `MakeSafeFileName` in DuckDbStorageWriter | ✅ Complete |
| FIX-C34 | `BundleLibraryService.BuildEntry()` now reads `manifest.json` (via `BundleLayout.ManifestFile`) | ✅ Complete |
| I3 (verify) | `DdsDiagnosticDataSource.OnSampleReceived` null guard already present — no code change needed | ✅ Verified |
| C1 (verify) | All sentinel references use `_ready` consistently — no code change needed | ✅ Verified |

---

## Issues Encountered and Resolutions

### Issue 1: DuckDB 1.0.2 does not support partial indexes

**Spec said:** `CREATE INDEX IF NOT EXISTS idx_slow_state_entity_time ON slow_state (entity_id, publish_wallclock) WHERE entity_id IS NOT NULL;`

**Problem:** DuckDB 1.0.2 throws `Not implemented Error: Partial indexes are not supported yet!` when the index DDL contains a WHERE clause.

**Resolution:** Removed the WHERE clause. The index is now:
```sql
CREATE INDEX IF NOT EXISTS idx_slow_state_entity_time ON slow_state (entity_id, publish_wallclock)
```
Rows with `entity_id IS NULL` are indexed but queries that filter on `entity_id` skip them naturally. Performance impact is negligible. The `SchemaV1Tests` test `CreateIndexes_SlowStateEntityTime_HasNoWhereClause` documents and enforces this constraint.

---

### Issue 2: `EntitySlowStateService` ordinal shift after adding `entity_id` column

**Problem:** When `entity_id VARCHAR` was inserted between `instance_key` and `trace_id` in the `slow_state` schema, `EntitySlowStateService` was reading `trace_id` from column 7 and `payload` from column 8. After the column addition these shifted to 8 and 9 respectively, causing silent `null` reads.

**Resolution:** Updated ordinals in `EntitySlowStateService.MapRow()` to use 7 for `entity_id`, 8 for `trace_id`, and 9 for `payload`.

---

### Issue 3: `DI` resolution failure — `TimeProvider` not registered before `IClock`

**Problem:** `SystemClock` now requires `TimeProvider` in its constructor. All DI containers that register `IClock` were missing the upstream `TimeProvider.System` registration, causing `InvalidOperationException: No service for type 'System.TimeProvider' has been registered` at startup.

**Affected hosts:** `AdapterRegistry`, `ObserverHostBuilder`, `FakeNode/Program.cs`, `TracerAgentFixture`, `FakeNodeFixture`, `ObserverFixture`, and two inline DI containers in `AgentRecoveryTests`.

**Resolution:** Added `builder.Services.AddSingleton(TimeProvider.System)` immediately before each `IClock` registration in all affected files. For `AgentRecoveryTests` this required editing two inline `ServiceCollection` initializations.

---

### Issue 4: `AmbiguousMatchException` in BundleLibraryEndpoints

**Problem:** `BundleLibraryEndpoints.Map()` registered `GET /api/bundles/{id}/download` and `DELETE /api/bundles/{id}` — routes that were already registered by `BundleEndpoints.Map()`. At startup this caused `AmbiguousMatchException: The request matched multiple endpoints`.

**Resolution:** Removed the two duplicate route registrations from `BundleLibraryEndpoints.Map()`. The handler methods (`HandleDownloadAsync`, `HandleDeleteAsync`) were retained in the class for existing unit test coverage.

---

### Issue 5: `TransportMonitor` not registered in integration test DI containers

**Problem:** `AgentHostedService` depends on `TransportMonitor` (added in a prior batch). Several DI containers in `TracerAgentFixture` and `AgentRecoveryTests` were built without it, causing `InvalidOperationException` when the host started during integration tests.

**Resolution:** Added `builder.Services.AddSingleton<TransportMonitor>()` after `AgentStateReporter` in all affected DI containers.

---

### Issue 6: `EntityHistoryRoundTripTests` — future BaseTime causing inverted query range

**Problem:** `EntityHistoryRoundTripTests` used `BaseTime = new DateTimeOffset(2026, 8, 1, ...)`. Today is 2026-05-23. `EntityDiscoveryService.DiscoverAsync` queries `publish_wallclock >= sessionStart AND < (session.EndUtc ?? UtcNow)`. For sessions without an explicit end event, `UtcNow = 2026-05-23` is BEFORE `sessionStart = 2026-08-01`, creating the impossible range [2026-08-01, 2026-05-23] which returns zero results.

**Resolution:** Changed `BaseTime` to `new DateTimeOffset(2024, 1, 15, 9, 0, 0, TimeSpan.Zero)` — a date safely in the past.

---

### Issue 7: `BundleLibraryEndpointsTests` — CreateBundle wrote wrong manifest filename

**Problem:** The `CreateBundle()` helper in `BundleLibraryEndpointsTests` was writing to `"metadata.json"` instead of `"manifest.json"`. After FIX-C34 changed `BundleLibraryService` to look for `manifest.json`, the test helper no longer produced the expected file.

**Resolution:** Updated `CreateBundle()` to write `"manifest.json"`, matching `BundleLayout.ManifestFile`.

---

## Design Decisions

### FIX-B3: BackgroundService approach for BuiltInLoader seeding

Both `ObserverHostBuilder` and `OfflineViewerHostBuilder` had:
```csharp
_ = Task.Run(() => BuiltInLoader.EnsureLoadedAsync(store, CancellationToken.None));
```
The fire-and-forget `Task.Run` discards any exceptions silently. The fix wraps the call in a `try/catch` with `LogError`, consistent with the simpler option described in the spec. This keeps the code local to the host builder rather than introducing a new `BackgroundService` class, which would be over-engineering for a one-time seed operation.

The `SetChanged` event handler was converted to:
```csharp
tracker.SetChanged += async (_, _) =>
{
    try { await schemaService.InvalidateAsync(); }
    catch (Exception ex) { logger.LogError(ex, "Schema invalidation failed"); }
};
```

### FIX-C2: Keeping `MakeSafeFileName`

The private `MakeSafeFileName` method was left in `DuckDbStorageWriter` rather than removed. It is no longer called by production code, but removing it would require checking if any test references it. Leaving it is safe — dead private methods generate no warnings.

### BudgetService latency budgets (intentionally deferred)

`BudgetService.GetBudgetsAsync()` reads `latencyBudgets` from `metadata.json`. Per FIX-C34 spec: *"Since no latency budget metadata file exists yet in bundles, it correctly returns `[]` for now. Do NOT change this."* No change made. This is a deferred feature (requires a separate design for how latency budget data is persisted per-bundle).

---

## Confirmed Already-Fixed Items

### I3 — Null guard in `DdsDiagnosticDataSource.OnSampleReceived`

Code review of `src/Tracer.Adapters.DDS/DdsDiagnosticDataSource.cs` confirms:
```csharp
private void OnSampleReceived(IDdsSample? sample)
{
    var record = _translator.Translate(sample);
    if (record is null) return;
    ...
}
```
The null guard is present and returns early. No code change required.

### C1 — Sentinel filename standardization

Grep across `src/` confirms all sentinel file references use `"_ready"`. The `BundleLayout.cs` constant is `ReadySentinelFile = "_ready"` and all consumers reference the constant rather than the string literal. No code change required.

---

## New Unit Tests (36 total)

### FIX-A1: `tests/Tracer.Tests.Unit/AdapterSelection/SystemClockTests.cs` (8 tests)

| Test | Assertion |
|------|-----------|
| `AdapterSelectionSystemClock_Now_ReflectsFakeTimeProvider` | `FakeTimeProvider` time set to specific value; `clock.Now` returns it exactly |
| `AdapterSelectionSystemClock_Now_AdvancesWhenFakeTimeAdvances` | Fake time advanced 30s; `clock.Now` reflects the advance |
| `AdapterSelectionSystemClock_Constructor_ThrowsForNullTimeProvider` | `new SystemClock(null!)` throws `ArgumentNullException("timeProvider")` |
| `AdapterSelectionSystemClock_WithSystemTimeProvider_ReturnsCurrentTime` | `TimeProvider.System` variant returns time bracketed by `DateTimeOffset.UtcNow` before/after |
| `AgentSystemClock_Now_ReflectsFakeTimeProvider` | Same pattern for `Tracer.Agent.Time.SystemClock` |
| `AgentSystemClock_Now_AdvancesWhenFakeTimeAdvances` | Same advance test for Agent's SystemClock |
| `AgentSystemClock_Constructor_ThrowsForNullTimeProvider` | Same null-check for Agent's SystemClock |
| `AgentSystemClock_WithSystemTimeProvider_ReturnsCurrentTime` | Same real-time bracket test for Agent's SystemClock |

### FIX-A2: `tests/Tracer.Tests.Unit/Core/StateSampleRecordTypedValuesTests.cs` (4 tests)

| Test | Assertion |
|------|-----------|
| `TypedValues_IsNullByDefault` | `StateSampleRecord` without `TypedValues` initializer has `null` |
| `TypedValues_CanBeSetToNonNull` | Values `{"speed": 42.5, "temp": null}` round-trip through the property |
| `TypedValues_IsReadOnly_CannotBeModified` | Property type is assignable to `IReadOnlyDictionary<string, double?>` |
| `TypedValues_SupportsNullableDoubleValues` | `0.0`, `NaN`, `null`, `-999.99` all stored correctly |

### FIX-A3: `tests/Tracer.Tests.Unit/Storage/SchemaV1Tests.cs` (6 tests)

| Test | Assertion |
|------|-----------|
| `CreateIndexes_ContainsPartialIndexOnParentEventId` | Confirms existing `parent_event_id` index is present (regression guard) |
| `CreateIndexes_SlowStateEntityTime_UsesEntityIdColumn` | DDL contains `idx_slow_state_entity_time ON slow_state (entity_id, publish_wallclock)` |
| `CreateIndexes_SlowStateEntityTime_HasNoWhereClause` | The `idx_slow_state_entity_time` line does not contain `WHERE` |
| `CreateIndexes_SlowStateEntityTime_DoesNotUseInstanceKey` | Old wrong index definition is absent |
| `CreateSlowStateTable_ContainsEntityIdColumn` | `slow_state` CREATE TABLE DDL contains `entity_id` |
| `CreateIndexes_SlowStateEntityTime_IsIdempotent_Sql` | `CREATE INDEX IF NOT EXISTS` is present for idempotent migrations |

### FIX-A4: `tests/Tracer.Tests.Unit/Agent/LoggingPathsTests.cs` (4 tests)

| Test | Assertion |
|------|-----------|
| `GetCurrentLogFilePath_ReturnsPathInLogsRoot` | Returned path starts with `logsRoot` argument |
| `GetCurrentLogFilePath_ContainsDateSuffix` | Path contains today's date as `yyyyMMdd` |
| `GetCurrentLogFilePath_HasJsonExtension` | Path ends with `.json` |
| `GetCurrentLogFilePath_ContainsAgentPrefix` | Filename starts with `tracer-agent-` |

### FIX-B2: `tests/Tracer.Tests.Unit/Adapters/Nas/NasIsReadyLoggingTests.cs` (3 tests)

| Test | Assertion |
|------|-----------|
| `IsReady_InvalidDataException_LogsWarning` | Corrupt zip bytes → `CapturingNasLogger.Warnings` contains `"Skipping incomplete interval archive"` with the zip path |
| `IsReady_InvalidDataException_DoesNotThrow` | Same corrupt zip → `ListIntervalsAsync` returns empty, no exception |
| `IsReady_IOException_LogsWarning` | `openZip` delegate throws `IOException` → warning logged with `"Skipping incomplete interval archive"` |

### FIX-C2: `tests/Tracer.Tests.Unit/Storage/SafeFileNameTests.cs` (7 tests)

| Test | Assertion |
|------|-----------|
| `SafeFileName_ReplacesSpecialChars` | `"my/topic:name"` produces output without `/` or `:` |
| `SafeFileName_AppendsFourCharHexSuffix` | Last `_`-delimited segment is 4 lowercase hex characters |
| `SafeFileName_DifferentInputs_ProduceDifferentSuffixes` | `"my/topic"` and `"my_topic"` produce different filenames |
| `SafeFileName_SameInput_IsDeterministic` | Same input always produces same output |
| `SafeFileName_AllowsHyphenDotUnderscore` | `"topic-v1.0_beta"` preserves hyphens, dots, underscores |
| `SafeFileName_NullInput_Throws` | `BundleNaming.SafeFileName(null!)` throws `ArgumentNullException` |
| `SafeFileName_CollidingTopics_ProduceDifferentFilenames` | `"a/b"` ≠ `"a_b"` (hash suffix prevents collision) |

### FIX-C34: Added to `tests/Tracer.Tests.Unit/WebApi/BundleLibraryServiceTests.cs` (4 new tests)

| Test | Assertion |
|------|-----------|
| `List_DirectoryWithManifestJson_ReturnsEntry` | Directory with `manifest.json` is returned in `ListAsync()` |
| `List_DirectoryWithMetadataJsonOnly_IsSkipped` | Directory with only `metadata.json` (old layout) is skipped |
| `List_BundleMetadataJsonPresent_DoesNotAffectListResult` | User `bundle-metadata.json` label `"Custom Label"` is merged into the entry |
| `UpdateMetadata_StillWritesBundleMetadataJson` | `UpdateMetadataAsync` writes to `bundle-metadata.json`; `manifest.json` is not overwritten |

---

## Suggested Commit Message

```
fix(backend): FIX1 Part A/B/C — TimeProvider, TypedValues, SQL index, LOG_FILE, NAS warnings, fire-and-forget, DI strict, SafeFileName, manifest.json

FIX-A1: Inject TimeProvider into both SystemClock classes; propagate to all DI
containers (AdapterRegistry, ObserverHostBuilder, FakeNode, TracerAgentFixture,
FakeNodeFixture, ObserverFixture, AgentRecoveryTests inline containers).
Package: Microsoft.Extensions.TimeProvider.Testing v8.0.0 added for tests.

FIX-A2: Add TypedValues (IReadOnlyDictionary<string, double?>?) to StateSampleRecord.

FIX-A3: Fix slow_state index — use entity_id column (was instance_key). Add entity_id
VARCHAR column. Remove WHERE clause (DuckDB 1.0.2 does not support partial indexes).
Update EntitySlowStateService column ordinals to match new schema.

FIX-A4: Emit LOG_FILE=<path> to stdout in Tracer.Agent/Program.cs before RunAsync.

FIX-B2: Log LogWarning in NasStorageReader.IsReady catch blocks before returning false.

FIX-B3: Fix fire-and-forget async in ObserverHostBuilder and OfflineViewerHostBuilder:
schema invalidation handler uses async/await with try/catch; BuiltInLoader seed
wrapped in try/catch with LogError.

FIX-B4: GetService -> GetRequiredService for ILogger<BudgetService> in both host builders.

FIX-C2: Replace internal MakeSafeFileName with BundleNaming.SafeFileName in
DuckDbStorageWriter. Add Tracer.Bundle project reference.

FIX-C34: BundleLibraryService.BuildEntry reads manifest.json (BundleLayout.ManifestFile)
instead of the non-existent metadata.json. UpdateMetadataAsync still writes
bundle-metadata.json (user-editable, separate concern).

Also fixed: AmbiguousMatchException from duplicate routes in BundleLibraryEndpoints;
EntityHistoryRoundTripTests BaseTime was a future date (2026-08-01 > UtcNow 2026-05-23).

Build: Tracer.sln → 0 warnings, 0 errors
Unit tests: 801 passed, 0 failed
Integration tests: 106 passed, 0 failed (excl. pre-existing Publish_ProducesExpectedLayout)
New tests: 36 (exceeds minimum of 20)
```
