# BATCH-13 Review

**Batch:** BATCH-13  
**Tasks:** TRC-P4-001, TRC-P4-004  
**Reviewer:** Dev Lead  
**Decision:** APPROVED ✅

---

## Review Summary

Both assemblies are clean, minimal, and correctly scoped. Build succeeds with zero warnings. All 241 tests pass. The one bug (record equality with `IReadOnlyList` properties) was caught by the test suite and fixed correctly. No over-engineering.

---

## TRC-P4-001 — Tracer.Bundle

### Code quality: PASS

**BundleManifest.cs**
- Record types with `required init` properties — correct pattern for immutable data.
- Shared static `SerializerOptions` avoids repeated `JsonSerializerOptions` allocations on the hot path — good.
- Nested records in the same file is a sensible choice for a small format module (avoids file proliferation).

**BundleSchemaV1.cs**
- `IReadOnlySet<int>` for recognized versions — extensible if a schema V2 is added later. Correct.

**BundleLayout.cs**
- All paths defined. Matches §3.1 spec exactly.

**BundleNaming.cs**
- Uses `SHA256.HashData` (static, no allocation) — best practice for .NET 8.
- `ArgumentNullException.ThrowIfNull` — correct boundary validation.
- Outputs exactly as spec requires: `_` replacement + `_` + 4 hex chars suffix.

### Test quality: PASS

- `BundleManifest_RoundTripsViaJsonSerializer`: The fix (JSON comparison instead of `.Be()`) is correct and actually a stronger test — it confirms the serialized form is byte-for-byte stable across a round-trip.
- `BundleNaming_SafeFileName_DistinctInputs_ProduceDifferentOutputs`: Tests the collision prevention correctly. `"x:y"` and `"x_y"` produce the same base form (`x_y`) but different SHA256 suffixes.
- `BundleLayout_AllPathConstants_AreNonEmpty`: Reflection-based. Robust against additions — if someone adds a path constant later, the test automatically covers it.

---

## TRC-P4-004 — MultiIntervalReader

### Code quality: PASS

**AttachedDatabaseManager.cs**
- `RandomNumberGenerator.GetBytes(3)` for 6-hex alias suffix — cryptographically random, appropriate. Using `Random` would be a security smell here.
- `EscapePath` (single-quote doubling for SQL strings) — correct SQL injection defense for the file path parameter.
- `DisposeAsync` best-effort: swallows individual errors to ensure all aliases are attempted. Correct behavior for disposal.
- Regex alias generation: `[^a-z0-9]` → `_` — correct normalization.

**MultiIntervalReader.cs**
- `internal DuckDBConnection Connection` — correctly scoped for test access only.
- `BuildEventsUnionSql` with zero attachments returns `"SELECT NULL WHERE FALSE"` — correct sentinel (not an empty string, not an exception).
- `__source_alias` column included in every SELECT — enables multi-interval provenance tracking.

### Test quality: PASS

- Temp file cleanup omitted intentionally (DuckDB holds file handles post-disposal). Comment in report documents this. Acceptable.
- `AttachAsync_SameHint_TwiceProducesDistinctAliases`: Important collision test. Correctly uses two distinct file paths with the same hint.
- `DisposeAsync_CompletesWithoutThrowing`: Tests second-dispose safety — idempotent disposal is important.
- `SourceAliasColumn_PresentInResults`: An integration-level assertion in a unit test — correctly uses the internal `Connection` property via `InternalsVisibleTo`.

---

## Acceptance Criteria Check

### TRC-P4-001
- [x] SC1: `Tracer.Bundle.csproj` references only `Tracer.Core` and `Ulid` ✅
- [x] SC2: `BundleManifest` is a `record` with all top-level fields, serializes to camelCase JSON ✅
- [x] SC3: `BundleSchemaV1.CurrentVersion == 1`; `IsRecognized(1)` true; `IsRecognized(99)` false ✅
- [x] SC4: `BundleLayout.ManifestFile == "manifest.json"` and all other path constants defined ✅
- [x] SC5: `SafeFileName("vehicle:blue:17")` → all chars in `[a-zA-Z0-9._-]` + 4-char hex suffix ✅
- [x] SC6: `SafeFileName("x:y")` ≠ `SafeFileName("x_y")` (collision prevention) ✅
- [x] SC7: All 7 `BundleManifestTests` pass ✅
- [x] SC8: All Phase 1–3 tests pass ✅

### TRC-P4-004
- [x] SC1: Alias matches `db_[a-z0-9_]+_[0-9a-f]{6}` ✅
- [x] SC2: Two files with same hint → distinct aliases ✅
- [x] SC3: `DetachAsync` removes alias from `Attachments` ✅
- [x] SC4: `DisposeAsync` detaches all without throwing ✅
- [x] SC5: `CreateAsync` returns reader with correct `Attachments.Count` ✅
- [x] SC6: `BuildEventsUnionSql` with 2 attachments → exactly 1 `"UNION ALL"` + 2 `.events` refs ✅
- [x] SC7: Zero attachments → `"SELECT NULL WHERE FALSE"` ✅
- [x] SC8: Real DuckDB test verifies `__source_alias` column in results ✅
- [x] SC11: All test classes created and passing ✅
- [x] SC12: All Phase 1–3 tests pass ✅

---

## Decision: **APPROVED**

Phase 4 foundation complete. Ready to proceed with TRC-P4-002 (Bundle Packaging) and TRC-P4-003 (Bundle Validation).
