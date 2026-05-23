# BATCH-57: Backend Foundation Fixes (FIX1 Part A/B/C)

**Batch Number:** BATCH-57  
**Tasks:** FIX-A1, FIX-A2, FIX-A3, FIX-A4, FIX-B2, FIX-B3, FIX-B4, FIX-C2, FIX-C34  
**Source:** `docs/FIX1-TASKS.md` — Part A (Foundation), Part B (Backend), Part C (Storage)  
**Priority:** HIGH  
**Estimated Effort:** 10–14 hours  
**Dependencies:** Requires BATCH-56 complete (already approved)

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.guides/DEV-GUIDE.md`
2. **Fix Specifications:** `docs/FIX1-TASKS.md` — **Part A (Foundation and Cross-Cutting), Part B (Backend), and Part C (Storage Layouts)**
3. **Phase Design Refs:** `docs/tracer_phase11_design.md` §A6.1.1 (TimeProvider), `docs/tracer_phase2_design.md` (Agent startup), `docs/tracer_phase7_design.md` §TRC-P7-002 (slow state index)
4. **Previous Review:** `.dev/tracer/reviews/BATCH-56-REVIEW.md`
5. **CODE-STANDARDS:** `.github/skills/CODE-STANDARDS.md`

### Source Code Locations
- **Agent:** `src/Tracer.Agent/`
- **AdapterSelection:** `src/Tracer.AdapterSelection/`
- **Observer:** `src/Tracer.Observer/`
- **OfflineViewer:** `src/Tracer.OfflineViewer/`
- **DuckDB storage:** `src/Tracer.Storage.DuckDB/`
- **NAS adapter:** `src/Tracer.Adapters.Nas/`
- **DDS adapter:** `src/Tracer.Adapters.DDS/`
- **Bundle format:** `src/Tracer.Bundle/`
- **Web API queries:** `src/Tracer.WebApi/Queries/`
- **Unit tests:** `tests/Tracer.Tests.Unit/`
- **Integration tests:** `tests/Tracer.Tests.Integration/`

### Build Command
```
dotnet build Tracer.sln -c Release
```

### Test Command
```
dotnet test Tracer.sln -c Release --no-build
```

### Report Submission
`.dev/tracer/reports/BATCH-57-REPORT.md`

---

## Context

This batch fixes 9 confirmed defects from the gap/flaw analysis in `docs/FIX1-TASKS.md`. These are all **C# backend** changes — no frontend work in this batch.

**IMPORTANT NOTE on confirmed-already-fixed items:**
- **I3 (Null guard in DdsDiagnosticDataSource)**: Already implemented — `OnSampleReceived` in `src/Tracer.Adapters.DDS/DdsDiagnosticDataSource.cs` already has `if (record is null) return;`. Include a test to verify this behavior but no code fix needed.
- **C1 (Sentinel filename standardization)**: Code already consistently uses `_ready` everywhere. Verify this during implementation and document in report; no code change expected.

---

## 🎯 Batch Objectives

Fix 9 confirmed backend defects covering:
- Time provider abstraction compliance
- Missing record fields
- SQL schema errors
- Startup log output
- Error handling / logging gaps
- DI strictness
- Path safety
- Bundle library file naming

---

## ✅ Tasks

### Task 1: FIX-A1 — TimeProvider in SystemClock

**Files:**
- `src/Tracer.AdapterSelection/SystemClock.cs` (UPDATE)
- `src/Tracer.Agent/Time/SystemClock.cs` (UPDATE)
- `src/Tracer.AdapterSelection/AdapterRegistry.cs` (UPDATE DI registration)
- `src/Tracer.Observer/ObserverHostBuilder.cs` (UPDATE DI registration)
- `src/Tracer.FakeNode/Program.cs` (UPDATE DI registration)

**Specification:** See `docs/FIX1-TASKS.md` Part A §1 and acceptance criterion A6.1.1.

**Fix:**
1. Both `SystemClock` classes currently do `DateTimeOffset.UtcNow` directly.
2. Add a `TimeProvider` constructor parameter to both `SystemClock` classes.
3. Change `Now` to return `WallclockTime.FromDateTimeOffset(_timeProvider.GetUtcNow())`.
4. Update all DI registrations to register `TimeProvider.System` and pass it to `SystemClock`:
   - `AdapterRegistry.cs` — register `SystemClock` with `TimeProvider.System`
   - `ObserverHostBuilder.cs` — `SystemClock` is registered at line `builder.Services.AddSingleton<IClock, SystemClock>()`
   - `FakeNode/Program.cs` — similar registration

**Tests Required:**
- Verify `SystemClock` returns time from injected `TimeProvider` not from `DateTimeOffset.UtcNow`
- Use `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` to set a known time and assert `Now` returns it
- Test both `Tracer.AdapterSelection.SystemClock` and `Tracer.Agent.Time.SystemClock`

---

### Task 2: FIX-A2 — TypedValues in StateSampleRecord

**File:** `src/Tracer.Core/Records/StateSampleRecord.cs` (UPDATE)

**Specification:** See `docs/FIX1-TASKS.md` Part A §2 and acceptance criterion A1.2.4.

**Fix:** Add the following property to `StateSampleRecord`:
```csharp
public IReadOnlyDictionary<string, double?>? TypedValues { get; init; }
```

Ensure all existing code that constructs `StateSampleRecord` still compiles (this is nullable/optional so no breaking changes expected).

**Tests Required:**
- Verify `StateSampleRecord` can be constructed with `TypedValues = null` (default)
- Verify `StateSampleRecord` can be constructed with `TypedValues` populated
- Verify that existing fast state records produced by `DdsSampleTranslator.Translate` are not broken

---

### Task 3: FIX-A3 — Slow State Index SQL Fix

**File:** `src/Tracer.Storage.DuckDB/Schema/SchemaV1.cs` (UPDATE)

**Specification:** See `docs/FIX1-TASKS.md` Part A §3. See also `docs/tracer_phase7_design.md` for TRC-P7-002.

**Fix:** In `SchemaV1.cs`, find the index definition for `idx_slow_state_entity_time`. Currently it reads:
```sql
CREATE INDEX IF NOT EXISTS idx_slow_state_entity_time ON slow_state(instance_key, publish_wallclock);
```

Change it to **exactly**:
```sql
CREATE INDEX IF NOT EXISTS idx_slow_state_entity_time ON slow_state (entity_id, publish_wallclock) WHERE entity_id IS NOT NULL;
```

**Tests Required:**
- Assert that `SchemaV1.GetDdl()` (or the relevant constant/property) contains the correct SQL string with `entity_id`, the space before `(`, and the `WHERE entity_id IS NOT NULL` clause
- Assert it does NOT contain `instance_key` in this context

---

### Task 4: FIX-A4 — TracerAgent LOG_FILE Startup Output

**File:** `src/Tracer.Agent/Program.cs` (UPDATE)

**Specification:** See `docs/FIX1-TASKS.md` Part A §4 and acceptance criterion A6.3.1. See how `src/Tracer.FakeNode/Program.cs` does it (lines ~30-35) for the correct pattern.

**Fix:** After `AgentHostBuilder.Build(args)` returns the host (but before `host.RunAsync()`), resolve the `AgentConfig` from `host.Services`, compute the log file path using `LoggingPaths.GetCurrentLogFilePath(config.LogsRoot)`, and emit `Console.WriteLine($"LOG_FILE={logFilePath}");`.

Note: `AgentHostBuilder.cs` already uses `LoggingPaths.GetCurrentLogFilePath(config.LogsRoot)` in the Serilog configuration, so the path computation is already available.

**Tests Required:**
- This is hard to unit test in isolation, but verify (via code review / read the file) that `Program.cs` emits the `LOG_FILE=` line before `host.RunAsync()`.
- Add an integration-level test in `Tracer.Tests.Integration` that asserts the Agent process stdout starts with `LOG_FILE=` — or document that this will be verified manually.

---

### Task 5: FIX-B2 — NAS Sentinel Warning Logging

**File:** `src/Tracer.Adapters.Nas/NasStorageReader.cs` (UPDATE)

**Specification:** See `docs/FIX1-TASKS.md` Part B §2.

**Fix:** In `NasStorageReader.IsReady(string zipPath)` (around line 223), the method silently returns `false` on `InvalidDataException` and `IOException`. Update both catch blocks to call `_logger.LogWarning(ex, "Skipping incomplete interval archive at {Path}: _ready sentinel missing or zip corrupt", zipPath)` before returning `false`.

Current code:
```csharp
catch (InvalidDataException) { return false; }
catch (IOException) { return false; }
```

Expected after fix:
```csharp
catch (InvalidDataException ex)
{
    _logger.LogWarning(ex, "Skipping incomplete interval archive at {Path}: _ready sentinel missing or zip corrupt", zipPath);
    return false;
}
catch (IOException ex)
{
    _logger.LogWarning(ex, "Skipping incomplete interval archive at {Path}: _ready sentinel missing or zip corrupt", zipPath);
    return false;
}
```

**Tests Required:**
- Create a test with a corrupted zip (invalid data) — verify that `IsReady` returns `false` AND logs a warning
- Create a test with an IO exception scenario (e.g., locked file, use mock `_openZip`) — verify warning is logged
- Use the existing test infrastructure in `NasStorageReader` tests if present, or create new ones

---

### Task 6: FIX-B3 — Fix Fire-and-Forget Async in Startup

**Files:**
- `src/Tracer.Observer/ObserverHostBuilder.cs` (UPDATE, near bottom of `Build()`)
- `src/Tracer.OfflineViewer/OfflineViewerHostBuilder.cs` (UPDATE, same patterns)

**Specification:** See `docs/FIX1-TASKS.md` Part B §3.

There are two fire-and-forget issues to fix, both in Observer and in OfflineViewer (check both):

**Issue 1 — Schema service invalidation:**
```csharp
tracker.SetChanged += (_, _) => { _ = schemaService.InvalidateAsync(); return Task.CompletedTask; };
```

Fix: Wrap the `InvalidateAsync()` call in a `try/catch` so exceptions are logged, not silently discarded. The event handler pattern should remain async-safe:

```csharp
tracker.SetChanged += async (_, _) =>
{
    try { await schemaService.InvalidateAsync(); }
    catch (Exception ex) { app.Services.GetRequiredService<ILogger<ObserverHostBuilder>>().LogError(ex, "Schema invalidation failed"); }
};
```

**Issue 2 — BuiltInLoader seeding:**
```csharp
app.Lifetime.ApplicationStarted.Register(() =>
{
    var store = app.Services.GetRequiredService<ISavedQueryStore>();
    _ = Task.Run(() => BuiltInLoader.EnsureLoadedAsync(store, CancellationToken.None));
});
```

Fix: Create a `BackgroundService` (e.g., `BuiltInQuerySeederService`) that runs `BuiltInLoader.EnsureLoadedAsync` in `ExecuteAsync` with proper exception logging, and register it as a hosted service. This is the correct DI lifecycle pattern.

Alternatively (simpler acceptable fix): wrap the `Task.Run` in a try/catch that logs the exception:
```csharp
_ = Task.Run(async () =>
{
    try { await BuiltInLoader.EnsureLoadedAsync(store, CancellationToken.None); }
    catch (Exception ex) { /* log */ }
});
```

Choose the approach that best fits the existing codebase patterns.

**Tests Required:**
- Test that if `schemaService.InvalidateAsync()` throws, the exception is caught and logged (use a mock)
- Test that BuiltInLoader errors are caught and don't crash the startup

---

### Task 7: FIX-B4 — BudgetService DI Strict Resolution

**Files:**
- `src/Tracer.Observer/ObserverHostBuilder.cs` (UPDATE)
- `src/Tracer.OfflineViewer/OfflineViewerHostBuilder.cs` (UPDATE)

**Specification:** See `docs/FIX1-TASKS.md` Part B §4.

**Fix:** Search for `sp.GetService<ILogger<BudgetService>>()` in both files (Observer line ~270, OfflineViewer similar location). Change to `sp.GetRequiredService<ILogger<BudgetService>>()` so DI validation fails fast rather than returning null.

Also check `src/Tracer.TestHarness/Observer/WebApiFixture.cs` and `src/Tracer.TestHarness/Observer/ObserverFixture.cs` where BudgetService is registered — apply the same fix there.

**Tests Required:**
- Verify the build passes (this is primarily a defensive code quality fix)
- No specific behavior test needed, but document in report

---

### Task 8: FIX-C2 — Use BundleNaming.SafeFileName in DuckDbStorageWriter

**File:** `src/Tracer.Storage.DuckDB/DuckDbStorageWriter.cs` (UPDATE)

**Specification:** See `docs/FIX1-TASKS.md` Part C §2.

**Fix:** In `DuckDbStorageWriter`, the private method `MakeSafeFileName(topic)` replaces non-alphanumeric chars with `_` but has no collision protection (two distinct topics could produce the same safe name). 

Replace the call to `MakeSafeFileName(topic)` at line ~194 with `BundleNaming.SafeFileName(topic)`. This is the same method used by `FastStateCopier` and `FastStateFileLocator` — using it here ensures consistency.

You'll need to add the appropriate `using` reference for `Tracer.Bundle.Format`. Check if `Tracer.Storage.DuckDB.csproj` already has a project reference to `Tracer.Bundle`; if not, add it.

You may keep the private `MakeSafeFileName` method for now (or remove it if no longer referenced elsewhere).

**Tests Required:**
- Test that topics with special chars (e.g., `vehicle:state/fast`) produce distinct safe paths
- Test that two topics that would collide under simple `_` replacement produce different paths (using the hash suffix from `BundleNaming.SafeFileName`)

---

### Task 9: FIX-C34 — BundleLibraryService Reads Wrong Metadata File

**File:** `src/Tracer.WebApi/Queries/BundleLibraryService.cs` (UPDATE)

**Specification:** See `docs/FIX1-TASKS.md` Part C §3 and §4. See also `src/Tracer.Bundle/Format/BundleLayout.cs` for file name constants.

**Background:** The aggregator writes `manifest.json` (via `BundleDirectoryWriter`). `BundleLibraryService.BuildEntry()` looks for `metadata.json`, which does not exist. This means no bundles ever appear in the library listing — a critical bug.

**Fix:**
1. In `BundleLibraryService.BuildEntry()`, change the line that builds `metaPath`:
   ```csharp
   // Before (wrong):
   var metaPath = Path.Combine(dir, "metadata.json");
   
   // After (correct):
   var metaPath = Path.Combine(dir, "manifest.json");
   ```
   
2. Update `ReadAggregatorMetadata(path)` to deserialize from `BundleManifest` (or its `JsonOpts` which already uses camelCase) correctly. The `AggregatorMetadata` private class maps to the fields present in `BundleManifest` (BundleId, CreatedAtUtc, TimeRange, SessionContext) — verify the field names match the camelCase manifest.
   
3. Also check `UpdateMetadataAsync()` — it reads `metadata.json` at line ~63; verify this is for the **user-editable** `bundle-metadata.json` and should NOT be changed.

4. For `BudgetService.GetBudgetsAsync()` — it reads `latencyBudgets` from `metadata.json` at line ~37. This is a separate concern. Since no latency budget metadata file exists yet in bundles, it correctly returns `[]` for now. Do NOT change this — leave a comment noting it's a deferred feature. Document in your report.

**Tests Required:**
- Test `BundleLibraryService.ListAsync()` with a directory containing a properly formatted `manifest.json` — assert the entry is returned
- Test with directory that has no `manifest.json` — assert the directory is skipped gracefully (no exception)
- Test that `bundle-metadata.json` is still read and merged for user metadata when present

---

## 🧪 Testing Requirements

**Minimum:** 20+ unit tests total across all tasks.

**Quality Standards:**
- Tests must verify ACTUAL behavior, not just compilation
- Tests that mock `TimeProvider` must actually assert the returned time matches what was set
- Tests for NAS warning must capture log output and assert specific messages
- Tests for BundleLibraryService must use a real temp directory with proper files

**Test naming convention:** Follow existing project conventions in `tests/Tracer.Tests.Unit/`.

---

## 🔄 MANDATORY WORKFLOW

Complete tasks in sequence with ALL tests passing before moving on:

1. Task 1 (TimeProvider) → implement → tests pass ✅
2. Task 2 (TypedValues) → implement → tests pass ✅
3. Task 3 (SQL index) → implement → tests pass ✅
4. Task 4 (LOG_FILE) → implement → verify ✅
5. Task 5 (NAS warnings) → implement → tests pass ✅
6. Task 6 (Fire-and-forget) → implement → tests pass ✅
7. Task 7 (DI strict) → implement → build passes ✅
8. Task 8 (SafeFileName) → implement → tests pass ✅
9. Task 9 (BundleLibrary) → implement → tests pass ✅
10. Full solution build with `dotnet build Tracer.sln -c Release` — ZERO warnings, ZERO errors ✅
11. Full test suite with `dotnet test Tracer.sln -c Release --no-build` — ALL passing ✅

**DO NOT** move to the next task until current task's tests ALL pass.  
**DO NOT** ask for permission to run tests or fix compilation errors — do it all. No laziness.

---

## ⚠️ Quality Standards

**❗ TEST QUALITY EXPECTATIONS:**
- **NOT ACCEPTABLE:** Tests that only check the object compiles or exists
- **NOT ACCEPTABLE:** Tests that don't assert actual values
- **REQUIRED:** Tests that break if the implementation is wrong
- **REQUIRED:** For FIX-A1, use `FakeTimeProvider` and assert the exact time returned
- **REQUIRED:** For FIX-B2, capture log output and assert warning messages

**❗ REPORT QUALITY:**
- Report MUST document issues encountered and solutions chosen
- Report MUST document any deferred items (e.g., BudgetService latency budgets)
- Report MUST include final test counts per project

---

## 📊 Report Requirements

When done, write `.dev/tracer/reports/BATCH-57-REPORT.md`.

**Report must include:**
- Implementation status for each task (DONE / DEFERRED + reason)
- Final build output (`dotnet build Tracer.sln -c Release` — must show 0 warnings, 0 errors)
- Final test output (`dotnet test Tracer.sln --no-build`)
- Issues encountered and how you resolved them
- Design decisions you made beyond the spec
- Any edge cases discovered
- Confirmation on already-fixed items (I3, C1): brief code review notes

---

## 📚 Reference Materials

- **Fix Specs:** `docs/FIX1-TASKS.md` — Parts A, B, C
- **Design:** `docs/tracer_phase2_design.md`, `docs/tracer_phase7_design.md`, `docs/tracer_phase11_design.md`
- **Bundle format:** `src/Tracer.Bundle/Format/BundleLayout.cs`, `src/Tracer.Bundle/Format/BundleNaming.cs`
- **Existing safe name usage:** `src/Tracer.Aggregator/Consolidation/FastStateCopier.cs` (lines 92-93)
- **FakeNode LOG_FILE pattern:** `src/Tracer.FakeNode/Program.cs` (lines ~30-35)
- **Observer fire-and-forget location:** `src/Tracer.Observer/ObserverHostBuilder.cs` (near end of `Build()`)
- **OfflineViewer counterparts:** `src/Tracer.OfflineViewer/OfflineViewerHostBuilder.cs`
