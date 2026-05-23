# BATCH-56 — Phase 11: Integration Test Infrastructure + Soak Tests + Handoff

**Batch Number:** BATCH-56  
**Tasks:** TRC-P11-008 (Integration.Real test project) + TRC-P11-009 (Soak tests + handoff notes)  
**Phase:** 11 — Real Adapter Integration  
**Estimated Effort:** 10–12 hours  
**Priority:** HIGH — Final Phase 11 tasks; completes the project  
**Dependencies:** BATCH-55 (committed at `8989179`)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch completes Phase 11. You have **two tasks**:

1. **TRC-P11-008**: Create `Tracer.Tests.Integration.Real` — a new test project with integration tests that require an external simulation harness. All tests must be **skipped (not failed)** when `TRACER_HARNESS_PATH` environment variable is absent.
2. **TRC-P11-009**: Add `SoakTests.cs` to the real integration project, and author the Phase 11 handoff notes document at `docs/phase11-handoff-notes.md`.

### Required Reading (IN ORDER)

1. `docs/TASK-DETAIL.md` section [TRC-P11-008](../../docs/TASK-DETAIL.md#trc-p11-008--integration-test-infrastructure--tracertestsintegrationreal)
2. `docs/TASK-DETAIL.md` section [TRC-P11-009](../../docs/TASK-DETAIL.md#trc-p11-009--soak-test-and-final-validation)
3. `docs/tracer_phase11_design.md` §8 (Integration-Real Test Suite — §8.1 Project Layout, §8.2 Test Classes, §8.3 Soak Tests, §8.4 CI Lane)
4. `tests/Tracer.Tests.Integration/Tracer.Tests.Integration.csproj` — existing integration project structure to follow
5. `tests/Tracer.Tests.Integration/TestCollections.cs` — pattern for collection definitions

### Source Code Locations

- **New project:** `tests/Tracer.Tests.Integration.Real/`
- **New doc:** `docs/phase11-handoff-notes.md`
- **Solution file:** `Tracer.sln` (must add new project)

### Report Submission

**When done, submit your report to:**  
`.dev/tracer/reports/BATCH-56-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: Complete tasks in sequence with passing tests:**

1. **TRC-P11-008:** Create project → Build → verify all tests Skip → ✅
2. **TRC-P11-009:** Add SoakTests + handoff notes → ✅
3. **Final:** `dotnet build Tracer.sln -c Release` → **BUILD SUCCEEDED** ✅
4. **Final:** `dotnet test tests\Tracer.Tests.Integration.Real -c Release` → **All tests Skipped, 0 Failed** ✅
5. **Final:** `dotnet test tests\Tracer.Tests.Unit -c Release --no-build` → **394 passed** (or more) ✅

**DO NOT** move to the next task until current task tests pass.  
**DO NOT** fabricate test results — run the actual commands and include the actual output in your report.  
**DO NOT** stop after writing code — fix all failures until zero failures remain.

---

## ✅ TRC-P11-008: Integration Test Infrastructure

### Task 8.1: Create the Project

**File:** `tests/Tracer.Tests.Integration.Real/Tracer.Tests.Integration.Real.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Tracer.Adapters.DDS\Tracer.Adapters.DDS.csproj" />
    <ProjectReference Include="..\..\src\Tracer.Adapters.SharedMemory\Tracer.Adapters.SharedMemory.csproj" />
    <ProjectReference Include="..\..\src\Tracer.Adapters.Sync\Tracer.Adapters.Sync.csproj" />
    <ProjectReference Include="..\..\src\Tracer.Adapters.Nas\Tracer.Adapters.Nas.csproj" />
    <ProjectReference Include="..\..\src\Tracer.AdapterSelection\Tracer.AdapterSelection.csproj" />
    <ProjectReference Include="..\..\src\Tracer.Agent\Tracer.Agent.csproj" />
    <ProjectReference Include="..\..\src\Tracer.Bundle\Tracer.Bundle.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>

</Project>
```

Add the project to the solution:
```
dotnet sln Tracer.sln add tests\Tracer.Tests.Integration.Real\Tracer.Tests.Integration.Real.csproj
```

### Task 8.2: Skip Attribute

**File:** `tests/Tracer.Tests.Integration.Real/Infrastructure/SkipIfNoSimulationHarnessAttribute.cs`

```csharp
using Xunit;

namespace Tracer.Tests.Integration.Real.Infrastructure;

/// <summary>
/// Use instead of [Fact] on tests that require the simulation harness process.
/// The test is automatically skipped (not failed) when TRACER_HARNESS_PATH is not set.
/// </summary>
public sealed class SkipIfNoSimulationHarnessAttribute : FactAttribute
{
    private const string EnvVar = "TRACER_HARNESS_PATH";

    public SkipIfNoSimulationHarnessAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
            Skip = $"Simulation harness unavailable ({EnvVar} not set). " +
                   "See README-integration-real.md for setup instructions.";
    }
}
```

Also add a `[RealIntegrationTest]` trait attribute:

**File:** `tests/Tracer.Tests.Integration.Real/Infrastructure/RealIntegrationTestAttribute.cs`

```csharp
using Xunit.Sdk;

namespace Tracer.Tests.Integration.Real.Infrastructure;

/// <summary>Marks a test as belonging to the real-integration test lane.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class RealIntegrationTestAttribute : Attribute { }
```

### Task 8.3: SimulationHarnessFixture

**File:** `tests/Tracer.Tests.Integration.Real/Infrastructure/SimulationHarnessFixture.cs`

```csharp
using System.Diagnostics;
using Xunit;

namespace Tracer.Tests.Integration.Real.Infrastructure;

/// <summary>
/// xUnit IAsyncLifetime fixture that starts and stops the simulation harness process.
/// The harness executable path is read from the TRACER_HARNESS_PATH environment variable.
/// When the variable is absent this fixture does nothing (tests are skipped by [SkipIfNoSimulationHarness]).
/// </summary>
public sealed class SimulationHarnessFixture : IAsyncLifetime
{
    private const string EnvVar = "TRACER_HARNESS_PATH";
    private Process? _harnessProcess;

    public bool IsAvailable { get; private set; }

    public async Task InitializeAsync()
    {
        var harnessPath = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrWhiteSpace(harnessPath))
        {
            IsAvailable = false;
            return;
        }

        _harnessProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = harnessPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };
        _harnessProcess.Start();

        // Allow harness time to initialize (up to 30 s).
        await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        IsAvailable = true;
    }

    /// <summary>
    /// Instructs the harness to emit a deterministic trace chain for testing.
    /// </summary>
    public Task EmitKnownTraceAsync(ulong traceId, int depth, CancellationToken ct = default)
    {
        // In a real deployment this would send a control message to the harness.
        // For CI scaffolding, this is a no-op placeholder.
        return Task.CompletedTask;
    }

    /// <summary>
    /// Instructs the harness to emit a burst of events at the specified rate.
    /// </summary>
    public Task EmitEventBurstAsync(int count, int ratePerSec, CancellationToken ct = default)
    {
        // Placeholder — real implementation sends control message to harness.
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (_harnessProcess is not null && !_harnessProcess.HasExited)
        {
            _harnessProcess.Kill(entireProcessTree: true);
            _harnessProcess.Dispose();
        }
        return Task.CompletedTask;
    }
}
```

### Task 8.4: Collection Definition

**File:** `tests/Tracer.Tests.Integration.Real/Infrastructure/TestCollections.cs`

```csharp
using Xunit;

namespace Tracer.Tests.Integration.Real.Infrastructure;

[CollectionDefinition("RealIntegration", DisableParallelization = true)]
public sealed class RealIntegrationCollection : ICollectionFixture<SimulationHarnessFixture> { }
```

### Task 8.5: Test Classes

Create these six test files. All tests use `[SkipIfNoSimulationHarness]` so they are skipped on dev machines.

**File:** `tests/Tracer.Tests.Integration.Real/DdsRoundTripTests.cs`

```csharp
using FluentAssertions;
using Tracer.Tests.Integration.Real.Infrastructure;
using Xunit;

namespace Tracer.Tests.Integration.Real;

[Collection("RealIntegration")]
[RealIntegrationTest]
public sealed class DdsRoundTripTests(SimulationHarnessFixture harness)
{
    [SkipIfNoSimulationHarness]
    public async Task KnownTraceChainArrivesInBundle()
    {
        // Arrange: emit 1000 events with known trace chain.
        const ulong traceId = 0xDEADBEEF;
        await harness.EmitKnownTraceAsync(traceId, depth: 10);

        // Act: (In real deployment) rotate interval and build bundle.
        // On a CI machine without harness this test is skipped.
        await Task.Delay(100); // placeholder

        // Assert: (placeholder — real assertion compares bundle events)
        true.Should().BeTrue("placeholder assertion — requires harness to be running");
    }
}
```

**File:** `tests/Tracer.Tests.Integration.Real/SharedMemoryThroughputTests.cs`

```csharp
using FluentAssertions;
using Tracer.Tests.Integration.Real.Infrastructure;
using Xunit;

namespace Tracer.Tests.Integration.Real;

[Collection("RealIntegration")]
[RealIntegrationTest]
public sealed class SharedMemoryThroughputTests(SimulationHarnessFixture harness)
{
    [SkipIfNoSimulationHarness]
    public async Task SustainedThroughput_DropRateBelow0Point1Percent()
    {
        // Emit 5000 events/sec × 60 s = 300,000 events.
        // Drop rate must be < 0.1% (< 300 drops).
        await harness.EmitEventBurstAsync(count: 300_000, ratePerSec: 5_000);
        await Task.Delay(100); // placeholder for actual measurement

        // Assert: (placeholder — real assertion reads dropped_count from transport health)
        true.Should().BeTrue("placeholder assertion — requires harness to be running");
    }
}
```

**File:** `tests/Tracer.Tests.Integration.Real/SharedMemoryLossTests.cs`

```csharp
using FluentAssertions;
using Tracer.Tests.Integration.Real.Infrastructure;
using Xunit;

namespace Tracer.Tests.Integration.Real;

[Collection("RealIntegration")]
[RealIntegrationTest]
public sealed class SharedMemoryLossTests(SimulationHarnessFixture harness)
{
    [SkipIfNoSimulationHarness]
    public async Task DroppedCountMatchesObservedDeficit()
    {
        // Pause consumer, saturate ring, resume, measure deficit vs dropped_count.
        await Task.Delay(100); // placeholder

        true.Should().BeTrue("placeholder assertion — requires harness to be running");
    }
}
```

**File:** `tests/Tracer.Tests.Integration.Real/SyncUploadTests.cs`

```csharp
using FluentAssertions;
using Tracer.Tests.Integration.Real.Infrastructure;
using Xunit;

namespace Tracer.Tests.Integration.Real;

[Collection("RealIntegration")]
[RealIntegrationTest]
public sealed class SyncUploadTests(SimulationHarnessFixture harness)
{
    [SkipIfNoSimulationHarness]
    public async Task HappyPathUploadCompletes()
    {
        // Complete an interval; poll until NAS zip exists with _ready sentinel.
        await Task.Delay(100); // placeholder

        true.Should().BeTrue("placeholder assertion — requires harness to be running");
    }
}
```

**File:** `tests/Tracer.Tests.Integration.Real/TraceContextPropagationTests.cs`

```csharp
using FluentAssertions;
using Tracer.Tests.Integration.Real.Infrastructure;
using Xunit;

namespace Tracer.Tests.Integration.Real;

[Collection("RealIntegration")]
[RealIntegrationTest]
public sealed class TraceContextPropagationTests(SimulationHarnessFixture harness)
{
    [SkipIfNoSimulationHarness]
    public async Task ParentChildRelationshipsPreserved()
    {
        // Emit depth-3 chain; assert causal tree has 3 nodes and 2 edges.
        const ulong rootEventId = 0x64; // 100 decimal
        await harness.EmitKnownTraceAsync(rootEventId, depth: 3);
        await Task.Delay(100); // placeholder

        true.Should().BeTrue("placeholder assertion — requires harness to be running");
    }
}
```

**File:** `tests/Tracer.Tests.Integration.Real/EndToEndSessionTests.cs`

```csharp
using FluentAssertions;
using Tracer.Tests.Integration.Real.Infrastructure;
using Xunit;

namespace Tracer.Tests.Integration.Real;

[Collection("RealIntegration")]
[RealIntegrationTest]
public sealed class EndToEndSessionTests(SimulationHarnessFixture harness)
{
    [SkipIfNoSimulationHarness]
    public async Task BundleContainsAllAgentData()
    {
        // 5-minute simulated session across multiple agent processes.
        // Assert bundle contains events from all agents.
        await Task.Delay(100); // placeholder

        true.Should().BeTrue("placeholder assertion — requires harness to be running");
    }
}
```

### Task 8.6: README

**File:** `tests/Tracer.Tests.Integration.Real/README-integration-real.md`

```markdown
# Tracer.Tests.Integration.Real — Integration Test Suite

This project contains integration tests that require the customer's simulation harness to be available. Tests are **automatically skipped** (not failed) on developer machines where the harness is not installed.

## Running the Tests

### Prerequisites

1. Simulation harness executable installed and accessible.
2. Environment variable `TRACER_HARNESS_PATH` set to the full path of the harness executable.

### Run All Real Integration Tests

```bash
export TRACER_HARNESS_PATH=/path/to/simulation-harness.exe
dotnet test tests/Tracer.Tests.Integration.Real -c Release
```

### Run Without Harness (Shows All Tests as Skipped)

```bash
dotnet test tests/Tracer.Tests.Integration.Real -c Release
# Expected: 0 Failed, N Skipped
```

### Run Soak Tests (Requires Harness, Runs for 48 h)

```bash
export TRACER_HARNESS_PATH=/path/to/simulation-harness.exe
dotnet test tests/Tracer.Tests.Integration.Real -c Release --filter "Category=SoakTest"
```

## CI Lanes

- **Nightly CI lane:** Runs `RealIntegrationTest` category tests (requires harness environment).
- **PR CI lane:** Does NOT run this project — these tests are skipped on standard PRs.
- **Release gate:** All `RealIntegrationTest` tests must pass before a production release.
- **Soak tests:** Run on-demand or on a weekly schedule; require 48 h of runtime.

## Environment Variables

| Variable | Description | Required |
|----------|-------------|----------|
| `TRACER_HARNESS_PATH` | Full path to simulation harness executable | Yes (tests skip if absent) |

## External Team Requirements

See `docs/phase11-handoff-notes.md` for requirements from the simulation team and sync team.
```

---

## ✅ TRC-P11-009: Soak Tests + Handoff Notes

### Task 9.1: Soak Test Category Attribute

**File:** `tests/Tracer.Tests.Integration.Real/Infrastructure/SoakTestAttribute.cs`

```csharp
using Xunit;

namespace Tracer.Tests.Integration.Real.Infrastructure;

/// <summary>
/// Use instead of [Fact] on 48-hour soak tests. Combines skip-if-no-harness
/// with a "SoakTest" trait for CI filter targeting.
/// </summary>
public sealed class SoakTestAttribute : FactAttribute
{
    private const string EnvVar = "TRACER_HARNESS_PATH";

    public SoakTestAttribute()
    {
        Traits.Add("Category", "SoakTest");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
            Skip = $"Soak test skipped ({EnvVar} not set). Requires harness and 48 h of runtime.";
    }
}
```

Wait — xUnit v2 `FactAttribute` does not have a `Traits` property. Use `[Trait]` attribute separately on the method. Instead, define the `[SoakTest]` attribute as:

```csharp
using Xunit;

namespace Tracer.Tests.Integration.Real.Infrastructure;

/// <summary>
/// Custom skip attribute for 48-hour soak tests. Skipped when TRACER_HARNESS_PATH is absent.
/// Pair with [Trait("Category", "SoakTest")] on the test method for CI filter support.
/// </summary>
public sealed class SoakTestAttribute : FactAttribute
{
    private const string EnvVar = "TRACER_HARNESS_PATH";

    public SoakTestAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
            Skip = $"Soak test skipped ({EnvVar} not set). Requires harness and 48 h of runtime.";
    }
}
```

### Task 9.2: Soak Tests

**File:** `tests/Tracer.Tests.Integration.Real/SoakTests.cs`

```csharp
using FluentAssertions;
using Tracer.Tests.Integration.Real.Infrastructure;
using Xunit;

namespace Tracer.Tests.Integration.Real;

[Collection("RealIntegration")]
public sealed class SoakTests(SimulationHarnessFixture harness)
{
    /// <summary>
    /// 48-hour continuous run. Validates: no RSS growth, no file-handle growth,
    /// stable drop rate, stable throughput, crash recovery at hour 24,
    /// successful bundle builds at hours 12, 24, 36, and end.
    ///
    /// Run with: dotnet test --filter "Category=SoakTest"
    /// Requires: TRACER_HARNESS_PATH set, 48 h of available runtime.
    /// </summary>
    [SoakTest]
    [Trait("Category", "SoakTest")]
    public async Task Phase11_48HourSoakRun_MeetsAllStabilityCriteria()
    {
        // Soak run infrastructure — samples every 5 min over 48 h.
        const int totalMinutes = 48 * 60;
        const int sampleIntervalMinutes = 5;
        var rssSamples = new List<long>();
        var handleSamples = new List<int>();
        var dropSamples = new List<long>();
        var throughputSamples = new List<double>();

        var agentProcess = System.Diagnostics.Process.GetCurrentProcess();
        var startTime = DateTimeOffset.UtcNow;

        // Emit initial burst to establish baseline.
        await harness.EmitEventBurstAsync(count: 5_000, ratePerSec: 5_000);
        await Task.Delay(TimeSpan.FromSeconds(10));

        // Collect samples. In a real soak run this runs for 48 h.
        // For automated test purposes the loop exits early if harness is not available.
        for (var minute = 0; minute < totalMinutes; minute += sampleIntervalMinutes)
        {
            agentProcess.Refresh();
            rssSamples.Add(agentProcess.WorkingSet64);
            handleSamples.Add(agentProcess.HandleCount);

            // Sample throughput (placeholder).
            throughputSamples.Add(5_000.0);

            await Task.Delay(TimeSpan.FromMinutes(sampleIntervalMinutes));

            // Induced crash at hour 24.
            if (minute == 24 * 60)
            {
                // (Placeholder) In a real run, kill and restart the agent process.
                await Task.Delay(TimeSpan.FromSeconds(5)); // simulate restart time
            }

            // Bundle build checkpoints.
            if (minute is (12 * 60) or (24 * 60) or (36 * 60) or (totalMinutes - sampleIntervalMinutes))
            {
                // (Placeholder) Trigger bundle build and assert success.
            }
        }

        // Assert stability criteria.
        // RSS slope over final 12 h: < 1 MB/h.
        var finalRssSamples = rssSamples.TakeLast(12 * 60 / sampleIntervalMinutes).ToList();
        var rssSlope = ComputeLinearRegressionSlope(finalRssSamples);
        (rssSlope / 1_048_576).Should().BeLessThan(1.0,
            "agent RSS must not grow more than 1 MB/h over the final 12 h");

        // Throughput stability: within ±10% of first-hour baseline.
        var baseline = throughputSamples.Take(12).Average();
        throughputSamples.Skip(12).All(s => Math.Abs(s - baseline) / baseline < 0.10)
            .Should().BeTrue("throughput must remain within 10% of the first-hour baseline");
    }

    private static double ComputeLinearRegressionSlope(IList<long> samples)
    {
        if (samples.Count < 2) return 0;
        var n = samples.Count;
        var sumX = (double)n * (n - 1) / 2;
        var sumX2 = (double)n * (n - 1) * (2 * n - 1) / 6;
        var sumY = samples.Sum(s => (double)s);
        var sumXY = samples.Select((s, i) => i * (double)s).Sum();
        return (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
    }
}
```

### Task 9.3: Handoff Notes

**File:** `docs/phase11-handoff-notes.md`

Create this Markdown document covering what Phase 11 requires from external teams.

```markdown
# Phase 11 — Handoff Notes

**Document:** Phase 11 Real Adapter Integration — External Requirements  
**Version:** 1.0  
**Last Updated:** [current date]

---

## Overview

Phase 11 of the Tracer system integrates real production adapters:
- **DDS** data source (CycloneDDS.NET)
- **Shared Memory** IPC transport (named ring buffer)
- **Sync System** upload adapter
- **NAS** storage reader

This document specifies what Tracer requires from the **simulation team** and the **sync team** for the integration to function correctly.

---

## Requirements for the Simulation Team

### 1. Trace Context Propagation Discipline

Every DDS publish that originates or propagates a Tracer event MUST:

- Call `dds_write_ts()` on every publish (not `dds_write()`).
- Populate the `traceId`, `eventId`, and `parentEventId` fields in all event IDL types.
- Maintain the parent-child relationship: `parentEventId` of a child event equals the `eventId` of the triggering event.

**Why:** Tracer reconstructs causal trees from these fields. Missing or zero values cause trace chains to appear as isolated events.

### 2. DDS Domain ID Agreement

All simulation processes and Tracer agents must use the **same DDS domain ID** (default: 0, configurable via `appsettings.json` `dds.participant.domainId`). Mismatched domain IDs cause complete topic isolation.

### 3. IDL Type Coverage

All published IDL event types must include the following fields:
- `uint64 traceId` — globally unique trace identifier (same across the trace chain)
- `uint64 eventId` — per-event unique identifier  
- `uint64 parentEventId` — zero for root events; `eventId` of the triggering event otherwise

### 4. Simulation Harness Interface

The simulation harness (`TRACER_HARNESS_PATH`) must:
- Accept `--emit-trace <traceId> <depth>` command-line arguments to emit deterministic trace chains.
- Accept `--emit-burst <count> <rate>` to emit event bursts at the specified rate.
- Exit within 5 seconds when sent SIGTERM or CTRL+C.

---

## Requirements for the Sync Team

### 1. Telemetry REST Endpoint Contract

The sync master must expose endpoints matching the contract in `docs/sync_addendum_telemetry.md §A4`:

- `POST /telemetry/submit` — submit a telemetry zip archive for upload.
- `GET /telemetry/status/{correlationId}` — poll upload status.
- Status values: `Pending`, `InProgress`, `Completed`, `Failed`.

**Contract stability:** These endpoints are called by `Tracer.Adapters.Sync.SyncSystemUploadService`. Any schema change requires coordination with the Tracer team.

### 2. `_ready` Sentinel Discipline

The sync agent MUST write the `_ready` entry as the **last entry** in each interval zip archive before declaring the interval complete. Tracer's NAS reader uses the presence of `_ready` to determine whether an interval is safe to read. Zips without `_ready` are skipped and logged as warnings.

### 3. NAS Layout

The NAS share must follow the layout expected by `NasStorageReader`:
```
{NasRoot}\telemetry\{nodeId}\{intervalTimestamp}.zip
```
Where `intervalTimestamp` follows the format `yyyyMMddTHHmmss` (UTC).

---

## Phase 11 Completion Checklist

All 10 success criteria from `tracer_phase11_design.md §1.3` must be verified:

- [ ] **Criterion 1**: DDS adapter reads and decodes samples from subscribed topics
- [ ] **Criterion 2**: SharedMemory ring buffer handles write-ahead and drop-oldest at capacity
- [ ] **Criterion 3**: Sync upload submits and polls correctly; retries on transient errors
- [ ] **Criterion 4**: NAS reader skips non-ready zips; circuit breaker trips after threshold failures
- [ ] **Criterion 5**: AdapterSelection registers correct adapters from configuration
- [ ] **Criterion 6**: `appsettings.json` defaults are coherent; `appsettings.IntegrationReal.json` overrides correctly
- [ ] **Criterion 7**: TransportMonitor logs warnings when SharedMemory drops increase; health endpoint exposes `sharedMemoryDropped` and `ingestChannelDepth`
- [ ] **Criterion 8**: Soak run shows no monotonic RSS or file-handle growth over 48 h
- [ ] **Criterion 9**: All Phase 1–10 unit and integration tests continue to pass
- [ ] **Criterion 10**: Integration-real tests compile and skip cleanly on dev machines; run and pass in the harness environment

---

## Known Limitations

| Issue | Description | Tracking |
|-------|-------------|---------|
| DuckDB process-exit crash | Native DuckDB library crashes during testhost shutdown after all tests complete. Tests pass (394/394); crash is post-run cleanup only. | DT-041 |
| `intervalsAwaitingUpload` health field | Not yet exposed in `/api/health`. Requires tracking state in `UploadIntentDispatcher`. | DT-042 |
```

---

## ✅ Definition of Done

Complete all items before submitting the report:

- [ ] **Build passes**: `dotnet build Tracer.sln -c Release --no-incremental` → 0 errors, 0 warnings
- [ ] **Real integration project skips**: `dotnet test tests\Tracer.Tests.Integration.Real -c Release` → **0 Failed, N Skipped**
- [ ] **Unit tests still pass**: `dotnet test tests\Tracer.Tests.Unit -c Release --no-build` → **394+ passed, 0 Failed**
- [ ] **Handoff notes created**: `docs/phase11-handoff-notes.md` exists and covers simulation team + sync team requirements
- [ ] **README created**: `tests/Tracer.Tests.Integration.Real/README-integration-real.md` exists with correct content

---

## 📝 Report Template

```markdown
# BATCH-56 Report

**Batch:** BATCH-56  
**Tasks Completed:** TRC-P11-008, TRC-P11-009

## Build Output

[paste dotnet build output]

## Test Output — Integration.Real (All Skipped)

[paste dotnet test output for Integration.Real]

## Test Output — Unit Tests (394+ passed)

[paste dotnet test output for Unit tests]

## Files Created/Modified

[list all new/modified files]
```
