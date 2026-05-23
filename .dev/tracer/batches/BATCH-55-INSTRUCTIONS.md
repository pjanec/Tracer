# BATCH-55 — Phase 11: Corrective Fixes + Hardening

**Batch Number:** BATCH-55  
**Tasks:** Corrective Task 0 (BATCH-54 P1 fixes) + TRC-P11-007  
**Phase:** 11 — Real Adapter Integration  
**Estimated Effort:** 14–16 hours  
**Priority:** CRITICAL  
**Dependencies:** BATCH-54 (rejected — partial work in place, see below)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch has two parts:
1. **Corrective Task 0**: Fix **all five P1 items** from the BATCH-54 review (which REJECTED the previous submission). The previous developer falsely reported these as done — verification shows none of them were applied.
2. **TRC-P11-007**: Implement hardening for all four real adapters plus health endpoint additions.

### Required Reading (IN ORDER)

1. `.dev/tracer/reviews/BATCH-54-REVIEW.md` — understand exactly which P1 items must be fixed
2. `docs/TASK-DETAIL.md` section [TRC-P11-007](../../docs/TASK-DETAIL.md#trc-p11-007--hardening--resource-limits-back-pressure-and-error-recovery)
3. `docs/tracer_phase11_design.md` §9 (Hardening Items — §9.1 Resource Limits, §9.2 Graceful Degradation, §9.3 Error Recovery, §9.4 Monitoring Hooks)
4. `docs/tracer_phase11_design.md` §4.6 (SharedMemory monitoring loop code snippet) and §5.6 (Sync upload backlog)

### Source Code Locations

- **P1 fixes:** `src/Tracer.Adapters.DDS/DdsDiagnosticDataSource.cs`, `src/Tracer.Agent/AgentHostBuilder.cs`, `tests/Tracer.Tests.Unit/Adapters/` (several files)
- **Hardening — NAS:** `src/Tracer.Adapters.Nas/NasStorageReader.cs` (add retry + circuit breaker)
- **Hardening — agent monitoring:** `src/Tracer.Agent/AgentHostedService.cs` (add monitor loop) or a new `TransportMonitor.cs`
- **Hardening — health:** `src/Tracer.WebApi/Endpoints/HealthEndpoints.cs`
- **Tests:** `tests/Tracer.Tests.Unit/Adapters/`, `tests/Tracer.Tests.Unit/WebApi/`

### Report Submission

**When done, submit your report to:**  
`.dev/tracer/reports/BATCH-55-REPORT.md`

**If you have questions, create:**  
`.dev/tracer/questions/BATCH-55-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Corrective Task 0:** Apply all 5 P1 fixes → `dotnet test tests\Tracer.Tests.Unit -c Release --no-build --filter "FullyQualifiedName~DdsDiagnostic|FullyQualifiedName~SharedMemoryRingBuffer|FullyQualifiedName~SharedMemoryTransport|FullyQualifiedName~SyncSystemUpload|FullyQualifiedName~AdapterRegistry"` → **ALL pass** ✅
2. **TRC-P11-007:** Implement hardening → Write tests → **ALL tests pass** ✅
3. **Final:** `dotnet test tests\Tracer.Tests.Unit -c Release --no-build --filter "FullyQualifiedName!~Publish_ProducesExpectedLayout"` → **ALL pass** ✅

**DO NOT** move to the next task until current task tests pass.  
**DO NOT** fabricate test results — run the actual commands and include the actual output in your report.  
**DO NOT** stop after writing code — fix all failures until zero failures remain.  
**DO NOT** ask for permission to run tests or fix build errors — just do it.

---

## ✅ Corrective Task 0: Fix All P1 Items from BATCH-54 Review

Verification shows **none** of these were applied despite the previous report claiming they were done. Fix each one completely.

### P1-A: Fix DDS Overflow Detection (dead code path)

**File:** `src/Tracer.Adapters.DDS/DdsDiagnosticDataSource.cs`  
**File:** `tests/Tracer.Tests.Unit/Adapters/DDS/DdsDiagnosticDataSourceTests.cs`

**Problem:** `Channel.CreateBounded` with `FullMode = BoundedChannelFullMode.DropOldest` causes `TryWrite` to **always return `true`** (the channel silently drops the oldest item). The `if (!writer.TryWrite(record))` branch is **dead code** that can never execute. The `_dropBurstActive` warning is never logged in production.

**Fix:**

In `DdsDiagnosticDataSource.cs`, change `OnSampleReceived` to accept a `ChannelReader<DiagnosticRecord> reader` parameter alongside the writer:

```csharp
private void OnSampleReceived(
    IDdsSample sample,
    DdsTopicSubscription topicSub,
    ChannelWriter<DiagnosticRecord> writer,
    ChannelReader<DiagnosticRecord> reader,
    int capacity)
{
    try
    {
        var record = _translator.Translate(sample, topicSub);
        if (record is null) return;

        // Pre-check: DropOldest means TryWrite always succeeds, so we must
        // check the count before writing to detect that a drop will occur.
        if (reader.Count >= capacity)
        {
            Interlocked.Increment(ref _droppedCount);
            if (Interlocked.Exchange(ref _dropBurstActive, 1) == 0)
                _logger.LogWarning(
                    "DDS ingest channel full (capacity={Capacity}), dropping oldest record for topic {Topic}",
                    capacity, topicSub.TopicName);
        }
        else
        {
            Interlocked.Exchange(ref _dropBurstActive, 0);
        }

        writer.TryWrite(record);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to translate DDS sample on topic {Topic}", topicSub.TopicName);
    }
}
```

Add `private long _droppedCount;` field alongside `_dropBurstActive`. Add a `public long GetDroppedCount() => Interlocked.Read(ref _droppedCount);` method.

Update the call sites in `ReadAsync` to pass `channel.Reader` and `_config.IngestBufferSize`.

**Test required — add to `DdsDiagnosticDataSourceTests.cs`:**

```csharp
[Fact]
public async Task ReadAsync_OverfilledChannel_DropsRecordsAndLogsWarning()
{
    // Arrange: inject more samples than the buffer capacity.
    // Use a small buffer (3 items) and inject 10 samples.
    const int bufferSize = 3;
    var samples = Enumerable.Range(0, 10)
        .Select(i => (IDdsSample)new FakeSample(new FakeEventPayload { eventId = (ulong)i }, (ulong)i))
        .ToList();
    var factory = new FakeSubscriberFactory(samples);

    var logger = new CapturingLogger<DdsDiagnosticDataSource>();
    var source = Build(factory, ingestBufferSize: bufferSize, logger: logger);

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    var received = new List<DiagnosticRecord>();
    try
    {
        await foreach (var record in source.ReadAsync(cts.Token))
        {
            received.Add(record);
        }
    }
    catch (OperationCanceledException) { /* expected */ }

    // At most bufferSize items were queued; the rest were dropped.
    received.Count.Should().BeLessThanOrEqualTo(bufferSize);
    source.GetDroppedCount().Should().BeGreaterThan(0);
    logger.Warnings.Should().Contain(w => w.Contains("channel full"));
}
```

Add a `CapturingLogger<T>` helper in the test file (or a shared file in the test project):

```csharp
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Warnings { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= LogLevel.Warning)
            Warnings.Add(formatter(state, exception));
    }
}
```

Update `Build(...)` to accept `int ingestBufferSize = 100` and `ILogger<DdsDiagnosticDataSource>? logger = null` parameters.

---

### P1-B: Fix Trivial Drop Count Assertion

**File:** `tests/Tracer.Tests.Unit/Adapters/SharedMemory/SharedMemoryRingBufferTests.cs`

**Problem:** Line 109 reads `buffer.GetDroppedCount().Should().BeGreaterThanOrEqualTo(0)` — this is trivially true and proves nothing.

**Fix:** Change to `buffer.GetDroppedCount().Should().BeGreaterThan(0)`.

---

### P1-C: Add Field-Level Assertions to Transport Round-Trip Test

**File:** `tests/Tracer.Tests.Unit/Adapters/SharedMemory/SharedMemoryTransportTests.cs`

**Problem:** `ReadAsync_RecordsWrittenByWriter_AreYielded` only asserts `received.Should().HaveCount(3)`. No field is verified to have round-tripped through encode/decode.

**Fix:** After `received.Should().HaveCount(3)`, add:

```csharp
// Verify field-level round-trip through encode/decode
received[0].Should().BeOfType<EventRecord>().Which.SequenceNumber.Should().Be(1UL);
received[1].Should().BeOfType<EventRecord>().Which.SequenceNumber.Should().Be(2UL);
received[2].Should().BeOfType<EventRecord>().Which.SequenceNumber.Should().Be(3UL);
received.OfType<EventRecord>().Should().AllSatisfy(r =>
{
    r.Topic.Should().Be(new TopicName("topic.event"));
    r.PublisherNode.Should().Be(new AgentId("pub"));
});
```

---

### P1-D: Add 4 Missing SyncSystemUploadService Tests

**File:** `tests/Tracer.Tests.Unit/Adapters/Sync/SyncSystemUploadServiceTests.cs`

**Problem:** The following 4 test scenarios from the BATCH-53 spec are missing:
- **SC1**: `RequestUploadAsync` sends correct JSON body to the sync master (verify request body serialization)
- **SC3**: `WaitForCompletionAsync` polls until Complete (calls `GetStatusAsync` multiple times)
- **SC5**: `WaitForCompletionAsync` cancelled during poll throws `OperationCanceledException`
- **SC6**: `RequestUploadAsync` returns 503 twice then 201 — retries and succeeds

**Fix:** Extend `FakeHttpMessageHandler` with request capture support and a response queue for multi-response sequences. Add the 4 tests:

```csharp
// Extend FakeHttpMessageHandler:
private sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    public List<HttpRequestMessage> CapturedRequests { get; } = new();

    public void Enqueue(HttpStatusCode status, object? body = null)
    {
        _responses.Enqueue(_ => {
            var r = new HttpResponseMessage(status);
            if (body is not null) r.Content = JsonContent.Create(body);
            return r;
        });
    }

    public void EnqueueFactory(Func<HttpRequestMessage, HttpResponseMessage> factory)
        => _responses.Enqueue(factory);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        CapturedRequests.Add(request);
        if (_responses.Count == 0)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = JsonContent.Create(new { intentId = "default-intent" }) });
        return Task.FromResult(_responses.Dequeue()(request));
    }
}
```

**SC1 test:**
```csharp
[Fact]
public async Task RequestUploadAsync_SendsCorrectBodyToSyncMaster()
{
    var (svc, handler) = Build();
    string? capturedBody = null;
    handler.EnqueueFactory(req => {
        capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(new { intentId = "i1" }) };
    });

    await svc.RequestUploadAsync(MakeRequest("blue-cmd-01", "20260519T140000Z"), CancellationToken.None);

    capturedBody.Should().NotBeNull();
    capturedBody.Should().Contain("blue-cmd-01");
    capturedBody.Should().Contain("20260519T140000Z");
}
```

**SC3 test:**
```csharp
[Fact]
public async Task WaitForCompletionAsync_PollingUntilComplete_CallsGetStatusMultipleTimes()
{
    var (svc, handler) = Build();
    handler.Enqueue(HttpStatusCode.OK, new { intentId = "blue-cmd-01|20260519T140000Z" });
    var intentId = await svc.RequestUploadAsync(MakeRequest(), CancellationToken.None);

    // Return InProgress twice, then Completed
    handler.Enqueue(HttpStatusCode.OK, new { status = "InProgress" });
    handler.Enqueue(HttpStatusCode.OK, new { status = "InProgress" });
    handler.Enqueue(HttpStatusCode.OK, new { status = "Completed" });

    var finalStatus = await svc.WaitForCompletionAsync(intentId, pollIntervalMs: 1, CancellationToken.None);

    finalStatus.Should().Be(UploadStatus.Complete);
    // At least 3 status calls were made
    handler.CapturedRequests.Count(r => r.RequestUri!.PathAndQuery.Contains("status"))
        .Should().BeGreaterThanOrEqualTo(3);
}
```

**SC5 test:**
```csharp
[Fact]
public async Task WaitForCompletionAsync_CancelledDuringPoll_ThrowsOperationCanceledException()
{
    var (svc, handler) = Build();
    handler.Enqueue(HttpStatusCode.OK, new { intentId = "blue-cmd-01|20260519T140000Z" });
    var intentId = await svc.RequestUploadAsync(MakeRequest(), CancellationToken.None);

    // Always return InProgress to force polling to continue
    for (int i = 0; i < 100; i++)
        handler.Enqueue(HttpStatusCode.OK, new { status = "InProgress" });

    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
    Func<Task> act = () => svc.WaitForCompletionAsync(intentId, pollIntervalMs: 10, cts.Token);

    await act.Should().ThrowAsync<OperationCanceledException>();
}
```

**SC6 test:**
```csharp
[Fact]
public async Task RequestUploadAsync_Returns503Twice_Then201_RetriesAndSucceeds()
{
    var (svc, handler) = Build(retryAttempts: 3);
    handler.Enqueue(HttpStatusCode.ServiceUnavailable);
    handler.Enqueue(HttpStatusCode.ServiceUnavailable);
    handler.Enqueue(HttpStatusCode.OK, new { intentId = "retry-intent" });

    var result = await svc.RequestUploadAsync(MakeRequest(), CancellationToken.None);

    result.Should().NotBeNull();
    result.Value.Should().NotBeEmpty();
    handler.CapturedRequests.Should().HaveCount(3);
}
```

> **Note:** If `WaitForCompletionAsync` doesn't currently accept a `pollIntervalMs` parameter, add it (default 5000) or use `TimeSpan` overload. If `SyncSystemUploadService` doesn't have a `WaitForCompletionAsync` method, check the implementation — SC3 and SC5 may need to call `GetStatusAsync` in a loop directly in the test (simulating what the caller does). Adapt as needed but the test must exercise the polling behavior.

---

### P1-F: Wire AdapterSelection into AgentHostBuilder

**File:** `src/Tracer.Agent/AgentHostBuilder.cs`

**Problem:** `AgentHostBuilder.cs` still contains hardcoded adapter registrations:
```csharp
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IAgentTransport>(sp => TransportFactory.Create(...));
builder.Services.AddSingleton<ITelemetryUploadService>(sp => UploadServiceFactory.Create(...));
```
`AddTracerAdapters` was never called.

**Fix:** Replace the three hardcoded adapter registrations with:
```csharp
builder.Services.AddTracerAdapters(builder.Configuration);
```

Add `using Tracer.AdapterSelection;` at the top of the file.

Remove the now-redundant `using` directives for `Tracer.Agent.Transport`, `Tracer.Agent.Upload`, and `Tracer.Agent.Time` if they are only used by the removed code (check that no other code in the builder file needs them).

Verify the build: `dotnet build src\Tracer.Agent\Tracer.Agent.csproj -c Release`

> **Note:** `TransportFactory.cs` and `UploadServiceFactory.cs` in `Tracer.Agent` may become dead code after this change. Do NOT delete them yet — they may be referenced by other code paths. Leave them in place; a cleanup can happen in a future batch.

---

## ✅ TRC-P11-007: Hardening

**Task definition:** `docs/TASK-DETAIL.md` §TRC-P11-007  
**Design reference:** `docs/tracer_phase11_design.md` §9

---

### Task 7.1: SharedMemory Monitor Loop in TracerAgent

**File:** `src/Tracer.Agent/Diagnostics/TransportMonitor.cs` (NEW)

Create a dedicated `TransportMonitor` class that runs a periodic background loop:

```csharp
using Microsoft.Extensions.Logging;
using Tracer.Core.Abstractions;

namespace Tracer.Agent.Diagnostics;

/// <summary>
/// Periodically polls the transport for dropped records and logs a warning when the count increases.
/// </summary>
internal sealed class TransportMonitor
{
    private readonly IAgentTransport _transport;
    private readonly ILogger<TransportMonitor> _logger;
    private readonly TimeSpan _pollInterval;
    private long _lastDroppedCount;

    public TransportMonitor(
        IAgentTransport transport,
        ILogger<TransportMonitor> logger,
        TimeSpan? pollInterval = null)
    {
        _transport = transport;
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(5);
    }

    public async Task MonitorAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollInterval, ct).ConfigureAwait(false);
                var health = _transport.GetHealth();
                var newDrops = health.TotalDropped - _lastDroppedCount;
                if (newDrops > 0)
                {
                    _logger.LogWarning(
                        "Transport dropped records since last check: NewDrops={NewDrops}, TotalDropped={TotalDropped}",
                        newDrops, health.TotalDropped);
                    _lastDroppedCount = health.TotalDropped;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // Monitor must never throw — log and continue.
                _logger.LogError(ex, "TransportMonitor encountered an unexpected error");
            }
        }
    }
}
```

Register `TransportMonitor` as a singleton in `AgentHostBuilder.cs` and start `MonitorAsync` in `AgentHostedService.StartAsync` (fire-and-forget with the hosted service's cancellation token).

**Tests — `tests/Tracer.Tests.Unit/Agent/TransportMonitorTests.cs` (NEW):**

Test 1 (`MonitorAsync_DroppedCountIncreases_LogsWarning`): Use a fake transport whose `GetHealth()` returns `TotalDropped = 0` on first call then `TotalDropped = 5` on second. Run the monitor for two cycles (short poll interval). Assert exactly one warning is logged containing "NewDrops=5".

Test 2 (`MonitorAsync_DroppedCountStable_NoWarningLogged`): Fake transport always returns `TotalDropped = 0`. Run three cycles. Assert no warnings logged.

Test 3 (`MonitorAsync_ExceptionInPoll_DoesNotThrow`): Fake transport throws `InvalidOperationException` from `GetHealth()`. Run the monitor for 2 cycles. Assert the monitor task does not throw.

---

### Task 7.2: NAS Reader — Retry and Circuit Breaker

**File:** `src/Tracer.Adapters.Nas/NasStorageReader.cs`

Read the current implementation first. Add the following capabilities:

**Retry on transient IOException:** Wrap file read operations in a retry helper. Configuration comes from `NasAdapterConfig`:
```csharp
public int RetryOnTransientError { get; init; } = 3;        // max attempts
public int RetryBaseDelaySeconds { get; init; } = 2;         // base delay in seconds
```

**Circuit breaker:** Track consecutive failure count. After `CircuitBreakerThreshold` consecutive failures, throw `CircuitBreakerOpenException`. Reset after `CircuitBreakerResetInterval`.
```csharp
public int CircuitBreakerThreshold { get; init; } = 5;
public int CircuitBreakerResetSeconds { get; init; } = 60;
```

**New exception type** — `src/Tracer.Adapters.Nas/CircuitBreakerOpenException.cs` (NEW):
```csharp
namespace Tracer.Adapters.Nas;

public sealed class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException(string message) : base(message) { }
}
```

Update `NasAdapterConfig.cs` to include `RetryOnTransientError`, `RetryBaseDelaySeconds`, `CircuitBreakerThreshold`, `CircuitBreakerResetSeconds` properties.

**Tests — `tests/Tracer.Tests.Unit/Adapters/Nas/NasReaderHardeningTests.cs` (NEW):**

Test 1 (`ReadFile_TransientIoException_RetriesAndSucceeds`): Mock file access that throws `IOException` twice then succeeds. Assert the read completes on the third attempt.

Test 2 (`ReadFile_AlwaysThrowsIoException_CircuitBreakerTripsAfterThreshold`): Always throw `IOException`. Assert that after `CircuitBreakerThreshold` calls, `CircuitBreakerOpenException` is thrown and `LogError` is emitted.

Test 3 (`ReadFile_CircuitBreakerResetsAfterInterval`): Trip the circuit breaker. Advance a `SimulatedClock` (or equivalent) past `CircuitBreakerResetSeconds`. Assert the next attempt calls the real (mocked) file access instead of immediately throwing.

> **Important:** The circuit breaker state must be **per-instance** (not static). Do not use a global static field.

---

### Task 7.3: Sync Upload Backlog Tracking

**File:** `src/Tracer.Agent/Upload/UploadIntentDispatcher.cs` (or whichever class queues upload intents)

Explore the current implementation to understand how intervals are queued for upload. The class responsible for tracking pending uploads should expose:
- `int PendingCount` property (count of intervals queued but not yet confirmed uploaded)
- `int BacklogWarningThreshold` (configurable via `AgentConfig`, default 3)
- Log a `LogWarning` when `PendingCount > BacklogWarningThreshold`

Find where `PendingCount` is tracked and add the backlog warning.

**Graceful shutdown flush:** In `AgentHostedService.StopAsync` (or equivalent shutdown path), after cancellation is signalled, wait up to `ShutdownUploadFlushTimeoutSeconds` (add to `AgentConfig`, default 60) for any in-flight upload to complete before returning. Use `Task.WhenAny(uploadTask, Task.Delay(timeout))` pattern.

Add `ShutdownUploadFlushTimeoutSeconds` to `AgentConfig` with a default of 60.

**Tests — `tests/Tracer.Tests.Unit/Agent/SyncUploadHardeningTests.cs` (NEW):**

Test 1 (`PendingCount_ExceedsThreshold_LogsWarning`): Enqueue 4 intervals (threshold = 3). Assert `LogWarning` is emitted referencing backlog count.

Test 2 (`GracefulShutdown_WaitsForInFlightUpload`): Signal shutdown while one upload is in-flight (mock a slow `ITelemetryUploadService`). Assert the shutdown path waits (up to the timeout) for the upload to complete.

---

### Task 7.4: Health Endpoint Additions

**File:** `src/Tracer.WebApi/Endpoints/HealthEndpoints.cs`

Update the `/api/health` endpoint to return richer data. The current response is `new { status = "ok" }`. Change it to include adapter health fields:

```csharp
app.MapGet("/api/health", (IAgentTransport? transport = null) =>
{
    var transportHealth = transport?.GetHealth();
    return Results.Ok(new
    {
        status = "ok",
        sharedMemoryDropped = transportHealth?.TotalDropped ?? 0L,
        ingestChannelDepth = transportHealth?.PendingCount ?? 0,
    });
});
```

The `IAgentTransport` should be injected as a nullable optional parameter (it may not be registered in all deployment modes — e.g., observer-only).

**Tests — update `tests/Tracer.Tests.Unit/WebApi/HealthEndpointTests.cs`:**

Verify `GET /api/health` returns a 200 response with `status`, `sharedMemoryDropped`, and `ingestChannelDepth` fields in the JSON body.

> **Note:** If `HealthEndpointTests.cs` doesn't exist yet, check `tests/Tracer.Tests.Unit/WebApi/` for the existing health test. The existing unit test suite already has WebApi tests — use the same fixture pattern.

---

## 🧪 Testing Requirements

**Run after Corrective Task 0:**
```
dotnet test tests\Tracer.Tests.Unit -c Release --no-build \
  --filter "FullyQualifiedName~DdsDiagnosticDataSource|FullyQualifiedName~SharedMemoryRingBuffer|FullyQualifiedName~SharedMemoryTransport|FullyQualifiedName~SyncSystemUpload|FullyQualifiedName~AdapterRegistry"
```
**Expected:** All pass, zero failures.

**Run after TRC-P11-007:**
```
dotnet test tests\Tracer.Tests.Unit -c Release --no-build \
  --filter "FullyQualifiedName~TransportMonitor|FullyQualifiedName~NasReaderHardening|FullyQualifiedName~SyncUploadHardening|FullyQualifiedName~HealthEndpoint"
```
**Expected:** All pass, zero failures.

**Final full suite:**
```
dotnet test tests\Tracer.Tests.Unit -c Release --no-build --filter "FullyQualifiedName!~Publish_ProducesExpectedLayout"
```
**Expected:** All pass, zero failures. Include the actual console output in your report.

---

## ⚠️ Quality Standards

**❗ TEST QUALITY — NON-NEGOTIABLE**

Every test must exercise real behavior, not just compile:
- P1-A test: must verify `GetDroppedCount() > 0` AND a warning containing "channel full" was logged
- P1-D SC3 test: must verify multiple `GetStatusAsync` calls were made (not just that it returned Complete)
- P1-D SC5 test: must verify `OperationCanceledException` is thrown during active polling
- P1-D SC6 test: must verify exactly 3 HTTP requests were made
- TransportMonitor tests: must verify the actual log output (use `CapturingLogger<T>`)
- NasReader circuit breaker test: must verify `CircuitBreakerOpenException` is thrown after threshold

**❗ DO NOT FABRICATE RESULTS**

The previous submission was REJECTED because it claimed fixes that were not applied. Include actual command output, actual file line numbers, and actual test counts in your report. If a test fails, fix the root cause. Do not move on while failures exist.

---

## 🎯 Success Criteria

- [ ] P1-A: `DdsDiagnosticDataSource.GetDroppedCount()` increments when channel full; `OverfilledChannel_DropsRecordsAndLogsWarning` test passes
- [ ] P1-B: `GetDroppedCount_AfterDrop_ReturnsPositive` uses `BeGreaterThan(0)` and passes
- [ ] P1-C: `ReadAsync_RecordsWrittenByWriter_AreYielded` asserts `SequenceNumber`, `Topic`, `PublisherNode` fields
- [ ] P1-D: 4 new Sync upload tests present and passing
- [ ] P1-F: `AgentHostBuilder.cs` calls `AddTracerAdapters(builder.Configuration)` instead of hardcoded adapter singletons
- [ ] TRC-P11-007: `TransportMonitor` implemented and tested (3 tests)
- [ ] TRC-P11-007: `NasStorageReader` retry + circuit breaker implemented and tested (3 tests)
- [ ] TRC-P11-007: Sync upload backlog warning implemented and tested (2 tests)
- [ ] TRC-P11-007: `/api/health` returns `sharedMemoryDropped` + `ingestChannelDepth` fields
- [ ] Full unit test suite passes (excluding `Publish_ProducesExpectedLayout`)

---

## 📚 Reference Materials

- **BATCH-54 Review (REJECTED):** `.dev/tracer/reviews/BATCH-54-REVIEW.md`
- **Task Definitions:** `docs/TASK-DETAIL.md` §TRC-P11-007
- **Phase 11 Design:** `docs/tracer_phase11_design.md` §4.6, §5.6, §8, §9
- **IAgentTransport:** `src/Tracer.Core/Abstractions/IAgentTransport.cs` (TransportHealth already has TotalDropped)
- **NasAdapterConfig:** `src/Tracer.Adapters.Nas/Configuration/NasAdapterConfig.cs`
- **AgentConfig:** `src/Tracer.Agent/Configuration/AgentConfig.cs`
- **AdapterRegistry:** `src/Tracer.AdapterSelection/AdapterRegistry.cs`
- **SharedMemoryTransport.GetHealth():** Already returns `TotalDropped` — use it

---

## 📊 Report Requirements

Your report must include:

1. **Verification** of each P1 fix (show the actual code change, not just claim it was done)
2. **Actual test output** — paste the real console output from `dotnet test`, not fabricated numbers
3. **Test counts** — total before and after (actual number from the test runner)
4. **Any deviations** — if you adapted an approach from the spec, explain exactly why
5. **Developer insights:**
   - What was the trickiest part of the NAS circuit breaker?
   - Did you find any issues in existing code while implementing the hardening?
   - What edge cases did you discover that weren't in the spec?
   - Suggested commit message
