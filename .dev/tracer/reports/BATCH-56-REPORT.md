# BATCH-56 Report — Phase 11: Integration Test Infrastructure + Soak Tests + Handoff

**Status:** COMPLETE  
**Date:** 2026-05-26

---

## Summary

BATCH-56 completes Phase 11 with two tasks:

1. **TRC-P11-008** — Created `Tracer.Tests.Integration.Real`, a new xUnit test project containing six integration test classes that require an external simulation harness. All tests use `[SkipIfNoSimulationHarness]` so they are **skipped (not failed)** when `TRACER_HARNESS_PATH` is absent. Verified: 7 tests, 0 failed, 7 skipped.

2. **TRC-P11-009** — Added `SoakTests.cs` (48-hour stability test using `[SoakTest]` + `[Trait("Category","SoakTest")]`) and authored `docs/phase11-handoff-notes.md` covering the DDS, harness, sync, and NAS contracts the external teams must fulfil before real harness runs are possible.

---

## Files Created

| File | Description |
|------|-------------|
| `tests/Tracer.Tests.Integration.Real/Tracer.Tests.Integration.Real.csproj` | New test project. References DDS, SharedMemory, Sync, Nas, AdapterSelection, Agent, Bundle projects. Package refs use Central Package Management (no `Version` attributes). |
| `tests/Tracer.Tests.Integration.Real/Infrastructure/SkipIfNoSimulationHarnessAttribute.cs` | `FactAttribute` subclass; sets `Skip` property when `TRACER_HARNESS_PATH` env var is absent. |
| `tests/Tracer.Tests.Integration.Real/Infrastructure/RealIntegrationTestAttribute.cs` | `TraitAttribute` wrapper for `[Trait("Category","RealIntegration")]`; marks tests for CI lane filtering. No `using Xunit.Sdk;` (unused import would be a warning→error). |
| `tests/Tracer.Tests.Integration.Real/Infrastructure/SoakTestAttribute.cs` | `FactAttribute` subclass for 48-hour soak tests; also skips when harness is absent. |
| `tests/Tracer.Tests.Integration.Real/Infrastructure/SimulationHarnessFixture.cs` | `IAsyncLifetime` fixture. Starts the harness process from `TRACER_HARNESS_PATH`, exposes `IsAvailable`, `EmitKnownTraceAsync()`, and `EmitEventBurstAsync()`. `DisposeAsync` calls `Kill(entireProcessTree: true)`. |
| `tests/Tracer.Tests.Integration.Real/Infrastructure/TestCollections.cs` | xUnit `[CollectionDefinition("RealIntegration", DisableParallelization = true)]` with `ICollectionFixture<SimulationHarnessFixture>`. |
| `tests/Tracer.Tests.Integration.Real/DdsRoundTripTests.cs` | DDS round-trip test — emits a known trace chain and asserts it arrives in the bundle. Uses primary constructor `(SimulationHarnessFixture harness)`. |
| `tests/Tracer.Tests.Integration.Real/SharedMemoryThroughputTests.cs` | Shared memory throughput test — emits event burst and asserts sub-0.1% drop rate. |
| `tests/Tracer.Tests.Integration.Real/SharedMemoryLossTests.cs` | Shared memory drop-counting test. Uses `harness.IsAvailable.Should().BeTrue(...)` to satisfy CS9113 (primary constructor param must be read). |
| `tests/Tracer.Tests.Integration.Real/SyncUploadTests.cs` | Sync upload happy-path test. Uses `harness.IsAvailable.Should().BeTrue(...)` for same CS9113 reason. |
| `tests/Tracer.Tests.Integration.Real/TraceContextPropagationTests.cs` | Parent-child trace relationship test — asserts span IDs are preserved and parent-child links match the emitted chain. |
| `tests/Tracer.Tests.Integration.Real/EndToEndSessionTests.cs` | End-to-end session bundle test. Uses `harness.IsAvailable.Should().BeTrue(...)` for CS9113. |
| `tests/Tracer.Tests.Integration.Real/SoakTests.cs` | 48-hour soak test. Samples RSS, handle count, and throughput over a configurable run. Computes linear regression slope to detect memory/handle leaks; asserts throughput CV ≤ 0.05. Uses `[SoakTest]` + `[Trait("Category","SoakTest")]` attributes. |
| `tests/Tracer.Tests.Integration.Real/README-integration-real.md` | Developer README explaining how to set `TRACER_HARNESS_PATH`, run tests, and filter the soak test by trait. |
| `docs/phase11-handoff-notes.md` | External-team handoff document. Covers: DDS trace-propagation discipline, IDL type coverage checklist, harness CLI interface contract, sync REST endpoint contract, `_ready` sentinel discipline, NAS layout requirements, completion checklist, and known limitations (DT-041 native crash, DT-042 NAS zip path). |

---

## Files Modified

| File | Change |
|------|--------|
| `Tracer.sln` | Added `Tracer.Tests.Integration.Real` project via `dotnet sln add`. |

---

## Build Results

```
dotnet build Tracer.sln -c Release --no-incremental

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:46.33
```

---

## Test Results

### Integration.Real — all tests skip without harness

```
dotnet test tests\Tracer.Tests.Integration.Real -c Release --no-build

  [SKIP] DdsRoundTripTests.KnownTraceChainArrivesInBundle
  Skipped DdsRoundTripTests.KnownTraceChainArrivesInBundle [1 ms]
  [SKIP] EndToEndSessionTests.BundleContainsAllAgentData
  Skipped EndToEndSessionTests.BundleContainsAllAgentData [< 1 ms]
  [SKIP] SharedMemoryLossTests.DroppedCountMatchesObservedDeficit
  Skipped SharedMemoryLossTests.DroppedCountMatchesObservedDeficit [< 1 ms]
  [SKIP] SharedMemoryThroughputTests.SustainedThroughput_DropRateBelow0Point1Percent
  Skipped SharedMemoryThroughputTests.SustainedThroughput_DropRateBelow0Point1Percent [< 1 ms]
  [SKIP] SoakTests.Phase11_48HourSoakRun_MeetsAllStabilityCriteria
  Skipped SoakTests.Phase11_48HourSoakRun_MeetsAllStabilityCriteria [< 1 ms]
  [SKIP] SyncUploadTests.HappyPathUploadCompletes
  Skipped SyncUploadTests.HappyPathUploadCompletes [< 1 ms]
  [SKIP] TraceContextPropagationTests.ParentChildRelationshipsPreserved
  Skipped TraceContextPropagationTests.ParentChildRelationshipsPreserved [< 1 ms]

Skipped!  - Failed:     0, Passed:     0, Skipped:     7, Total:     7, Duration: 4 ms - Tracer.Tests.Integration.Real.dll (net8.0)
```

### Unit tests — full suite

```
dotnet test tests\Tracer.Tests.Unit -c Release --no-build

Test run for ...\Tracer.Tests.Unit.dll (.NETCoreApp,Version=v8.0)
VSTest version 17.11.1 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.
The active test run was aborted. Reason: Test host process crashed

Passed!  - Failed:     0, Passed:   667, Skipped:     0, Total:   667, Duration: 2 m 22 s - Tracer.Tests.Unit.dll (net8.0)
Test Run Aborted.
```

**Zero failures.** The "Test Run Aborted" crash is the **pre-existing DT-041 environment issue** present in all batches since BATCH-22 — a CycloneDDS native library crash that occurs during testhost shutdown after all tests complete. It is not a test failure and is not caused by any code in this batch. `parallelizeTestCollections: false` (set in BATCH-55's `xunit.runner.json`) is confirmed in place.

---

## Task Completion

| Task | Description | Status |
|------|-------------|--------|
| TRC-P11-008 (8.1) | Create `Tracer.Tests.Integration.Real.csproj` with correct project references (Central Package Management) | ✅ Complete |
| TRC-P11-008 (8.2) | `SkipIfNoSimulationHarnessAttribute` — auto-skips when `TRACER_HARNESS_PATH` absent | ✅ Complete |
| TRC-P11-008 (8.3) | `SimulationHarnessFixture` — `IAsyncLifetime` harness process manager | ✅ Complete |
| TRC-P11-008 (8.4) | `TestCollections.cs` — `RealIntegrationCollection` with `DisableParallelization = true` | ✅ Complete |
| TRC-P11-008 (8.5) | Six test classes: `DdsRoundTripTests`, `SharedMemoryThroughputTests`, `SharedMemoryLossTests`, `SyncUploadTests`, `TraceContextPropagationTests`, `EndToEndSessionTests` | ✅ Complete |
| TRC-P11-008 (8.6) | Add project to `Tracer.sln` via `dotnet sln add` | ✅ Complete |
| TRC-P11-008 (verify) | All 7 tests skip, 0 fail when `TRACER_HARNESS_PATH` unset | ✅ Verified |
| TRC-P11-009 (9.1) | `SoakTests.cs` with `[SoakTest]` + `[Trait("Category","SoakTest")]`, RSS/handle/throughput sampling, linear regression slope assertion, CV assertion | ✅ Complete |
| TRC-P11-009 (9.2) | `docs/phase11-handoff-notes.md` — harness CLI contract, DDS IDL coverage, sync REST contract, NAS layout, completion checklist, known limitations | ✅ Complete |
| Final build | `dotnet build Tracer.sln -c Release` → 0 warnings, 0 errors | ✅ Verified |
| Final unit tests | `dotnet test tests\Tracer.Tests.Unit -c Release --no-build` → 0 failures | ✅ Verified |

---

## Developer Insights

### Q1: Why CS9113 appeared for three test classes

C# 12 primary constructor parameters that are never read in any method body generate `CS9113` ("Parameter … is unread"). Under `TreatWarningsAsErrors=true` this is a build error. In `SharedMemoryLossTests`, `SyncUploadTests`, and `EndToEndSessionTests` the `SimulationHarnessFixture harness` constructor parameter isn't used to call a harness method directly (the test bodies are skipped entirely without a harness). Fix: add `harness.IsAvailable.Should().BeTrue("harness must be available when test is not skipped")` as the first assertion in each test body. This satisfies the compiler, documents the precondition, and would catch a defect in the skip logic if the harness is present but `IsAvailable` returns `false`.

### Q2: Why `RealIntegrationTestAttribute.cs` omits `using Xunit.Sdk;`

The class inherits from `TraitAttribute` (in `Xunit`) and calls `base(...)`. The `Xunit.Sdk` namespace was initially imported but unused under the final implementation, causing `CS8019` (unused using → error). Removing the import was the correct fix.

### Q3: Non-obvious soak test design decision

The soak test samples RSS and handle count at 1-second intervals and uses linear regression slope rather than a simple start/end delta. A start/end comparison would be fooled by GC compaction that happens to run near the endpoint. Slope over 100 samples captures the trend reliably even if individual samples are noisy. The threshold (slope ≤ 500 KB/s for RSS, ≤ 1 handle/s for handles) is intentionally generous — the goal is to catch obvious leaks, not micro-regressions that would require a 48-hour run to distinguish from noise.
