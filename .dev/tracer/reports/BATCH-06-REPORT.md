# BATCH-06 Report

**Batch:** BATCH-06  
**Developer:** GitHub Copilot  
**Date:** 2026-05-20  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| TRC-P3-001 | ✅ | `Tracer.Observer` assembly fully implemented |
| TRC-P3-002 | ✅ | `Tracer.WebApi` assembly fully implemented |

---

## 🧪 Testing Results

**Unit Tests Passed:** 142 / 142  
**Integration Tests Passed:** 20 / 20 (+ 6 skipped stubs deferred to TRC-P3-009)

**Command used:** `dotnet test Tracer.sln --configuration Release`  
**Exit code:** 0

**Key Test Scenarios Verified:**
- [x] `ObserverIngestionTests` (6 tests) — pipeline writes events/states, broadcaster fires on events only, cancellation propagates cleanly, drop counter increments
- [x] `ObserverStateReporterTests` (5 tests) — snapshot accuracy, rolling counter buckets, expiry window
- [x] `ReadOnlyConnectionPoolTests` (6 tests) — pool initialization, connection acquire/return, rotation, dispose-after-dispose throws `ObjectDisposedException`
- [x] `ObserverHostedServiceTests` (5 tests) — startup order (recovery → open → pool init), graceful shutdown rotation, pool-refresh failure logged without crash
- [x] `HealthEndpointTests` (2 tests) — `/api/health` returns 200 with `{"status":"ok"}`, works without DuckDB
- [x] `ProblemDetailsFactoryTests` (4 tests) — null exception → 500, `ArgumentException` → 400 with message in detail, `TracerStorageException` → 500
- [x] `ObserverFakeNodeEndToEndTests` (3 stubs) — skipped, deferred to TRC-P3-009
- [x] `ObserverRotationIntegrationTests` (3 stubs) — skipped, deferred to TRC-P3-009

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

Several API mismatches were discovered from the Phase 2 precedents and required correction during test authoring:

- `LiveEventBroadcaster` has a no-arg constructor (not logger-injected). Tests that passed `NullLogger<LiveEventBroadcaster>` were fixed to `new LiveEventBroadcaster()`.
- `ITelemetryUploadService.RequestUploadAsync` returns `Task<UploadIntentId>` (a `readonly record struct`) not `Task<Guid>`. All `NoOpUploadService` test doubles were updated accordingly.
- `UploadStatus` is an enum (`Complete`, `Pending`, etc.) not a class/record.
- `TopicName` lives in `Tracer.Core.Domain`, not `Tracer.Core.Identity`. Using statements were corrected.
- All `DiagnosticRecord` subclass object initializers require all six base required properties (`SequenceNumber`, `PublishWallclock`, `ReceiveWallclock`, `PublisherNode`, `SubscriberNode`, `Topic`). Missing fields caused compile errors.
- `SimulatedClock` constructor takes `WallclockTime`, not `DateTimeOffset`. Replaced with `WallclockTime.FromDateTimeOffset(DateTimeOffset.UtcNow)`.
- `OnStart_RecoveryRunsBeforeIntervalOpen` initially cancelled the stopping token inside recovery, which prevented `InitializeAsync` from being reached — making the pool-before-recovery assertion vacuously fail. Replaced with a `StartAsync` / `Task.Delay` / `StopAsync` pattern that lets the pipeline fully initialize before verifying call order.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- `ReadOnlyConnectionPool.InitializeAsync` holds connections for the entire pool lifetime without any health-check or eviction policy. In the presence of DuckDB in-process file locking this is fine, but if the pool is extended to network databases the lack of reconnect logic could be a problem.
- `ObserverStateReporter` uses a fixed 60-bucket rolling counter (1-second granularity). If the interval is changed from 1 minute the counter would silently give wrong results. A constructor parameter for window size would make this more robust.
- The WebApi currently has no authentication middleware. CORS is permissive (`AllowAnyOrigin`). Both are intentional for this local-loopback tool, but worth noting for any future network-exposed deployment.

**Q3: What design decisions did you make beyond the instructions?**

- `WebApiFixture` was implemented with `builder.WebHost.UseTestServer()` + `app.GetTestClient()` (Microsoft.AspNetCore.Mvc.Testing) rather than spinning up a real Kestrel socket. This keeps tests hermetic, eliminates port-conflict flakiness, and mirrors the approach used in the ASP.NET Core documentation for in-process integration testing.
- `ObserverHostedServiceTests` uses virtual method overrides on `ReadOnlyConnectionPool` (`TrackingPool`, `FailingPool`) and `IStartupRecovery` (`TrackingRecovery`) rather than Moq/NSubstitute, consistent with the project's "no mock frameworks" convention.
- Integration stubs carry `[Fact(Skip = "Deferred to TRC-P3-009")]` so they appear in test discovery and serve as a to-do list visible in CI output without failing the suite.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- When the stopping token is cancelled before `ExecuteAsync` calls `pool.InitializeAsync`, the pool init is skipped entirely. The test for ordering (`OnStart_RecoveryRunsBeforeIntervalOpen`) must therefore not cancel mid-execution; it must wait for full initialization before asserting order.
- `ReadOnlyConnectionPool.AcquireAsync` after `DisposeAsync` must throw `ObjectDisposedException`. The test verifies this but the implementation needed the `_disposed` guard to be checked before the semaphore `WaitAsync` — otherwise the semaphore would never be released and the test would hang.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- `ObserverIngestionPipeline.RunAsync` iterates data sources sequentially. With many slow sources this could delay ingestion. A producer-consumer or `Task.WhenAll` approach per source would improve throughput at the cost of ordering guarantees.
- `LiveEventBroadcaster` uses a `Channel<EventRecord>` and a single reader task. Under high-frequency event streams the channel could back-pressure subscribers. The current `BoundedChannelFullMode.DropOldest` policy avoids blocking but silently discards events — acceptable for live streaming but worth surfacing as a metric.

---

## ⚠️ Outstanding Issues / Next Steps
- [ ] Full integration tests in `ObserverFakeNodeEndToEndTests` and `ObserverRotationIntegrationTests` deferred to TRC-P3-009 (require running Observer + FakeNode end-to-end)
- [ ] No authentication/authorization on WebApi endpoints — acceptable for loopback but document as known gap
- [ ] CORS policy is `AllowAnyOrigin` — appropriate for local dev tool, re-evaluate if ever network-exposed
