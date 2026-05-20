# Tracer — Technical Debt Tracker

| ID | Priority | Source Batch | Description | Target Batch | Status |
|----|----------|--------------|-------------|--------------|--------|
| DT-001 | P2 | BATCH-01 | `EventQueryBuilder.Build` embeds LIMIT/OFFSET as inline integers instead of `$limit`/`$offset` params per TRC-P1-006 spec | BATCH-03 | ✅ Resolved |
| DT-002 | P2 | BATCH-01 | `Build_SqlInjectionAttempt_IsParameterized` tests OwningPlayerId not PayloadSearch — misses `%`/`_` escape code path | BATCH-03 | ✅ Resolved |
| DT-003 | P2 | BATCH-01 | Future reports must include Developer Insights Q1-Q5 and suggested commit message | BATCH-02 | ✅ Resolved |
| DT-004 | P2 | BATCH-02 | `DeterminismTests` missing SequenceNumber and PayloadJson comparisons in same-seed test | BATCH-04 | ✅ Resolved |
| DT-005 | P2 | BATCH-02 | `MockDataSource_DifferentSeeds` checks fewer-than-all instead of first-record TraceId comparison | BATCH-04 | ✅ Resolved |
| DT-006 | P2 | BATCH-03 | `IntervalSchedulerTests` missing `LessThan1Minute_Throws` and `TimeUntilNextBoundary_Decreases` tests (TRC-P2-011 SC1) | BATCH-04 | ✅ Resolved |
| DT-007 | P2 | BATCH-03 | `IntervalRotatorTests` missing `NotifyCaptureGap_AccumulatesInManifest` test (TRC-P2-011 SC2) | BATCH-04 | ✅ Resolved |
| DT-008 | P2 | BATCH-03 | `RecordRouterTests` missing `RecordRouter_AfterWrite_NotifiesRotator` test (TRC-P2-011 SC3) | BATCH-04 | ✅ Resolved |
| DT-009 | P3 | BATCH-04 | `StartupRecoveryService.TryFinalizeAsync` reads `slow_state.duckdb` but records `SlowStateCount = 0` regardless | BATCH-09 | ✅ Resolved |

| DT-010 | P1 | BATCH-06 | `ObserverIngestionTests`: 5 of 6 tests bypass `ObserverIngestionPipeline.RunAsync`; pipeline is constructed but never called; tests manually invoke writer/broadcaster directly | BATCH-07 corrective | ✅ Resolved |
| DT-011 | P1 | BATCH-06 | Integration test stub method names in `ObserverFakeNodeEndToEndTests` and `ObserverRotationIntegrationTests` don't match TRC-P3-001 SC14/SC15 spec — must be renamed exactly | BATCH-07 corrective | ✅ Resolved |
| DT-012 | P2 | BATCH-06 | `OnGracefulShutdown_FinalRotationHasGracefulReason` has no assertion (`await Task.CompletedTask`) — must read manifest and assert `FinalizationReason == GracefulShutdown` | BATCH-07 | ✅ Resolved |
| DT-013 | P1 | BATCH-07 | Scenario endpoint routes deviate from spec: `/api/scenarios/{sessionId}/...` instead of spec `/api/scenario/...?sessionId=...` — breaks TRC-P3-009 and TRC-P3-010 integration test routes | BATCH-08 corrective | ✅ Resolved |
| DT-014 | P2 | BATCH-07 | `GetNotables_PaginationWithBeforeCursor` only asserts 200 status — must verify returned events are strictly before the cursor | BATCH-08 | ✅ Resolved |
| DT-015 | P2 | BATCH-07 | `ListSessions_OrderedByStartTimeDesc` test missing from `SessionEndpointTests` (replaced with fields-check test) | BATCH-08 | ✅ Resolved |
| DT-016 | P3 | BATCH-07 | `IntervalRotator.CurrentWriter` has a public setter for test injection; should be `internal set` + `InternalsVisibleTo` | BATCH-09 | ✅ Resolved |
| DT-017 | P2 | BATCH-08 | `SecondInterval_QueriesReturnCurrentIntervalEvents`: `ingestedTotal > 0` is trivially true; push session_start into interval 2, then verify `GET /api/sessions` shows it (proving pool targets new interval) | BATCH-09 | ✅ Resolved |
| DT-018 | P2 | BATCH-08 | `GetEvent_ById_ReturnsCorrectEventDto`: missing `traceId`, `severity`, `occurredAtUtc` field assertions matching the pushed event (spec TRC-P3-010 SC7) | BATCH-09 | ✅ Resolved |
| DT-019 | P2 | BATCH-08 | `GetTopology_AfterIngestion_ReturnsNodeInfo`: missing `eventsPublished` count assertion per node (spec TRC-P3-010 SC9) | BATCH-09 | ✅ Resolved |
| DT-020 | P3 | BATCH-08 | `MultipleNodes_AllEventsAppearInUnifiedStream`: asserts `lines.Count == 20` but doesn't verify 20 distinct `eventId` values (spec says "verified by eventId") | BATCH-09 | ✅ Resolved (test uses PascalCase `EventId` matching current SSE output — will update to camelCase in DT-021 fix) |
| DT-021 | P1 | BATCH-09 | SSE endpoint (`SseEndpoints.cs`) uses `JsonSerializer.Serialize(dto)` without options, producing PascalCase (`EventId`, `TraceId`). REST API uses camelCase. TypeScript `NotableEventDto` uses camelCase. This breaks TRC-P3-007 `useLiveNotables` SSE parsing — all field accesses return `undefined`. Fix: add `JsonNamingPolicy.CamelCase` options to SSE serializer; update DT-020 test to use `GetProperty("eventId")`. | BATCH-10 | Open |
| DT-022 | P2 | BATCH-09 | `@typescript-eslint` v6 in `tracer-viewer/package.json` does not officially support TypeScript 5.4.5 (supported range `<5.4.0`). Upgrade `@typescript-eslint/eslint-plugin` and `@typescript-eslint/parser` to `^7.0.0` or `^8.0.0`. | BATCH-10 | Open |

> P1 = Critical (blocks next batch), P2 = Should fix soon, P3 = Nice to fix eventually  
> Resolved items are marked ✅ (never deleted)
