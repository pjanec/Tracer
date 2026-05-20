# Tracer — Technical Debt Tracker

| ID | Priority | Source Batch | Description | Target Batch | Status |
|----|----------|--------------|-------------|--------------|--------|
| DT-001 | P2 | BATCH-01 | `EventQueryBuilder.Build` embeds LIMIT/OFFSET as inline integers instead of `$limit`/`$offset` params per TRC-P1-006 spec | BATCH-03 | Open |
| DT-002 | P2 | BATCH-01 | `Build_SqlInjectionAttempt_IsParameterized` tests OwningPlayerId not PayloadSearch — misses `%`/`_` escape code path | BATCH-03 | Open |
| DT-003 | P2 | BATCH-01 | Future reports must include Developer Insights Q1-Q5 and suggested commit message | BATCH-02 | ✅ Resolved |
| DT-004 | P2 | BATCH-02 | `DeterminismTests` missing SequenceNumber and PayloadJson comparisons in same-seed test | BATCH-04 | Open |
| DT-005 | P2 | BATCH-02 | `MockDataSource_DifferentSeeds` checks fewer-than-all instead of first-record TraceId comparison | BATCH-04 | Open |
| DT-006 | P2 | BATCH-03 | `IntervalSchedulerTests` missing `LessThan1Minute_Throws` and `TimeUntilNextBoundary_Decreases` tests (TRC-P2-011 SC1) | BATCH-05 | Open |
| DT-007 | P2 | BATCH-03 | `IntervalRotatorTests` missing `NotifyCaptureGap_AccumulatesInManifest` test (TRC-P2-011 SC2) | BATCH-05 | Open |
| DT-008 | P2 | BATCH-03 | `RecordRouterTests` missing `RecordRouter_AfterWrite_NotifiesRotator` test (TRC-P2-011 SC3) | BATCH-05 | Open |

> P1 = Critical (blocks next batch), P2 = Should fix soon, P3 = Nice to fix eventually  
> Resolved items are marked ✅ (never deleted)
