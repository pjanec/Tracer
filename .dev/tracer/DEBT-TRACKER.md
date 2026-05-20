# Tracer — Technical Debt Tracker

| ID | Priority | Source Batch | Description | Target Batch | Status |
|----|----------|--------------|-------------|--------------|--------|
| DT-001 | P2 | BATCH-01 | `EventQueryBuilder.Build` embeds LIMIT/OFFSET as inline integers instead of `$limit`/`$offset` params per TRC-P1-006 spec | BATCH-03 | Open |
| DT-002 | P2 | BATCH-01 | `Build_SqlInjectionAttempt_IsParameterized` tests OwningPlayerId not PayloadSearch — misses `%`/`_` escape code path | BATCH-03 | Open |
| DT-003 | P2 | BATCH-01 | Future reports must include Developer Insights Q1-Q5 and suggested commit message | BATCH-02 | ✅ Resolved |
| DT-004 | P2 | BATCH-02 | `DeterminismTests` missing SequenceNumber and PayloadJson comparisons in same-seed test | BATCH-04 | Open |
| DT-005 | P2 | BATCH-02 | `MockDataSource_DifferentSeeds` checks fewer-than-all instead of first-record TraceId comparison | BATCH-04 | Open |
| DT-006 | P2 | BATCH-03 | `IntervalSchedulerTests` missing `LessThan1Minute_Throws` and `TimeUntilNextBoundary_Decreases` tests (TRC-P2-011 SC1) | BATCH-04 | ✅ Resolved |
| DT-007 | P2 | BATCH-03 | `IntervalRotatorTests` missing `NotifyCaptureGap_AccumulatesInManifest` test (TRC-P2-011 SC2) | BATCH-04 | ✅ Resolved |
| DT-008 | P2 | BATCH-03 | `RecordRouterTests` missing `RecordRouter_AfterWrite_NotifiesRotator` test (TRC-P2-011 SC3) | BATCH-04 | ✅ Resolved |
| DT-009 | P3 | BATCH-04 | `StartupRecoveryService.TryFinalizeAsync` reads `slow_state.duckdb` but records `SlowStateCount = 0` regardless | BATCH-09 | Open |

| DT-010 | P1 | BATCH-06 | `ObserverIngestionTests`: 5 of 6 tests bypass `ObserverIngestionPipeline.RunAsync`; pipeline is constructed but never called; tests manually invoke writer/broadcaster directly | BATCH-07 corrective | Open |
| DT-011 | P1 | BATCH-06 | Integration test stub method names in `ObserverFakeNodeEndToEndTests` and `ObserverRotationIntegrationTests` don't match TRC-P3-001 SC14/SC15 spec — must be renamed exactly | BATCH-07 corrective | Open |
| DT-012 | P2 | BATCH-06 | `OnGracefulShutdown_FinalRotationHasGracefulReason` has no assertion (`await Task.CompletedTask`) — must read manifest and assert `FinalizationReason == GracefulShutdown` | BATCH-07 | Open |

> P1 = Critical (blocks next batch), P2 = Should fix soon, P3 = Nice to fix eventually  
> Resolved items are marked ✅ (never deleted)
