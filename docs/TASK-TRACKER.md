# Tracer — Task Tracker

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

**Architecture:** [tracer_architecture_v1.md](./tracer_architecture_v1.md) | **Design:** [tracer_design.md](./tracer_design.md)

---

<!-- PHASE 1 TRACKER BEGIN -->

## Phase 1 — Core Foundation: Interfaces, Storage, Mock Data Source, Test Harness

**Goal:** [tracer_phase1_design.md §1](./tracer_phase1_design.md#1-phase-1-scope-and-goals) — data model, storage, mock adapter, test scaffolding. No user-facing functionality yet.

**Phase success criteria:** All phase 1 success conditions from [tracer_phase1_design.md §1.3](./tracer_phase1_design.md#13-success-criteria) are met AND all Phase 1 integration tests pass.

- [x] **TRC-P1-001** Solution & Project Scaffold — [details](./TASK-DETAIL.md#trc-p1-001--solution--project-scaffold)
- [x] **TRC-P1-002** Tracer.Core: Domain Types — [details](./TASK-DETAIL.md#trc-p1-002--tracercore-domain-types)
- [x] **TRC-P1-003** Tracer.Core: Abstractions & Error Types — [details](./TASK-DETAIL.md#trc-p1-003--tracercore-abstractions--error-types)
- [x] **TRC-P1-004** Tracer.Core: Query Model — [details](./TASK-DETAIL.md#trc-p1-004--tracercore-query-model)
- [x] **TRC-P1-005** Tracer.Storage.DuckDB: Schema & Appenders — [details](./TASK-DETAIL.md#trc-p1-005--tracerstorageduckdb-schema--appenders)
- [x] **TRC-P1-006** Tracer.Storage.DuckDB: Query Layer — [details](./TASK-DETAIL.md#trc-p1-006--tracerstorageduckdb-query-layer)
- [x] **TRC-P1-007** Tracer.Adapters.Mock: MockDataSource & SimulatedClock — [details](./TASK-DETAIL.md#trc-p1-007--traceradaptersmock-mockdatasource--simulatedclock)
- [x] **TRC-P1-008** Tracer.Adapters.Mock: Scenario System — [details](./TASK-DETAIL.md#trc-p1-008--traceradaptersmock-scenario-system)
- [x] **TRC-P1-009** Tracer.TestHarness — [details](./TASK-DETAIL.md#trc-p1-009--tracertestharness)
- [x] **TRC-P1-010** Unit Tests: Core & Storage — [details](./TASK-DETAIL.md#trc-p1-010--unit-tests-core--storage)
- [x] **TRC-P1-011** Unit Tests: Mock Adapter — [details](./TASK-DETAIL.md#trc-p1-011--unit-tests-mock-adapter)
- [x] **TRC-P1-012** Integration Tests: End-to-End — [details](./TASK-DETAIL.md#trc-p1-012--integration-tests-end-to-end)

<!-- PHASE 1 TRACKER END -->

<!-- PHASE 2 TRACKER BEGIN -->

## Phase 2 — TracerAgent, Interval Rotation, Fast State, FakeNode

**Goal:** [tracer_phase2_design.md §1](./tracer_phase2_design.md#1-phase-2-scope-and-goals)

**Phase success criteria:** All conditions from [tracer_phase2_design.md §1.3](./tracer_phase2_design.md#13-success-criteria) AND all Phase 1 and Phase 2 integration tests pass.

- [x] **TRC-P2-001** New Core Abstractions [details](./TASK-DETAIL.md#trc-p2-001--new-core-abstractions-in-tracercore)
- [x] **TRC-P2-002** Fast-State Parquet Writers [details](./TASK-DETAIL.md#trc-p2-002--fast-state-parquet-writers)
- [x] **TRC-P2-003** Agent Configuration & DI [details](./TASK-DETAIL.md#trc-p2-003--agent-configuration--di)
- [x] **TRC-P2-004** Ingestion Pipeline [details](./TASK-DETAIL.md#trc-p2-004--ingestion-pipeline)
- [x] **TRC-P2-005** Interval Rotation Lifecycle [details](./TASK-DETAIL.md#trc-p2-005--interval-rotation-lifecycle)
- [x] **TRC-P2-006** Startup Recovery [details](./TASK-DETAIL.md#trc-p2-006--startup-recovery)
- [x] **TRC-P2-007** Upload & Retention [details](./TASK-DETAIL.md#trc-p2-007--upload--retention)
- [x] **TRC-P2-008** Mock Transport & Upload [details](./TASK-DETAIL.md#trc-p2-008--mock-transport--upload)
- [x] **TRC-P2-009** FakeNode [details](./TASK-DETAIL.md#trc-p2-009--fakenode)
- [x] **TRC-P2-010** TestHarness Phase 2 Additions [details](./TASK-DETAIL.md#trc-p2-010--testharness-phase-2-additions)
- [x] **TRC-P2-011** Agent Unit Tests [details](./TASK-DETAIL.md#trc-p2-011--agent-unit-tests)
- [x] **TRC-P2-012** Agent Integration Tests [details](./TASK-DETAIL.md#trc-p2-012--agent-integration-tests)

<!-- PHASE 2 TRACKER END -->

<!-- PHASE 3 TRACKER BEGIN -->

## Phase 3 — TracerObserver, Web API, Vue SPA, Session Browser & Scenario View

**Goal:** [tracer_phase3_design.md §1](./tracer_phase3_design.md#1-phase-3-scope-and-goals)

**Phase success criteria:** All conditions from [tracer_phase3_design.md §1.3](./tracer_phase3_design.md#13-success-criteria) AND all Phase 1, 2, and 3 integration tests pass.

- [ ] **TRC-P3-001** Tracer.Observer Assembly [details](./TASK-DETAIL.md#trc-p3-001--tracerObserver-assembly)
- [ ] **TRC-P3-002** Tracer.WebApi Setup & Middleware [details](./TASK-DETAIL.md#trc-p3-002--tracerwebapi-setup--middleware)
- [ ] **TRC-P3-003** Session & Topology Endpoints [details](./TASK-DETAIL.md#trc-p3-003--session--topology-endpoints)
- [ ] **TRC-P3-004** Scenario & Event Endpoints [details](./TASK-DETAIL.md#trc-p3-004--scenario--event-endpoints)
- [ ] **TRC-P3-005** SSE Live Streaming [details](./TASK-DETAIL.md#trc-p3-005--sse-live-streaming)
- [ ] **TRC-P3-006** Vue SPA Scaffold [details](./TASK-DETAIL.md#trc-p3-006--vue-spa-scaffold)
- [ ] **TRC-P3-007** Session Browser View [details](./TASK-DETAIL.md#trc-p3-007--session-browser-view)
- [ ] **TRC-P3-008** Scenario View [details](./TASK-DETAIL.md#trc-p3-008--scenario-view)
- [ ] **TRC-P3-009** Observer+FakeNode Integration Tests [details](./TASK-DETAIL.md#trc-p3-009--observerfakenode-integration-tests)
- [ ] **TRC-P3-010** Web API Query Round-Trip Tests [details](./TASK-DETAIL.md#trc-p3-010--web-api-query-round-trip-tests)
- [ ] **TRC-P3-011** Live Streaming Integration Tests [details](./TASK-DETAIL.md#trc-p3-011--live-streaming-integration-tests)
- [ ] **TRC-P3-012** Frontend Component Tests [details](./TASK-DETAIL.md#trc-p3-012--frontend-component-tests)
- [ ] **TRC-P3-013** Playwright E2E Smoke Tests [details](./TASK-DETAIL.md#trc-p3-013--playwright-e2e-smoke-tests)

<!-- PHASE 3 TRACKER END -->

<!-- PHASE 4 TRACKER BEGIN -->

## Phase 4 — TracerAggregator, Bundle Format, Offline Viewer, Self-Contained Packaging

**Goal:** [tracer_phase4_design.md §1](./tracer_phase4_design.md#1-phase-4-scope-and-goals)

**Phase success criteria:** All conditions from [tracer_phase4_design.md §1.3](./tracer_phase4_design.md#13-success-criteria) AND all Phase 1–4 integration tests pass.

- [ ] **TRC-P4-001** Bundle Format [details](./TASK-DETAIL.md#trc-p4-001--bundle-format)
- [ ] **TRC-P4-002** Bundle Packaging [details](./TASK-DETAIL.md#trc-p4-002--bundle-packaging)
- [ ] **TRC-P4-003** Bundle Validation [details](./TASK-DETAIL.md#trc-p4-003--bundle-validation)
- [ ] **TRC-P4-004** MultiIntervalReader [details](./TASK-DETAIL.md#trc-p4-004--multiintervalreader)
- [ ] **TRC-P4-005** Aggregation Core [details](./TASK-DETAIL.md#trc-p4-005--aggregation-core)
- [ ] **TRC-P4-006** Aggregation Consolidators [details](./TASK-DETAIL.md#trc-p4-006--aggregation-consolidators)
- [ ] **TRC-P4-007** tracer-aggregate.exe CLI [details](./TASK-DETAIL.md#trc-p4-007--tracer-aggregateexe-cli)
- [ ] **TRC-P4-008** OfflineViewer [details](./TASK-DETAIL.md#trc-p4-008--offlineviewer)
- [ ] **TRC-P4-009** Web API Bundle Mode [details](./TASK-DETAIL.md#trc-p4-009--web-api-bundle-mode)
- [ ] **TRC-P4-010** Self-Contained Distribution [details](./TASK-DETAIL.md#trc-p4-010--self-contained-distribution)
- [ ] **TRC-P4-011** TestHarness Phase 4 Additions [details](./TASK-DETAIL.md#trc-p4-011--testharness-phase-4-additions)
- [ ] **TRC-P4-012** Bundle & Aggregator Unit Tests [details](./TASK-DETAIL.md#trc-p4-012--bundle--aggregator-unit-tests)
- [ ] **TRC-P4-013** Bundle Round-Trip Integration Tests [details](./TASK-DETAIL.md#trc-p4-013--bundle-round-trip-integration-tests)

<!-- PHASE 4 TRACKER END -->

<!-- PHASE 5 TRACKER BEGIN -->

## Phase 5 — Engineer Timeline View, Canvas Rendering, Live Multi-Interval Queries

**Goal:** [tracer_phase5_design.md §1](./tracer_phase5_design.md#1-phase-5-scope-and-goals)

**Phase success criteria:** All conditions from [tracer_phase5_design.md §1.3](./tracer_phase5_design.md#13-success-criteria) AND all Phase 1–5 integration tests pass.

- [ ] **TRC-P5-001** LiveMultiIntervalReader & IntervalSetTracker [details](./TASK-DETAIL.md#trc-p5-001--livemultiintervalreader--intervalsettracker)
- [ ] **TRC-P5-002** /api/events List & Aggregate Endpoints [details](./TASK-DETAIL.md#trc-p5-002--apievents-list--aggregate-endpoints)
- [ ] **TRC-P5-003** Extended SSE for Filtered Events [details](./TASK-DETAIL.md#trc-p5-003--extended-sse-for-filtered-events)
- [ ] **TRC-P5-004** Timeline Canvas Renderer [details](./TASK-DETAIL.md#trc-p5-004--timeline-canvas-renderer)
- [ ] **TRC-P5-005** TimelineView Vue Components [details](./TASK-DETAIL.md#trc-p5-005--timelineview-vue-components)
- [ ] **TRC-P5-006** Timeline Composables & Store [details](./TASK-DETAIL.md#trc-p5-006--timeline-composables--store)
- [ ] **TRC-P5-007** FilterPanel & EventInspector [details](./TASK-DETAIL.md#trc-p5-007--filterpanel--eventinspector)
- [ ] **TRC-P5-008** Bundle Library UI [details](./TASK-DETAIL.md#trc-p5-008--bundle-library-ui)
- [ ] **TRC-P5-009** Shareable URLs & URL State [details](./TASK-DETAIL.md#trc-p5-009--shareable-urls--url-state)
- [ ] **TRC-P5-010** Auto-Follow Live Mode [details](./TASK-DETAIL.md#trc-p5-010--auto-follow-live-mode)
- [ ] **TRC-P5-011** Backend Unit Tests [details](./TASK-DETAIL.md#trc-p5-011--backend-unit-tests)
- [ ] **TRC-P5-012** Backend Integration Tests [details](./TASK-DETAIL.md#trc-p5-012--backend-integration-tests)
- [ ] **TRC-P5-013** Frontend Tests [details](./TASK-DETAIL.md#trc-p5-013--frontend-tests)

<!-- PHASE 5 TRACKER END -->

<!-- PHASE 6 TRACKER BEGIN -->

## Phase 6 — Causal Tree View, Trace Walking, Cross-View Navigation

**Goal:** [tracer_phase6_design.md §1](./tracer_phase6_design.md#1-phase-6-scope-and-goals)

**Phase success criteria:** All conditions from [tracer_phase6_design.md §1.3](./tracer_phase6_design.md#13-success-criteria) AND all Phase 1–6 integration tests pass.

- [ ] **TRC-P6-001** Schema Extension: parent_event_id Index [details](./TASK-DETAIL.md#trc-p6-001--schema-extension-parent_event_id-partial-index)
- [ ] **TRC-P6-002** Trace Walking Backend [details](./TASK-DETAIL.md#trc-p6-002--trace-walking-backend)
- [ ] **TRC-P6-003** Trace DTOs [details](./TASK-DETAIL.md#trc-p6-003--trace-dtos)
- [ ] **TRC-P6-004** Trace API Endpoints [details](./TASK-DETAIL.md#trc-p6-004--trace-api-endpoints)
- [ ] **TRC-P6-005** DAG Layout Algorithm [details](./TASK-DETAIL.md#trc-p6-005--dag-layout-algorithm)
- [ ] **TRC-P6-006** Causal Tree Canvas Renderer [details](./TASK-DETAIL.md#trc-p6-006--causal-tree-canvas-renderer-and-hit-test)
- [ ] **TRC-P6-007** CausalTreeView Vue Component [details](./TASK-DETAIL.md#trc-p6-007--causaltreeview-vue-component)
- [ ] **TRC-P6-008** Causal Tree Composables & Store [details](./TASK-DETAIL.md#trc-p6-008--causal-tree-composables-and-store)
- [ ] **TRC-P6-009** Cross-View Navigation [details](./TASK-DETAIL.md#trc-p6-009--cross-view-navigation)
- [ ] **TRC-P6-010** Shareable URL for Causal View [details](./TASK-DETAIL.md#trc-p6-010--shareable-url-for-causal-view)
- [ ] **TRC-P6-011** Backend Unit & Integration Tests [details](./TASK-DETAIL.md#trc-p6-011--backend-unit-and-integration-tests)
- [ ] **TRC-P6-012** Frontend Tests [details](./TASK-DETAIL.md#trc-p6-012--frontend-tests)

<!-- PHASE 6 TRACKER END -->
