# BATCH-54 — Phase 11 Part B: Test Corrections + Adapter Selection + Configuration

**Batch Number:** BATCH-54  
**Tasks:** Corrective Task 0 (BATCH-53 P1 test fixes) + TRC-P11-005 + TRC-P11-006  
**Phase:** 11 — Real Adapter Integration  
**Estimated Effort:** 14–16 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-53 (committed — Phase 11 Part A adapter code in place)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch has two parts: fix critical test gaps found in BATCH-53 review, then implement
the adapter selection assembly and configuration additions that wire the real adapters into
the host DI container.

### Required Reading (IN ORDER)

1. `docs/tracer_phase11_design.md` §7 (Adapter Selection, §7.1–§7.4) — primary design reference for TRC-P11-005
2. `docs/tracer_phase11_design.md` §3.8, §4 Config, §5.5, §6.5 — config POCOs for each adapter
3. `docs/TASK-DETAIL.md` sections [TRC-P11-005](./TASK-DETAIL.md#trc-p11-005--traceradapterselection-assembly--adapter-registry-and-di) and [TRC-P11-006](./TASK-DETAIL.md#trc-p11-006--configuration-additions--appsetingsjson-adapter-sections)
4. `.dev/tracer/reviews/BATCH-53-REVIEW.md` — understand exactly which tests must be fixed
5. `src/Tracer.Agent/Program.cs` (or the DI wiring entry point) — understand the host builder pattern
6. `src/Tracer.Aggregator/Program.cs` — understand aggregator host builder pattern

### Source Code Location

- **Corrective fixes:** `tests/Tracer.Tests.Unit/Adapters/` (existing test files)
- **New assembly:** `src/Tracer.AdapterSelection/` (create new)
- **Config additions:** `src/Tracer.Agent/appsettings.json`, `src/Tracer.Aggregator/appsettings.json`
- **Test project:** `tests/Tracer.Tests.Unit/AdapterSelection/` (new folder)
- **Solution file:** `Tracer.sln` (add new project)

### Report Submission

**When done, submit your report to:**  
`.dev/tracer/reports/BATCH-54-REPORT.md`

**If you have questions, create:**  
`.dev/tracer/questions/BATCH-54-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Corrective Task 0:** Fix BATCH-53 P1 tests → **ALL 234+ tests pass** ✅
2. **TRC-P11-005:** Implement AdapterSelection → Write tests → **ALL tests pass** ✅
3. **TRC-P11-006:** Add appsettings configs → Verify DI wiring works → **ALL tests pass** ✅

**DO NOT** move to the next task until current task tests pass.  
**DO NOT** ask for permission to run tests or fix failures — just do it until all pass.  
**DO NOT** stop after writing the code — run `dotnet test tests\Tracer.Tests.Unit -c Release --no-build` to verify.

---

## ✅ Corrective Task 0 — Fix BATCH-53 P1 Test Gaps

**Based on:** `.dev/tracer/reviews/BATCH-53-REVIEW.md` P1-A through P1-D

### Fix P1-A: `DdsDiagnosticDataSourceTests` — add drop-oldest test

**File:** `tests/Tracer.Tests.Unit/Adapters/DDS/DdsDiagnosticDataSourceTests.cs`

Add a test using `Microsoft.Extensions.Logging.Abstractions.FakeLogger<T>` (or a custom `ILogger` capture pattern) that:

1. Builds a `DdsDiagnosticDataSource` with `IngestBufferSize = 5`
2. Injects a `FakeSubscriberFactory` that synchronously fires **10 samples** when `Create` is called
3. Starts `ReadAsync` with a short `CancellationToken` timeout (e.g., 2 seconds)
4. Collects all yielded records
5. Asserts: `records.Count <= 5` (channel capacity was enforced)
6. Asserts: at least one `LogWarning` was emitted containing the topic name

The key: this test verifies the critical safety property — the DDS callback thread is never blocked, and the channel's `DropOldest` policy actually fires.

> **Tip on capturing logs**: use `Microsoft.Extensions.Logging.Testing.FakeLogger` (from `Microsoft.Extensions.Logging.Testing` package) OR create a simple `CapturingLogger<T>` that collects `LogLevel.Warning` messages into a `List<string>`. Add the package if it's not in `Directory.Packages.props`.
>
> Alternatively, check if the project already uses `NullLogger<T>` — if a fancier logger package isn't available, create a thin `CapturingLogger<T> : ILogger<T>` inner class in the test that stores messages. The critical assertion is that a warning WAS emitted.

### Fix P1-B: `SharedMemoryRingBufferTests` — fix trivial assertion

**File:** `tests/Tracer.Tests.Unit/Adapters/SharedMemory/SharedMemoryRingBufferTests.cs`

Change:
```csharp
// BEFORE (trivially true):
buffer.GetDroppedCount().Should().BeGreaterThanOrEqualTo(0);

// AFTER (actually verifies drops occurred):
buffer.GetDroppedCount().Should().BeGreaterThan(0);
```

This is a one-line fix, but it's P1 because the test name says "Positive" and the assertion doesn't enforce that.

### Fix P1-C: `SharedMemoryTransportTests` — add field-level assertions to round-trip test

**File:** `tests/Tracer.Tests.Unit/Adapters/SharedMemory/SharedMemoryTransportTests.cs`

Update `ReadAsync_RecordsWrittenByWriter_AreYielded` to also validate fields:

```csharp
// After: received.Should().HaveCount(3);
// Add:
var received3 = received.OfType<EventRecord>().ToList();
received3.Should().HaveCount(3);
received3[0].SequenceNumber.Should().Be(1UL);
received3[1].SequenceNumber.Should().Be(2UL);
received3[2].SequenceNumber.Should().Be(3UL);
// Also check a non-trivial field survives codec round-trip:
received3[0].Topic.Value.Should().Be("topic.event");
```

This validates that the codec encode/decode preserves field values across the transport, not just that 3 records arrive.

### Fix P1-D: `SyncSystemUploadServiceTests` — add 4 missing tests

**File:** `tests/Tracer.Tests.Unit/Adapters/Sync/SyncSystemUploadServiceTests.cs`

Add the following tests. The `FakeHttpMessageHandler` already exists in the file — extend it to capture the request body for SC1:

**SC1 — Request body validation:**
```csharp
[Fact]
public async Task RequestUploadAsync_SendsCorrectBodyToSyncMaster()
{
    // Capture the request body the handler receives
    // Assert POST /api/telemetry was called with nodeId = "blue-cmd-01",
    // intervalTimestamp = "20260519T140000Z", files array correct
    // ...
}
```

Extend `FakeHttpMessageHandler` to expose a `CapturedRequests` list and capture request content via `await request.Content.ReadAsStringAsync()`.

**SC3 — WaitForCompletionAsync polls correct number of times:**
```csharp
[Fact]
public async Task WaitForCompletionAsync_PollingUntilComplete_CallsGetStatusExpectedTimes()
{
    // handler: register returns OK, then status returns InProgress, InProgress, Completed
    // Assert: after WaitForCompletionAsync returns UploadStatus.Complete,
    // the handler was called exactly 3 times for status (not more, not less)
}
```

Track call count in `FakeHttpMessageHandler`.

**SC5 — Cancellation during poll:**
```csharp
[Fact]
public async Task WaitForCompletionAsync_CancelledDuringPoll_ThrowsOperationCanceledException()
{
    // handler: register returns OK; status poll hangs 500ms
    // cancel after 100ms
    // Assert: OperationCanceledException thrown; no further HTTP calls
}
```

Use a `TaskCompletionSource` in the handler to delay status responses until the test cancels.

**SC6 — Retry on 503:**
```csharp
[Fact]
public async Task RequestUploadAsync_Returns503Twice_Then201_RetriesAndSucceeds()
{
    // Build with retryAttempts = 3
    // handler: 503, 503, 201 with { intentId = ... }
    // Assert: RequestUploadAsync returns successfully and handler was called 3 times
}
```

> **Note on the current intentId design**: the service stores `{nodeId}|{intervalTimestamp}` rather than the server's intentId. This is the actual behavior. SC1's test should assert that the POST body is correct; the intentId returned by `RequestUploadAsync` will contain `nodeId` and `intervalTimestamp` as documented in `BATCH-53-REVIEW.md`. Adjust test assertions to match the actual (not the spec's original) behavior.

---

## ✅ TRC-P11-005 — `Tracer.AdapterSelection` Assembly

**Design reference:** `docs/tracer_phase11_design.md` §7 (§7.1 through §7.4)  
**Task details:** `docs/TASK-DETAIL.md#trc-p11-005--traceradapterselection-assembly--adapter-registry-and-di`

### Project Setup

Create `src/Tracer.AdapterSelection/Tracer.AdapterSelection.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Options" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Http" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Tracer.Core\Tracer.Core.csproj" />
    <ProjectReference Include="..\Tracer.Adapters.Mock\Tracer.Adapters.Mock.csproj" />
    <ProjectReference Include="..\Tracer.Adapters.DDS\Tracer.Adapters.DDS.csproj" />
    <ProjectReference Include="..\Tracer.Adapters.SharedMemory\Tracer.Adapters.SharedMemory.csproj" />
    <ProjectReference Include="..\Tracer.Adapters.Sync\Tracer.Adapters.Sync.csproj" />
    <ProjectReference Include="..\Tracer.Adapters.Nas\Tracer.Adapters.Nas.csproj" />
  </ItemGroup>
</Project>
```

Add to solution: `dotnet sln d:\Work\Tracer\Tracer.sln add src/Tracer.AdapterSelection/Tracer.AdapterSelection.csproj`

Also add a `ProjectReference` to `Tracer.AdapterSelection` in both `src/Tracer.Agent/Tracer.Agent.csproj` and `src/Tracer.Aggregator/Tracer.Aggregator.csproj`.

### Files to Create

#### `src/Tracer.AdapterSelection/AdapterRegistry.cs`

Implements the adapter selection logic from design §7.2. Key rules:

- Reads `adapters:dataSource`, `adapters:transport`, `adapters:upload`, `adapters:storageReader`, `adapters:clock` from `IConfiguration`
- Each slot has a `switch` expression over the allowed values (see §7.1 table)
- Unknown value → `InvalidOperationException` with a clear message: `"Unknown {slot} adapter value: '{value}'. Supported values: {list}"`
- Defaults: `dataSource = "mock"`, `transport = "in-process"`, `upload = "local-file-system"`, `storageReader = "local-file-system"`, `clock = "system"`

Supported values per slot:

| Slot | Values | Implementation |
|------|--------|---------------|
| `dataSource` | `"mock"` | `MockDataSource` |
| `dataSource` | `"dds"` | `DdsDiagnosticDataSource`; bind `DdsAdapterConfig` from `"dds"` section |
| `transport` | `"in-process"` | `InProcessChannelTransport` (from Mock adapter) |
| `transport` | `"shared-memory"` | `SharedMemoryTransport`; bind `SharedMemoryConfig` from `"sharedMemory"` section |
| `upload` | `"local-file-system"` | `LocalFileSystemUploadService` (from Mock adapter) |
| `upload` | `"sync"` | `SyncSystemUploadService`; bind `SyncAdapterConfig` from `"sync"` section; add named `HttpClient` |
| `storageReader` | `"local-file-system"` | `LocalFileSystemStorageReader` (from Mock adapter) |
| `storageReader` | `"nas"` | `NasStorageReader`; bind `NasAdapterConfig` from `"nas"` section |
| `clock` | `"system"` | `SystemClock` |
| `clock` | `"simulated"` | `SimulatedClock` |

Survey the existing Mock adapter to find the exact class names for InProcessChannelTransport, LocalFileSystemUploadService, and LocalFileSystemStorageReader.

For `"dds"` data source: the `DdsTopicRegistry` must be populated from `DdsAdapterConfig.Topics`. Each topic's `SampleTypeName` is a fully qualified type name (`"AssemblyName.Namespace.ClassName, AssemblyName"`). Use `Type.GetType(sampleTypeName)` to resolve it; log a warning and skip if the type can't be found.

For `"sync"` upload: use `services.AddHttpClient<SyncMasterRestClient>(client => { client.BaseAddress = new Uri(config.SyncMasterBaseUrl); client.Timeout = TimeSpan.FromSeconds(config.RequestTimeout); })`. Bind `SyncAdapterConfig` via `services.Configure<SyncAdapterConfig>(configuration.GetSection("sync"))`.

#### `src/Tracer.AdapterSelection/AdapterRegistrationExtensions.cs`

```csharp
namespace Tracer.AdapterSelection;

public static class AdapterRegistrationExtensions
{
    /// <summary>
    /// Registers adapter implementations chosen by the "adapters" configuration section.
    /// Call this from the host builder after all services are configured.
    /// </summary>
    public static IServiceCollection AddTracerAdapters(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var registry = new AdapterRegistry(configuration);
        registry.RegisterAdapters(services);
        return services;
    }
}
```

### Tests to Create

**File:** `tests/Tracer.Tests.Unit/AdapterSelection/AdapterRegistryTests.cs`

Add `ProjectReference` to `Tracer.AdapterSelection` in `tests/Tracer.Tests.Unit/Tracer.Tests.Unit.csproj`.

Test pattern: build an `IConfiguration` from a dictionary, call `registry.RegisterAdapters(services)`, resolve the registered type from `services.BuildServiceProvider()`, assert it's the expected implementation.

```csharp
// Helper:
private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
```

Required tests (target: 12–15 tests):

1. **`RegisterAdapters_DefaultConfig_RegistersMockDataSource`** — empty config; assert `IDiagnosticDataSource` resolves to `MockDataSource`.
2. **`RegisterAdapters_DefaultConfig_RegistersInProcessTransport`** — empty config; assert `IAgentTransport` resolves to `InProcessChannelTransport` (or whatever the in-process transport type is).
3. **`RegisterAdapters_DefaultConfig_RegistersLocalFileSystemUpload`** — assert `ITelemetryUploadService` resolves to local-filesystem mock.
4. **`RegisterAdapters_DefaultConfig_RegistersLocalFileSystemStorageReader`** — assert `ITelemetryStorageReader` resolves to local-filesystem mock.
5. **`RegisterAdapters_DataSource_Dds_RegistersDdsDiagnosticDataSource`** — `adapters:dataSource = "dds"` + minimal DDS config section; assert `IDiagnosticDataSource` resolves to `DdsDiagnosticDataSource`.
6. **`RegisterAdapters_Transport_SharedMemory_RegistersSharedMemoryTransport`** — `adapters:transport = "shared-memory"` + `sharedMemory:SharedMemoryName`, etc.; assert `IAgentTransport` resolves to `SharedMemoryTransport`.
7. **`RegisterAdapters_Upload_Sync_RegistersSyncSystemUploadService`** — `adapters:upload = "sync"` + sync config; assert `ITelemetryUploadService` resolves to `SyncSystemUploadService`.
8. **`RegisterAdapters_StorageReader_Nas_RegistersNasStorageReader`** — `adapters:storageReader = "nas"` + `nas:NasRoot`; assert `ITelemetryStorageReader` resolves to `NasStorageReader`.
9. **`RegisterAdapters_UnknownDataSource_ThrowsInvalidOperationException`** — `adapters:dataSource = "kafka"`; assert `InvalidOperationException` thrown with message containing `"dataSource"` and `"kafka"`.
10. **`RegisterAdapters_UnknownTransport_ThrowsInvalidOperationException`** — similar.
11. **`RegisterAdapters_MixedConfig_DdsDataSourcePlusMockUpload`** — `dataSource = "dds"` + `upload = "local-file-system"`; assert both are registered with the correct types.
12. **`RegisterAdapters_Clock_Simulated_RegistersSimulatedClock`** — `adapters:clock = "simulated"`; assert `IClock` resolves to `SimulatedClock`.
13. **`AddTracerAdapters_ExtensionMethod_RegistersServices`** — call `services.AddTracerAdapters(config)`; assert at least one core adapter interface is registered.

> **Test isolation note**: each test creates a fresh `ServiceCollection`. Do NOT share a `ServiceProvider` across tests.

---

## ✅ TRC-P11-006 — Configuration Additions

**Design reference:** `docs/tracer_phase11_design.md` §7.4  
**Task details:** `docs/TASK-DETAIL.md#trc-p11-006--configuration-additions--appsetingsjson-adapter-sections`

### Changes to `src/Tracer.Agent/appsettings.json`

Add the `adapters` section with mock defaults, and empty `dds`, `sharedMemory` sections:

```json
{
  "adapters": {
    "dataSource": "mock",
    "transport": "in-process",
    "upload": "local-file-system",
    "storageReader": "local-file-system",
    "clock": "system"
  },
  "dds": {
    "publisherNodeId": "UNSET",
    "ingestBufferSize": 50000,
    "participant": {
      "domainId": 0
    },
    "topics": []
  },
  "sharedMemory": {
    "sharedMemoryName": "TracerRingBuffer",
    "semaphoreName": "TracerSyncSem",
    "capacityBytes": 67108864
  }
}
```

Also wire `AddTracerAdapters` into the Agent's host builder: find the DI setup in `src/Tracer.Agent/Program.cs` and add:
```csharp
builder.Services.AddTracerAdapters(builder.Configuration);
```

Remove or replace any hardcoded `services.AddSingleton<IDiagnosticDataSource, MockDataSource>()` lines that this makes redundant.

### Changes to `src/Tracer.Aggregator/appsettings.json`

Add the `adapters` section and `nas` config:

```json
{
  "adapters": {
    "storageReader": "local-file-system",
    "clock": "system"
  },
  "nas": {
    "nasRoot": "UNSET",
    "preferLocalStaging": false
  }
}
```

Wire `AddTracerAdapters` into the Aggregator's host builder similarly.

### Create `src/Tracer.Agent/appsettings.IntegrationReal.json`

```json
{
  "adapters": {
    "dataSource": "dds",
    "transport": "shared-memory",
    "upload": "sync",
    "storageReader": "local-file-system",
    "clock": "system"
  }
}
```

This file is used by Phase 11's integration-real test suite.

### Build Verification

After making configuration changes:
1. `dotnet build src\Tracer.Agent\Tracer.Agent.csproj -c Release` — must succeed with 0 warnings
2. `dotnet build src\Tracer.Aggregator\Tracer.Aggregator.csproj -c Release` — must succeed with 0 warnings (if aggregator exists)
3. `dotnet test tests\Tracer.Tests.Unit -c Release --no-build --filter "FullyQualifiedName!~Publish_ProducesExpectedLayout"` — all tests pass

---

## 🧪 Testing Requirements

### Minimum Counts

| Area | Minimum |
|------|---------|
| Corrective fixes (P1-A through P1-D) | 8 new/modified tests |
| `AdapterRegistryTests` | 13 tests |
| **Total new+fixed tests** | 21+ |
| **Pre-existing tests must still pass** | 234 |

### Quality Standards

**❗ TEST QUALITY EXPECTATIONS**

- **NOT ACCEPTABLE:** `registry.RegisterAdapters(services)` then no assertion, or assert only that `services.Count > 0`
- **REQUIRED:** `services.BuildServiceProvider().GetRequiredService<IDiagnosticDataSource>()` and assert `is DdsDiagnosticDataSource`
- **REQUIRED:** Corrective tests must actually catch the bug they're fixing — if the fix reverts, the test must fail

**For Corrective Task 0 P1-A (back-pressure test)**:
- Do not mock the channel internals
- Use the real `DdsDiagnosticDataSource` with a small buffer size
- Inject 10 samples synchronously and verify the channel drops correctly
- The warning-log assertion is mandatory

**For AdapterRegistry tests**:
- Do NOT use `Moq` or `NSubstitute` for configuration — use real `ConfigurationBuilder`
- Use `ServiceCollection.BuildServiceProvider()` to resolve types
- Each test must resolve a specific interface type and assert `is ExactType`

---

## 📊 Report Requirements

**Required sections in `.dev/tracer/reports/BATCH-54-REPORT.md`:**

1. **Files Changed** — table with file path and change description
2. **Build Results** — paste output of `dotnet build tests\Tracer.Tests.Unit -c Release`
3. **Test Results** — paste output of `dotnet test tests\Tracer.Tests.Unit -c Release --no-build`
4. **Corrective Task 0 Summary** — list each P1 fix and confirm it addresses the root cause from BATCH-53-REVIEW
5. **Deviations from Instructions** — any intentional deviation with rationale

### Developer Insights

**Q1:** Did any of the P1 corrective fixes reveal additional gaps (e.g., while writing the drop-oldest test, did you find the implementation was subtly wrong)?

**Q2:** What issues did you encounter wiring `AddTracerAdapters` into the existing host builders? What patterns were already in place that you had to work around?

**Q3:** Were there any mock adapter class names in `Tracer.Adapters.Mock` that didn't match what you expected (e.g., different from `InProcessChannelTransport`)? How did you discover the correct names?

**Q4:** Did the `Type.GetType(sampleTypeName)` path for DDS topic type resolution work cleanly, or did you need a fallback?

**Q5:** Suggested commit message for this batch.

---

## 🎯 Success Criteria

- [ ] All 4 BATCH-53 P1 test gaps corrected (P1-A through P1-D)
- [ ] `Tracer.AdapterSelection` builds with 0 errors, 0 warnings
- [ ] `AdapterRegistry` correctly routes all 5 adapter slots to mock/real implementations
- [ ] Unknown adapter values throw `InvalidOperationException` with descriptive message
- [ ] `AddTracerAdapters` wired into `Tracer.Agent` host builder
- [ ] `appsettings.json` files updated with all required sections
- [ ] All 234+ tests passing

---

## ⚠️ Common Pitfalls to Avoid

1. **Forgetting to add the new project to `Tracer.sln`** — use `dotnet sln add`
2. **Not adding `ProjectReference` to `Tracer.Tests.Unit.csproj`** for the new `Tracer.AdapterSelection` project
3. **Circular dependency**: `Tracer.AdapterSelection` must NOT reference `Tracer.Agent` or `Tracer.Aggregator`
4. **ServiceProvider disposal warning**: in tests, call `provider.Dispose()` after resolving types, or use `using var provider = services.BuildServiceProvider()`
5. **IAgentTransport vs IAgentTransport implementations**: `SharedMemoryTransport` implements `IAgentTransport` but its `ReadAsync` method opens a ring buffer by name. In tests, the buffer doesn't need to exist for the DI registration test — you're just verifying the service type is registered, not that it works
6. **`CycloneDdsDisableCodeGen` in test project**: this flag is already in `Tracer.Tests.Unit.csproj` — don't remove it

---

## 📚 Reference Materials

- **Task Defs:** `docs/TASK-DETAIL.md` — see TRC-P11-005, TRC-P11-006
- **Design:** `docs/tracer_phase11_design.md` §7 (Adapter Selection), §3.8–§6.5 (Config POCOs)
- **Previous Review:** `.dev/tracer/reviews/BATCH-53-REVIEW.md`
- **Mock adapter patterns:** `src/Tracer.Adapters.Mock/MockDataSource.cs`, `src/Tracer.Adapters.Mock/Transport/`, `src/Tracer.Adapters.Mock/Upload/`, `src/Tracer.Adapters.Mock/Storage/`
- **Existing Host Builder:** `src/Tracer.Agent/Program.cs`
