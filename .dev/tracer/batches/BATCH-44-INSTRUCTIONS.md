# BATCH-44 — TriggerEvalService, TriggerEvalEndpoints, Lifecycle Topic Configuration

**Estimated effort:** 15–20 hours  
**Target tasks:** TRC-P8-007, TRC-P8-008, TRC-P8-010  
**Report output:** `.dev/tracer/reports/BATCH-44-REPORT.md`

---

## 1. Onboarding

### Project Context

Read these documents before writing any code:

- **Design reference:** `docs/tracer_phase8_design.md` §8 (Trigger Evaluation Log) and §9 (Lifecycle Topic Configuration)
- **Task definitions:** `docs/TASK-DETAIL.md` — search for `## TRC-P8-007`, `## TRC-P8-008`, `## TRC-P8-010`
- **Previous review:** `.dev/tracer/reviews/BATCH-43-REVIEW.md`
- **DEBT-TRACKER:** `.dev/tracer/DEBT-TRACKER.md`

### Key Architecture Patterns (Established in BATCH-43)

All the patterns below are **already working** — follow them exactly.

**DuckDB query pattern** (`LiveMultiIntervalReader` + named `$parameter` parameters):
```csharp
await using var pooled = await _reader.AcquireAsync(ct);
var sql = pooled.WithEventsCte("SELECT … FROM events WHERE …");
using var cmd = pooled.Connection.CreateCommand();
cmd.CommandText = sql;
cmd.Parameters.Add(new DuckDBParameter("name", value));
using var reader = cmd.ExecuteReader();
while (reader.Read()) { … }
```
See `src/Tracer.WebApi/Queries/EntityDiscoveryService.cs` and `ScenarioQueryService.cs` for working examples.

However, the design for `TriggerEvalService` (§8.2) uses `conn.BuildEventsUnionSql()` (a CTE-building helper). Use `pooled.WithEventsCte(innerSql)` with a `FROM events` inner query — this is the established pattern.  **Use `pooled.WithEventsCte` as in other query services, NOT `conn.BuildEventsUnionSql` from the design doc.**

**`EventRecordMapper.FromReader(reader)`** is in `src/Tracer.WebApi/Queries/EventRecordMapper.cs` — use it to parse DuckDB reader rows into `EventRecord`.

**Endpoint pattern** (public static handlers, `ArgumentNullException.ThrowIfNull`):
```csharp
public static async Task<Results<Ok<TDto>, NotFound>> HandleAsync(
    [FromQuery] string sessionId,
    [FromServices] SomeService service,
    CancellationToken ct)
{
    ArgumentNullException.ThrowIfNull(service);
    // …
}
```
See `src/Tracer.WebApi/Endpoints/AnnotationEndpoints.cs` — follow this structure exactly.

**SessionQueryService** is already registered. It resolves sessions by ID and returns `null` for unknown sessions. Use it for 404 checks:
```csharp
var session = await sessions.GetAsync(sessionId, ct);
if (session is null) return TypedResults.NotFound();
```

**DI registration** — add new singletons in:
- `src/Tracer.Observer/ObserverHostBuilder.cs` (live mode)
- `src/Tracer.OfflineViewer/OfflineViewerHostBuilder.cs` (bundle mode)

**WallclockTime** — factory `WallclockTime.FromDateTimeOffset(dto)`, access `.ToDateTimeOffset()`.

**`EventId` type** — custom `ulong` wrapper. `new EventId(value)`, `.Value` for the raw ulong, `.ToString()` returns 16-char uppercase hex.

---

## 2. Test-Driven Task Progression (MANDATORY)

> **Do not skip this. Every task must follow this exact workflow.**

For every success condition listed:
1. Write the test first (it will fail — that is expected).
2. Run the test and confirm the failure is the right one (missing class/method, not a compile error you forgot to fix).
3. Implement the minimum code that makes it pass.
4. Run the test again and confirm it is green.
5. Only then move on to the next success condition.

Run tests with:
```
dotnet test tests/Tracer.Tests.Unit/Tracer.Tests.Unit.csproj --no-build --filter "FullyQualifiedName~TriggerEval|FullyQualifiedName~LifecycleTopic"
```

Do **not** run the full test suite without a filter — it hangs indefinitely (known issue DT-028).

After implementing all tasks, run a broader regression check:
```
dotnet test tests/Tracer.Tests.Unit/Tracer.Tests.Unit.csproj --no-build --filter "FullyQualifiedName~TriggerEval|FullyQualifiedName~LifecycleTopic|FullyQualifiedName~Annotation|FullyQualifiedName~SavedView|FullyQualifiedName~ConfigEndpoint"
```

Confirm: zero failures, zero skipped.

---

## 3. Task TRC-P8-007 — TriggerEvalService

**Design reference:** `docs/tracer_phase8_design.md §8.2`  
**Test file:** `tests/Tracer.Tests.Unit/WebApi/TriggerEvalServiceTests.cs` (create new)

### What to Build

**New file: `src/Tracer.WebApi/Queries/TriggerEvalService.cs`**

Contains:
- `public enum TriggerResult { Fired, NotFired }`
- `public sealed record TriggerEvaluation` (all fields below)
- `public sealed record TriggerEvalResult` (wraps `IReadOnlyList<TriggerEvaluation>`)
- `public sealed class TriggerEvalService`

**`TriggerEvaluation` record fields:**
```csharp
public required EventId EventId { get; init; }
public required DateTimeOffset EvaluatedAtUtc { get; init; }
public required string PublisherNode { get; init; }
public required TraceId TraceId { get; init; }  // NOTE: TraceId type (not ulong)
public required string TriggerId { get; init; }
public string? TriggerLabel { get; init; }
public required string Inputs { get; init; }     // raw JSON string
public required TriggerResult Result { get; init; }
public EventId? NextEventId { get; init; }
public string? Reason { get; init; }
```

**`TriggerEvalService` constructor:**
```csharp
public TriggerEvalService(LiveMultiIntervalReader reader, ILogger<TriggerEvalService> logger)
```

**`ListAsync` signature:**
```csharp
public async Task<TriggerEvalResult> ListAsync(
    string sessionId,
    WallclockTime from,
    WallclockTime to,
    string? triggerIdFilter,
    TriggerResult? resultFilter,
    int limit,
    CancellationToken ct)
```

**SQL strategy:**
- Base WHERE: `topic = 'scenario.trigger_evaluated'` (hardcoded)
- Time range: `publish_wallclock >= $from AND publish_wallclock < $to`
- Optional trigger ID filter: `JSON_EXTRACT_STRING(payload, '$.triggerId') = $triggerId`
- Optional result filter: `JSON_EXTRACT_STRING(payload, '$.result') = $result` (value is lowercase: `"fired"` or `"not-fired"`)
- LIMIT enforced in SQL: `LIMIT $limit`
- Use `pooled.WithEventsCte(innerSql)` pattern (not `BuildEventsUnionSql`)
- Use `EventRecordMapper.FromReader(reader)` to parse rows

**`ParseEvaluation` method (private):**
- Parse JSON payload to extract `triggerId`, `triggerLabel`, `inputs`, `result`, `nextEventId`, `reason`
- `result` field in JSON is `"fired"` (lowercase) → `TriggerResult.Fired`; anything else → `TriggerResult.NotFired`
- `nextEventId` in JSON is a 16-char hex string → `new EventId(ulong.Parse(hexStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture))`
- On **any** exception (malformed JSON etc.): return a degraded `TriggerEvaluation` with `TriggerId = "(malformed payload)"`, `Inputs = ev.PayloadJson`, `Result = TriggerResult.NotFired` — **do not rethrow**

### Success Conditions (9 tests)

1. **`ListAsync_OnlyReturnsTriggerEvaluatedEvents`** — Mix of `scenario.trigger_evaluated` and other-topic events; only trigger-evaluated events returned.
2. **`ListAsync_FilterByTriggerId`** — Two events with `triggerId = "trigger-A"` and `"trigger-B"`; filter on `"trigger-A"` returns only trigger-A evaluations.
3. **`ListAsync_FilterByResult_Fired`** — Mix of fired/not-fired; `resultFilter = TriggerResult.Fired` returns only fired evaluations.
4. **`ListAsync_TimeRangeRespected`** — Events before `from` and within `[from, to)`; only in-range events returned.
5. **`ParseEvaluation_ExtractsAllPayloadFields`** — Payload: `{"triggerId":"t1","triggerLabel":"My Trigger","inputs":{"speed":12},"result":"fired","nextEventId":"00000000000000FF"}`; assert `TriggerId = "t1"`, `TriggerLabel = "My Trigger"`, `Result = TriggerResult.Fired`, `Inputs` contains `"speed"`, `NextEventId.Value == 255`.
6. **`ParseEvaluation_NotFiredResult`** — Payload with `"result":"not-fired"`; assert `Result == TriggerResult.NotFired`.
7. **`ParseEvaluation_MalformedPayload_ReturnsDegradedResult`** — `PayloadJson = "not-json"`; no exception; `TriggerId = "(malformed payload)"`, `Inputs == "not-json"`.
8. **`ListAsync_EmptyResult_NoException`** — No `scenario.trigger_evaluated` events; returns empty `Evaluations` list, no exception.
9. **`ListAsync_LimitRespected`** — 50 trigger evaluations; `limit: 5`; `Evaluations.Count == 5`.

### Test Infrastructure

Use the **existing** `DuckDbTestHelper` (or equivalent) for creating in-memory DuckDB test intervals — look at `tests/Tracer.Tests.Unit/WebApi/EntityDiscoveryServiceTests.cs` for the test harness pattern.

---

## 4. Task TRC-P8-008 — TriggerEvalEndpoints

**Design reference:** `docs/tracer_phase8_design.md §8.3`  
**Test file:** `tests/Tracer.Tests.Unit/WebApi/TriggerEvalEndpointsTests.cs` (create new)

### What to Build

**New file: `src/Tracer.WebApi/Endpoints/TriggerEvalEndpoints.cs`**

Route: `GET /api/scenario/triggers`

Query parameters:
- `sessionId` (required)
- `from` (optional `DateTimeOffset`)
- `to` (optional `DateTimeOffset`)
- `triggerId` (optional `string`)
- `result` (optional `string`) — case-insensitive; `"fired"` → `TriggerResult.Fired`, `"not-fired"` → `TriggerResult.NotFired`, anything else → `null` (no 400)
- `limit` (optional `int`, default 1000) — clamped to `[1, 5000]`

**Handler logic:**
1. Look up session via `SessionQueryService.GetAsync(sessionId, ct)`
2. If session is `null` → return HTTP 404
3. Parse `result` case-insensitively
4. Clamp `limit` to `[1, 5000]`
5. If `from`/`to` not provided, fall back to `session.StartUtc` / `session.EndUtc` (or `DateTimeOffset.MinValue` / `DateTimeOffset.UtcNow` if session has no range)
6. Call `TriggerEvalService.ListAsync(...)`
7. Return HTTP 200 with `TriggerEvaluationListDto`

**New DTOs in `src/Tracer.WebApi/Contracts/Dto/TriggerEvalDtos.cs`:**
```csharp
public sealed record TriggerEvaluationListDto
{
    public required IReadOnlyList<TriggerEvaluationDto> Evaluations { get; init; }
}

public sealed record TriggerEvaluationDto
{
    public required string EventId { get; init; }         // 16-char uppercase hex
    public required DateTimeOffset EvaluatedAtUtc { get; init; }
    public required string PublisherNode { get; init; }
    public required string TraceId { get; init; }         // 16-char uppercase hex
    public required string TriggerId { get; init; }
    public string? TriggerLabel { get; init; }
    public required string Inputs { get; init; }          // raw JSON string
    public required string Result { get; init; }          // "Fired" or "NotFired"
    public string? NextEventId { get; init; }             // 16-char uppercase hex OR null (NOT "0000000000000000")
    public string? Reason { get; init; }
}
```

**`TriggerEvalDtoMapper` (static class in same file):**
```csharp
public static TriggerEvaluationListDto Map(TriggerEvalResult result) { … }
public static TriggerEvaluationDto Map(TriggerEvaluation e) { … }
```

**Critical:** `NextEventId` in the DTO is `null` when `e.NextEventId is null` (do NOT use `"0000000000000000"`). When non-null, format as 16-char uppercase hex: `e.NextEventId.Value.ToString()`.

**Wire in `ObserverHostBuilder.cs`:**
```csharp
builder.Services.AddSingleton<TriggerEvalService>();
// … in endpoint mapping section:
TriggerEvalEndpoints.Map(app);
```

**Wire in `src/Tracer.OfflineViewer/OfflineViewerHostBuilder.cs`:**
```csharp
builder.Services.AddSingleton<TriggerEvalService>();
// … in endpoint mapping section:
TriggerEvalEndpoints.Map(app);
```

Note: `OfflineViewerHostBuilder` already has `LiveMultiIntervalReader` registered (it reads from bundle DuckDB files). `TriggerEvalService` can be wired identically in both hosts.

### Success Conditions (7 tests)

1. **`GET_ValidSessionId_Returns200`** — Known session with trigger-evaluated events; `GET /api/scenario/triggers?sessionId={id}`; HTTP 200 with non-empty evaluations.
2. **`GET_UnknownSessionId_Returns404`** — `SessionQueryService` returns null; HTTP 404.
3. **`GET_InvalidResultParam_ReturnsAll`** — `result=garbage`; HTTP 200 (not 400); evaluations of all results returned.
4. **`GET_LimitClamped_ToMaximum`** — `limit=99999`; `TriggerEvalService.ListAsync` receives `limit = 5000`; HTTP 200.
5. **`TriggerEvaluationDto_NextEventId_FormattedAsHex16`** — `NextEventId.Value == 255`; `TriggerEvaluationDto.NextEventId == "00000000000000FF"`.
6. **`TriggerEvaluationDto_NullNextEventId_SerializedAsNull`** — `NextEventId = null`; `TriggerEvaluationDto.NextEventId` is `null`.
7. **`DI_TriggerEvalService_Resolves`** — `TriggerEvalService` resolves from Observer DI container without exception. Test pattern: build a minimal DI container following the same structure as `SavedViewEndpointsTests.cs` or `AnnotationEndpointsTests.cs`.

---

## 5. Task TRC-P8-010 — Lifecycle Topic Configuration

**Design reference:** `docs/tracer_phase8_design.md §9`  
**Test file:** `tests/Tracer.Tests.Unit/Agent/LifecycleTopicClassifierTests.cs` (create new)

### What to Build

#### 5.1 — New classes in `src/Tracer.WebApi/`

Create `src/Tracer.WebApi/Lifecycle/LifecycleClassificationConfig.cs`:

```csharp
namespace Tracer.WebApi.Lifecycle;

public sealed class LifecycleClassificationConfig
{
    public IReadOnlyList<string> SpawnSuffixes { get; init; } 
        = new[] { "spawn", "created", "spawned" };

    public IReadOnlyList<string> OwnershipSuffixes { get; init; } 
        = new[] { "ownership_changed", "owner_transferred", "owner_changed" };

    public IReadOnlyList<string> DestructionSuffixes { get; init; } 
        = new[] { "destroyed", "killed", "removed", "despawned" };

    public LifecycleRegexPatterns? Regex { get; init; }
}

public sealed record LifecycleRegexPatterns(string? Spawn, string? Ownership, string? Destruction);
```

Create `src/Tracer.WebApi/Lifecycle/ILifecycleTopicClassifier.cs`:

```csharp
namespace Tracer.WebApi.Lifecycle;

public interface ILifecycleTopicClassifier
{
    /// <summary>Returns "spawn", "ownership", "destruction", or null.</summary>
    string? Classify(string topic);
}
```

Create `src/Tracer.WebApi/Lifecycle/ConfigurableLifecycleTopicClassifier.cs`:

```csharp
namespace Tracer.WebApi.Lifecycle;

public sealed class ConfigurableLifecycleTopicClassifier : ILifecycleTopicClassifier
{
    private readonly LifecycleClassificationConfig _config;

    public ConfigurableLifecycleTopicClassifier(LifecycleClassificationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
    }

    public string? Classify(string topic)
    {
        ArgumentNullException.ThrowIfNull(topic);

        var regex = _config.Regex;

        // Regex takes precedence — checked before suffix matching
        if (regex?.Spawn is { } spawnPattern && System.Text.RegularExpressions.Regex.IsMatch(topic, spawnPattern))
            return "spawn";
        if (regex?.Ownership is { } ownerPattern && System.Text.RegularExpressions.Regex.IsMatch(topic, ownerPattern))
            return "ownership";
        if (regex?.Destruction is { } destroyPattern && System.Text.RegularExpressions.Regex.IsMatch(topic, destroyPattern))
            return "destruction";

        // Suffix matching against the last dot-segment
        var suffix = topic.Contains('.') ? topic[(topic.LastIndexOf('.') + 1)..] : topic;

        if (_config.SpawnSuffixes.Contains(suffix, StringComparer.OrdinalIgnoreCase))
            return "spawn";
        if (_config.OwnershipSuffixes.Contains(suffix, StringComparer.OrdinalIgnoreCase))
            return "ownership";
        if (_config.DestructionSuffixes.Contains(suffix, StringComparer.OrdinalIgnoreCase))
            return "destruction";

        return null;
    }
}
```

#### 5.2 — Add `LifecycleClassification` to `ObserverConfig`

In `src/Tracer.Observer/Configuration/ObserverConfig.cs`, add:

```csharp
using Tracer.WebApi.Lifecycle;

// … inside ObserverConfig class:
public LifecycleClassificationConfig LifecycleClassification { get; set; } = new();
```

#### 5.3 — `ConfigEndpoints.cs`

Create `src/Tracer.WebApi/Endpoints/ConfigEndpoints.cs`:

```csharp
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Lifecycle;

namespace Tracer.WebApi.Endpoints;

public static class ConfigEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/config/lifecycle-classification", HandleAsync).WithOpenApi();
    }

    public static Ok<LifecycleConfigDto> HandleAsync(
        [FromServices] LifecycleClassificationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return TypedResults.Ok(new LifecycleConfigDto
        {
            SpawnSuffixes = config.SpawnSuffixes,
            OwnershipSuffixes = config.OwnershipSuffixes,
            DestructionSuffixes = config.DestructionSuffixes,
            SpawnRegex = config.Regex?.Spawn,
            OwnershipRegex = config.Regex?.Ownership,
            DestructionRegex = config.Regex?.Destruction,
        });
    }
}
```

> **Note:** `LifecycleClassificationConfig` is injected directly as a singleton (not `ObserverConfig`), so this endpoint works identically in both Observer and OfflineViewer host builders.
```

#### 5.4 — `LifecycleConfigDto`

Add to `src/Tracer.WebApi/Contracts/Dto/` (create `LifecycleConfigDto.cs`):

```csharp
namespace Tracer.WebApi.Contracts.Dto;

public sealed record LifecycleConfigDto
{
    public required IReadOnlyList<string> SpawnSuffixes { get; init; }
    public required IReadOnlyList<string> OwnershipSuffixes { get; init; }
    public required IReadOnlyList<string> DestructionSuffixes { get; init; }
    public string? SpawnRegex { get; init; }
    public string? OwnershipRegex { get; init; }
    public string? DestructionRegex { get; init; }
}
```

#### 5.5 — DI Registration

**`ObserverHostBuilder.cs`** — add near the WebApi services section:
```csharp
// LifecycleClassification config + classifier
var lifecycleCfg = observerConfig.LifecycleClassification;  // property added to ObserverConfig
builder.Services.AddSingleton<LifecycleClassificationConfig>(lifecycleCfg);
builder.Services.AddSingleton<ILifecycleTopicClassifier>(
    new ConfigurableLifecycleTopicClassifier(lifecycleCfg));
```
Also add in the endpoint mapping section:
```csharp
ConfigEndpoints.Map(app);
```

**`src/Tracer.OfflineViewer/OfflineViewerHostBuilder.cs`** — same pattern. Add `LifecycleClassification` property to `OfflineViewerConfig` with `new LifecycleClassificationConfig()` as default. Register:
```csharp
var lifecycleCfg = config.LifecycleClassification;  // property added to OfflineViewerConfig
builder.Services.AddSingleton<LifecycleClassificationConfig>(lifecycleCfg);
builder.Services.AddSingleton<ILifecycleTopicClassifier>(
    new ConfigurableLifecycleTopicClassifier(lifecycleCfg));
ConfigEndpoints.Map(app);
```

> **Key:** Register `LifecycleClassificationConfig` directly as a singleton — `ConfigEndpoints.HandleAsync` injects it, not `ObserverConfig`. This makes the endpoint work identically in both hosts.

#### 5.6 — Replace Phase 7 Hardcoded Classification (Test SC-8)

The Phase 7 lifecycle classification is in the **TypeScript frontend** (`tracer-viewer/src/utils/lifecycleClassifier.ts`), not the C# backend. The "Phase 7 callers" referenced in the task spec refer to this file.

**However, this is a frontend task.** The `TRC-P8-010` scope explicitly states: _"Out of scope: Frontend `lifecycleConfigStore.ts` (frontend tasks)."_

Therefore, Test SC-8 (`HardcodedClassifier_IsReplaced`) should verify that **no C# code** in the WebApi or Observer contains hardcoded lifecycle classification string literals like `"spawn"`, `"created"`, `"ownership_changed"` etc. — not the TypeScript file. Since there was no previous C# lifecycle classification code, write SC-8 as a static code verification test that confirms `ILifecycleTopicClassifier` is used via DI (not inline string matching). If you find no hardcoded C# suffix literals to remove, write SC-8 as:

```csharp
[Fact]
public void HardcodedClassifier_IsReplaced()
{
    // The ILifecycleTopicClassifier abstraction exists and the concrete 
    // implementation does not hardcode classification outside the config defaults.
    var classifier = new ConfigurableLifecycleTopicClassifier(new LifecycleClassificationConfig());
    // Verify default classification is correct and comes from config, not scattered literals
    classifier.Classify("vehicle.spawn").Should().Be("spawn");
    classifier.Classify("vehicle.destroyed").Should().Be("destruction");
    classifier.Classify("team.ownership_changed").Should().Be("ownership");
    // When a custom config is used, built-in defaults are replaced
    var custom = new ConfigurableLifecycleTopicClassifier(new LifecycleClassificationConfig
    {
        SpawnSuffixes = new[] { "born" }
    });
    custom.Classify("thing.born").Should().Be("spawn");
    custom.Classify("thing.spawn").Should().BeNull(); // built-in no longer active
}
```

### Success Conditions (10 tests)

1. **`DefaultConfig_SpawnSuffixes`** — `Classify("vehicle.spawn") == "spawn"`, `Classify("vehicle.created") == "spawn"`, `Classify("vehicle.spawned") == "spawn"`.
2. **`DefaultConfig_OwnershipSuffixes`** — `Classify("team.ownership_changed") == "ownership"`, `Classify("unit.owner_transferred") == "ownership"`, `Classify("player.owner_changed") == "ownership"`.
3. **`DefaultConfig_DestructionSuffixes`** — `Classify("unit.destroyed") == "destruction"`, `Classify("vehicle.killed") == "destruction"`, `Classify("npc.removed") == "destruction"`, `Classify("entity.despawned") == "destruction"`.
4. **`DefaultConfig_UnknownTopic_ReturnsNull`** — `Classify("sensors.telemetry") == null`, `Classify("weapons.fire") == null`, `Classify("vehicle.update") == null`.
5. **`CustomSuffixes_ReplaceBuiltIn`** — `SpawnSuffixes = ["instantiated"]`; `Classify("thing.instantiated") == "spawn"`; `Classify("thing.spawn") == null`.
6. **`RegexOverride_TakesPrecedenceOverSuffixes`** — `Regex.Spawn = "^entity\\.new_"`; `Classify("entity.new_fighter") == "spawn"`; `Classify("vehicle.spawn") == null` (spawn regex overrides suffix for spawn category).
7. **`GET_LifecycleClassification_Returns200WithConfig`** — Call `HandleAsync` directly with a `LifecycleClassificationConfig` that has `SpawnSuffixes = ["born"]`; assert HTTP 200 and `dto.SpawnSuffixes == ["born"]`.
8. **`HardcodedClassifier_IsReplaced`** — See SC-8 implementation above.
9. **`DI_BothHosts_ResolveILifecycleTopicClassifier`** — `ILifecycleTopicClassifier` resolves from both Observer and Offline Viewer DI containers as `ConfigurableLifecycleTopicClassifier` without exception.
10. **`DefaultValues_MatchDesignSpec`** — `new LifecycleClassificationConfig()` has `SpawnSuffixes = ["spawn", "created", "spawned"]`, `OwnershipSuffixes = ["ownership_changed", "owner_transferred", "owner_changed"]`, `DestructionSuffixes = ["destroyed", "killed", "removed", "despawned"]`, `Regex == null`.

---

## 6. Build Validation

After all tasks are implemented and tests pass:

```powershell
dotnet build Tracer.sln --configuration Release --no-incremental
```

Expected: **0 warnings, 0 errors.** `TreatWarningsAsErrors=true` is global — any warning is a failure.

Common CA1062 requirement: any `public` method in `Tracer.WebApi` or `Tracer.Aggregator` that takes a reference-type parameter **must** call `ArgumentNullException.ThrowIfNull(param)` before using it.

---

## 7. Known Issues to Be Aware Of

From `DEBT-TRACKER.md`:
- **DT-028** — Full test suite (`dotnet test` without filter) hangs indefinitely. Always use `--filter`.
- **DT-035** — `RecordOpenedAsync` swallows exceptions (P3, non-blocking).
- **DT-036** — `AggregationOrchestrator` constructor proliferation (P3, non-blocking).

Do not attempt to fix DT-035 or DT-036 in this batch.

---

## 8. Developer Insights (Required in Report)

When writing `.dev/tracer/reports/BATCH-44-REPORT.md`, you **must** answer these questions:

1. **What issues were encountered during implementation?** (compilation errors, design ambiguities, unexpected API limitations, etc.)
2. **What weak points were spotted in the codebase?** (patterns that should be generalized, inconsistencies, potential bugs noticed in passing)
3. **What design decisions were made beyond the spec?** (any choices you made where the spec was silent or ambiguous)
4. **Test count summary:** How many tests were added? How many pass? List any that were skipped or modified from the spec.

---

## 9. Report Format

Write your completion report to `.dev/tracer/reports/BATCH-44-REPORT.md` using this structure:

```markdown
# BATCH-44 Completion Report

## Status: [COMPLETE / PARTIAL]

## Tasks Implemented
- [ ] TRC-P8-007 — TriggerEvalService
- [ ] TRC-P8-008 — TriggerEvalEndpoints
- [ ] TRC-P8-010 — Lifecycle Topic Configuration

## Test Results
| Suite | Tests Added | Passing | Failing | Skipped |
|-------|-------------|---------|---------|---------|
| TriggerEvalServiceTests | ? | ? | ? | ? |
| TriggerEvalEndpointsTests | ? | ? | ? | ? |
| LifecycleTopicClassifierTests | ? | ? | ? | ? |

## Build Status
`dotnet build Tracer.sln --configuration Release`: PASS / FAIL

## Developer Insights

### Issues Encountered
[answer here]

### Weak Points Spotted
[answer here]

### Design Decisions Beyond Spec
[answer here]

## Files Created
[list]

## Files Modified
[list]
```

---

## 10. Final Reminder

- Do **not** run `dotnet test` without a filter.
- Confirm all tests are **green**, not just "compiling".
- Read assertions — make sure tests check behavior, not just "doesn't throw".
- `TriggerEvaluationDto.NextEventId` must be `null` (not `"0000000000000000"`) when `EventId?.NextEventId` is null.
- `ConfigurableLifecycleTopicClassifier` — regex is checked first, then suffix. When regex is set for a category, the suffix for that category is **not** checked.
