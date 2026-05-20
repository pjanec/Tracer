# BATCH-13 Report

**Batch:** BATCH-13  
**Tasks:** TRC-P4-001, TRC-P4-004  
**Date:** 2026-05-21  
**Status:** COMPLETE

---

## 1. Summary

BATCH-13 implements the two foundational Phase 4 assemblies: `Tracer.Bundle` (pure data-model, no I/O) and `Tracer.Storage.DuckDB.MultiInterval` (cross-interval DuckDB querying). Both assemblies build with zero warnings under `TreatWarningsAsErrors`. 17 new unit tests added; total is 241 (200 unit + 41 integration), all passing. One issue encountered and resolved: `BundleManifest_RoundTripsViaJsonSerializer` initially used `.Be()` (record equality), which fails for `IReadOnlyList<T>` properties because records compare references, not contents. Fixed by comparing JSON re-serializations instead.

---

## 2. TRC-P4-001 — Bundle Format

### New files

| File | Description |
|---|---|
| `src/Tracer.Bundle/Tracer.Bundle.csproj` | New project (refs Tracer.Core + Ulid) |
| `src/Tracer.Bundle/Format/BundleManifest.cs` | Root record + nested types (BundleWriterInfo, BundleTimeRange, BundleSessionContext, BundleStatistics, BundleFileEntry) |
| `src/Tracer.Bundle/Format/BundleSchemaV1.cs` | CurrentVersion=1, IsRecognized(int) |
| `src/Tracer.Bundle/Format/BundleLayout.cs` | Path constants: ManifestFile, ScenarioFile, TopologyFile, SourceIntervalsFile, EventsDb, SlowStateDb, ChecksumsFile, FastStateDirectory, AnnotationsDirectory, AnnotationsKeepFile |
| `src/Tracer.Bundle/Format/BundleNaming.cs` | SafeFileName() — replaces hostile chars with '_' + 4-char SHA256 hex suffix |
| `tests/Tracer.Tests.Unit/Bundle/BundleManifestTests.cs` | 7 tests |

### Design decisions

- `BundleManifest.SerializerOptions` is a static shared instance with `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` to avoid repeated allocations.
- `BundleNaming.SafeFileName` uses `SHA256.HashData` (no allocation of a `SHA256` instance) — the static method is available in .NET 8.

### Bug found and fixed

`BundleManifest_RoundTripsViaJsonSerializer` initially asserted `restored.Should().Be(original)`. C# records use `EqualityComparer<T>.Default` for each property; for `IReadOnlyList<string>`, this is reference equality — so two lists with the same content are not equal after deserialization. Fixed by asserting `json2.Should().Be(json1)` (JSON round-trip produces identical JSON output).

### Test methods (7, all pass)

1. `BundleManifest_RoundTripsViaJsonSerializer`
2. `BundleManifest_CamelCaseJson_ContainsBundleIdKey`
3. `BundleSchemaV1_CurrentVersionIsOne`
4. `BundleSchemaV1_IsRecognized_TrueForOne_FalseForNinetyNine`
5. `BundleNaming_SafeFileName_ReplacesColons`
6. `BundleNaming_SafeFileName_DistinctInputs_ProduceDifferentOutputs`
7. `BundleLayout_AllPathConstants_AreNonEmpty`

---

## 3. TRC-P4-004 — MultiIntervalReader

### New files

| File | Description |
|---|---|
| `src/Tracer.Storage.DuckDB.MultiInterval/Tracer.Storage.DuckDB.MultiInterval.csproj` | New project (refs Tracer.Core + Tracer.Storage.DuckDB + DuckDB.NET) |
| `src/Tracer.Storage.DuckDB.MultiInterval/IntervalDbFile.cs` | Value record: FilePath + AliasHint |
| `src/Tracer.Storage.DuckDB.MultiInterval/AttachedDatabaseManager.cs` | Attaches/detaches DuckDB files; generates `db_{hint}_{6hex}` aliases |
| `src/Tracer.Storage.DuckDB.MultiInterval/MultiIntervalReader.cs` | Static factory CreateAsync(); BuildEventsUnionSql(); exposes internal Connection for tests |
| `tests/Tracer.Tests.Unit/MultiInterval/AttachedDatabaseManagerTests.cs` | 5 tests |
| `tests/Tracer.Tests.Unit/MultiInterval/MultiIntervalReaderTests.cs` | 5 tests |

### Design decisions

- `AttachedDatabaseManager` uses `RandomNumberGenerator.GetBytes(3)` for the 6-hex suffix (cryptographically random, not `Random`).
- `MultiIntervalReader.Connection` is `internal` (not public) — exposed only for tests via `InternalsVisibleTo`.
- Tests do not delete temp DuckDB files: DuckDB holds file handles even after connection disposal. Temp files accumulate in `%TEMP%` and are cleaned up by OS. This is standard practice for DuckDB integration tests.
- `BuildEventsUnionSql` includes `'alias' AS __source_alias` in every SELECT so callers can trace which interval each row came from.

### Test methods (10, all pass)

**AttachedDatabaseManagerTests** (5):
1. `AttachAsync_ProducesAliasMatchingPattern`
2. `AttachAsync_SameHint_TwiceProducesDistinctAliases`
3. `DetachAsync_RemovesAliasFromAttachments`
4. `DisposeAsync_DetachesAllAttachments`
5. `AliasGeneration_ProducesValidSqlIdentifier`

**MultiIntervalReaderTests** (5):
1. `CreateWithZeroFiles_BuildEventsUnionSql_ReturnsEmptySentinel`
2. `CreateWithOneFile_SqlReferencesAlias`
3. `CreateWithTwoFiles_SqlContainsOneUnionAll`
4. `SourceAliasColumn_PresentInResults`
5. `DisposeAsync_CompletesWithoutThrowing`

---

## 4. Test Results

```
Build succeeded. 0 Warning(s), 0 Error(s)

Tests:
  Integration:  41 passed, 0 failed
  Unit:        200 passed, 0 failed
  Total:       241 passed (was 224 before BATCH-13)
  New tests:    17 (7 Bundle + 5 AttachedDatabaseManager + 5 MultiIntervalReader)
```

---

## 5. Suggested Commit Message

```
feat(bundle,multi-interval): Bundle Format and MultiIntervalReader assemblies (TRC-P4-001, TRC-P4-004)

TRC-P4-001 — Tracer.Bundle:
- Add src/Tracer.Bundle/ assembly (BundleManifest, BundleLayout, BundleSchemaV1, BundleNaming)
- Add Directory.Packages.props entries: System.CommandLine, System.IO.Compression.ZipFile, Ulid
- Add tests/Tracer.Tests.Unit/Bundle/BundleManifestTests.cs (7 tests)

TRC-P4-004 — Tracer.Storage.DuckDB.MultiInterval:
- Add src/Tracer.Storage.DuckDB.MultiInterval/ assembly
  (IntervalDbFile, AttachedDatabaseManager, MultiIntervalReader)
- Add tests/Tracer.Tests.Unit/MultiInterval/AttachedDatabaseManagerTests.cs (5 tests)
- Add tests/Tracer.Tests.Unit/MultiInterval/MultiIntervalReaderTests.cs (5 tests)

Totals: 241 tests (200 unit + 41 integration) — 0 failures. Build: 0 warnings.
```
