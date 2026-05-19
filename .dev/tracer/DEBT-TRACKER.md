# Tracer — Technical Debt Tracker

| ID | Priority | Source Batch | Description | Target Batch | Status |
|----|----------|--------------|-------------|--------------|--------|
| DT-001 | P2 | BATCH-01 | `EventQueryBuilder.Build` embeds LIMIT/OFFSET as inline integers instead of `$limit`/`$offset` params per TRC-P1-006 spec | BATCH-03 | Open |
| DT-002 | P2 | BATCH-01 | `Build_SqlInjectionAttempt_IsParameterized` tests OwningPlayerId not PayloadSearch — misses `%`/`_` escape code path | BATCH-03 | Open |
| DT-003 | P2 | BATCH-01 | Future reports must include Developer Insights Q1-Q5 and suggested commit message | BATCH-02 | Open |

> P1 = Critical (blocks next batch), P2 = Should fix soon, P3 = Nice to fix eventually  
> Resolved items are marked ✅ (never deleted)
