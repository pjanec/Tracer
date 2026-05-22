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

- [x] **TRC-P3-001** Tracer.Observer Assembly [details](./TASK-DETAIL.md#trc-p3-001--tracerObserver-assembly)
- [x] **TRC-P3-002** Tracer.WebApi Setup & Middleware [details](./TASK-DETAIL.md#trc-p3-002--tracerwebapi-setup--middleware)
- [x] **TRC-P3-003** Session & Topology Endpoints [details](./TASK-DETAIL.md#trc-p3-003--session--topology-endpoints)
- [x] **TRC-P3-004** Scenario & Event Endpoints [details](./TASK-DETAIL.md#trc-p3-004--scenario--event-endpoints)
- [x] **TRC-P3-005** SSE Live Streaming [details](./TASK-DETAIL.md#trc-p3-005--sse-live-streaming)
- [x] **TRC-P3-006** Vue SPA Scaffold [details](./TASK-DETAIL.md#trc-p3-006--vue-spa-scaffold)
- [x] **TRC-P3-007** Session Browser View [details](./TASK-DETAIL.md#trc-p3-007--session-browser-view)
- [x] **TRC-P3-008** Scenario View [details](./TASK-DETAIL.md#trc-p3-008--scenario-view)
- [x] **TRC-P3-009** Observer+FakeNode Integration Tests [details](./TASK-DETAIL.md#trc-p3-009--observerfakenode-integration-tests)
- [x] **TRC-P3-010** Web API Query Round-Trip Tests [details](./TASK-DETAIL.md#trc-p3-010--web-api-query-round-trip-tests)
- [x] **TRC-P3-011** Live Streaming Integration Tests [details](./TASK-DETAIL.md#trc-p3-011--live-streaming-integration-tests)
- [x] **TRC-P3-012** Frontend Component Tests [details](./TASK-DETAIL.md#trc-p3-012--frontend-component-tests)
- [x] **TRC-P3-013** Playwright E2E Smoke Tests [details](./TASK-DETAIL.md#trc-p3-013--playwright-e2e-smoke-tests)

<!-- PHASE 3 TRACKER END -->

<!-- PHASE 4 TRACKER BEGIN -->

## Phase 4 — TracerAggregator, Bundle Format, Offline Viewer, Self-Contained Packaging

**Goal:** [tracer_phase4_design.md §1](./tracer_phase4_design.md#1-phase-4-scope-and-goals)

**Phase success criteria:** All conditions from [tracer_phase4_design.md §1.3](./tracer_phase4_design.md#13-success-criteria) AND all Phase 1–4 integration tests pass.

- [x] **TRC-P4-001** Bundle Format [details](./TASK-DETAIL.md#trc-p4-001--bundle-format)
- [x] **TRC-P4-002** Bundle Packaging [details](./TASK-DETAIL.md#trc-p4-002--bundle-packaging)
- [x] **TRC-P4-003** Bundle Validation [details](./TASK-DETAIL.md#trc-p4-003--bundle-validation)
- [x] **TRC-P4-004** MultiIntervalReader [details](./TASK-DETAIL.md#trc-p4-004--multiintervalreader)
- [x] **TRC-P4-005** Aggregation Core [details](./TASK-DETAIL.md#trc-p4-005--aggregation-core)
- [x] **TRC-P4-006** Aggregation Consolidators [details](./TASK-DETAIL.md#trc-p4-006--aggregation-consolidators)
- [x] **TRC-P4-007** tracer-aggregate.exe CLI [details](./TASK-DETAIL.md#trc-p4-007--tracer-aggregateexe-cli)
- [x] **TRC-P4-008** OfflineViewer [details](./TASK-DETAIL.md#trc-p4-008--offlineviewer)
- [x] **TRC-P4-009** Web API Bundle Mode [details](./TASK-DETAIL.md#trc-p4-009--web-api-bundle-mode)
- [x] **TRC-P4-010** Self-Contained Distribution [details](./TASK-DETAIL.md#trc-p4-010--self-contained-distribution)
- [x] **TRC-P4-011** TestHarness Phase 4 Additions [details](./TASK-DETAIL.md#trc-p4-011--testharness-phase-4-additions)
- [x] **TRC-P4-012** Bundle & Aggregator Unit Tests [details](./TASK-DETAIL.md#trc-p4-012--bundle--aggregator-unit-tests)
- [x] **TRC-P4-013** Bundle Round-Trip Integration Tests [details](./TASK-DETAIL.md#trc-p4-013--bundle-round-trip-integration-tests)

<!-- PHASE 4 TRACKER END -->

<!-- PHASE 5 TRACKER BEGIN -->

## Phase 5 — Engineer Timeline View, Canvas Rendering, Live Multi-Interval Queries

**Goal:** [tracer_phase5_design.md §1](./tracer_phase5_design.md#1-phase-5-scope-and-goals)

**Phase success criteria:** All conditions from [tracer_phase5_design.md §1.3](./tracer_phase5_design.md#13-success-criteria) AND all Phase 1–5 integration tests pass.

- [x] **TRC-P5-001** LiveMultiIntervalReader & IntervalSetTracker [details](./TASK-DETAIL.md#trc-p5-001--livemultiintervalreader--intervalsettracker)
- [x] **TRC-P5-002** /api/events List & Aggregate Endpoints [details](./TASK-DETAIL.md#trc-p5-002--apievents-list--aggregate-endpoints)
- [x] **TRC-P5-003** Extended SSE for Filtered Events [details](./TASK-DETAIL.md#trc-p5-003--extended-sse-for-filtered-events)
- [x] **TRC-P5-004** Timeline Canvas Renderer [details](./TASK-DETAIL.md#trc-p5-004--timeline-canvas-renderer)
- [x] **TRC-P5-005** TimelineView Vue Components [details](./TASK-DETAIL.md#trc-p5-005--timelineview-vue-components)
- [x] **TRC-P5-006** Timeline Composables & Store [details](./TASK-DETAIL.md#trc-p5-006--timeline-composables--store)
- [x] **TRC-P5-007** FilterPanel & EventInspector [details](./TASK-DETAIL.md#trc-p5-007--filterpanel--eventinspector)
- [x] **TRC-P5-008** Bundle Library UI [details](./TASK-DETAIL.md#trc-p5-008--bundle-library-ui)
- [x] **TRC-P5-009** Shareable URLs & URL State [details](./TASK-DETAIL.md#trc-p5-009--shareable-urls--url-state)
- [x] **TRC-P5-010** Auto-Follow Live Mode [details](./TASK-DETAIL.md#trc-p5-010--auto-follow-live-mode)
- [x] **TRC-P5-011** Backend Unit Tests [details](./TASK-DETAIL.md#trc-p5-011--backend-unit-tests)
- [x] **TRC-P5-012** Backend Integration Tests [details](./TASK-DETAIL.md#trc-p5-012--backend-integration-tests)
- [x] **TRC-P5-013** Frontend Tests [details](./TASK-DETAIL.md#trc-p5-013--frontend-tests)

<!-- PHASE 5 TRACKER END -->

<!-- PHASE 6 TRACKER BEGIN -->

## Phase 6 — Causal Tree View, Trace Walking, Cross-View Navigation

**Goal:** [tracer_phase6_design.md §1](./tracer_phase6_design.md#1-phase-6-scope-and-goals)

**Phase success criteria:** All conditions from [tracer_phase6_design.md §1.3](./tracer_phase6_design.md#13-success-criteria) AND all Phase 1–6 integration tests pass.

- [x] **TRC-P6-001** Schema Extension: parent_event_id Index [details](./TASK-DETAIL.md#trc-p6-001--schema-extension-parent_event_id-partial-index)
- [x] **TRC-P6-002** Trace Walking Backend [details](./TASK-DETAIL.md#trc-p6-002--trace-walking-backend)
- [x] **TRC-P6-003** Trace DTOs [details](./TASK-DETAIL.md#trc-p6-003--trace-dtos)
- [x] **TRC-P6-004** Trace API Endpoints [details](./TASK-DETAIL.md#trc-p6-004--trace-api-endpoints)
- [x] **TRC-P6-005** DAG Layout Algorithm [details](./TASK-DETAIL.md#trc-p6-005--dag-layout-algorithm)
- [x] **TRC-P6-006** Causal Tree Canvas Renderer [details](./TASK-DETAIL.md#trc-p6-006--causal-tree-canvas-renderer-and-hit-test)
- [x] **TRC-P6-007** CausalTreeView Vue Component [details](./TASK-DETAIL.md#trc-p6-007--causaltreeview-vue-component)
- [x] **TRC-P6-008** Causal Tree Composables & Store [details](./TASK-DETAIL.md#trc-p6-008--causal-tree-composables-and-store)
- [x] **TRC-P6-009** Cross-View Navigation [details](./TASK-DETAIL.md#trc-p6-009--cross-view-navigation)
- [x] **TRC-P6-010** Shareable URL for Causal View [details](./TASK-DETAIL.md#trc-p6-010--shareable-url-for-causal-view)
- [x] **TRC-P6-011** Backend Unit & Integration Tests [details](./TASK-DETAIL.md#trc-p6-011--backend-unit-and-integration-tests)
- [x] **TRC-P6-012** Frontend Tests [details](./TASK-DETAIL.md#trc-p6-012--frontend-tests)

<!-- PHASE 6 TRACKER END -->

<!-- PHASE 7 TRACKER BEGIN -->

## Phase 7 — Entity History View, Slow State Time Series, Fast State Drill-Down

**Goal:** [tracer_phase7_design.md §1](./tracer_phase7_design.md#1-phase-7-scope-and-goals)

**Phase success criteria:** All conditions from [tracer_phase7_design.md §1.3](./tracer_phase7_design.md#13-success-criteria) AND all Phase 1–6 tests pass.

- [x] **TRC-P7-001** Tracer.Storage.Parquet Assembly [details](./TASK-DETAIL.md#trc-p7-001--tracerstorageparquet-assembly)
- [x] **TRC-P7-002** Schema Extension: slow_state Entity-Time Index [details](./TASK-DETAIL.md#trc-p7-002--schema-extension-slow_state-entity-time-index)
- [x] **TRC-P7-003** EntityDiscoveryService [details](./TASK-DETAIL.md#trc-p7-003--entitydiscoveryservice)
- [x] **TRC-P7-004** EntityEventsService [details](./TASK-DETAIL.md#trc-p7-004--entityeventsservice)
- [x] **TRC-P7-005** EntitySlowStateService [details](./TASK-DETAIL.md#trc-p7-005--entityslowstateservice)
- [x] **TRC-P7-006** BuildSlowStateUnionSql Extension [details](./TASK-DETAIL.md#trc-p7-006--buildslowstateunionsql-extension)
- [x] **TRC-P7-007** FastStateFileLocator [details](./TASK-DETAIL.md#trc-p7-007--faststatefilelocator)
- [x] **TRC-P7-008** EntityFastStateService [details](./TASK-DETAIL.md#trc-p7-008--entityfaststateservice)
- [x] **TRC-P7-009** Entity Web API Endpoints, DTOs, and Wiring [details](./TASK-DETAIL.md#trc-p7-009--entity-web-api-endpoints-dtos-and-wiring)
- [x] **TRC-P7-010** `EntityHistoryView.vue` — View Layout and Shared Time Axis [details](./TASK-DETAIL.md#trc-p7-010--entityhistoryviewvue--view-layout-and-shared-time-axis)
- [x] **TRC-P7-011** `EntityLifecycleRibbon.vue` — Spawn/Ownership/Destruction Band [details](./TASK-DETAIL.md#trc-p7-011--entitylifecycleribbonvue--spawnownershipdestruction-band)
- [x] **TRC-P7-012** `EntityEventStrip.vue` — Event Markers on Timeline [details](./TASK-DETAIL.md#trc-p7-012--entityeventstripvue--event-markers-on-timeline)
- [x] **TRC-P7-013** `SlowStateChart.vue` and `slowStateChartRenderer.ts` [details](./TASK-DETAIL.md#trc-p7-013--slowstatechartsvue-and-slowstatechartrenderersdts)
- [x] **TRC-P7-014** `FastStateDrillDown.vue`, `FastStateColumnPicker.vue`, and `fastStateChartRenderer.ts` [details](./TASK-DETAIL.md#trc-p7-014--faststatdrilldownvue-faststatcolumnpickervue-and-faststatechartrenderersdts)
- [x] **TRC-P7-015** `useEntityHistoryQuery.ts` and `entityHistoryStore.ts` [details](./TASK-DETAIL.md#trc-p7-015--useentityhistoryqueryts-and-entityhistorystorets-fetch-orchestration)
- [x] **TRC-P7-016** `useEntityHistoryUrl.ts` — URL State [details](./TASK-DETAIL.md#trc-p7-016--useentityhistoryurlts--url-state)
- [x] **TRC-P7-017** `useFastStateChart.ts` — On-Demand Fast State [details](./TASK-DETAIL.md#trc-p7-017--usefaststatechartts--on-demand-fast-state)
- [x] **TRC-P7-018** Cross-View Navigation Pivots [details](./TASK-DETAIL.md#trc-p7-018--cross-view-navigation-pivots)
- [x] **TRC-P7-019** Entity Discovery in Session Browser [details](./TASK-DETAIL.md#trc-p7-019--entity-discovery-in-session-browser)
- [x] **TRC-P7-020** Phase 7 Tests (Backend Unit, Integration, Frontend, E2E) [details](./TASK-DETAIL.md#trc-p7-020--phase-7-tests-backend-unit-integration-frontend-e2e)

<!-- PHASE 7 TRACKER END -->

<!-- PHASE 8 TRACKER BEGIN -->

## Phase 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish

**Goal:** [tracer_phase8_design.md §1](./tracer_phase8_design.md#1-phase-8-scope-and-goals)

**Phase success criteria:** All conditions from [tracer_phase8_design.md §1.3](./tracer_phase8_design.md#13-success-criteria) AND all Phase 1–7 tests pass.

- [x] **TRC-P8-001** Tracer.Storage.Annotations Assembly [details](./TASK-DETAIL.md#trc-p8-001--tracerstorageannotations-assembly)
- [x] **TRC-P8-002** SqliteAnnotationStore [details](./TASK-DETAIL.md#trc-p8-002--sqliteannotationstore)
- [x] **TRC-P8-003** BundleAnnotationStore [details](./TASK-DETAIL.md#trc-p8-003--bundleannotationstore)
- [x] **TRC-P8-004** Tracer.Storage.SavedViews Assembly [details](./TASK-DETAIL.md#trc-p8-004--tracerstoragesavedviews-assembly)
- [x] **TRC-P8-005** Annotation REST API Endpoints [details](./TASK-DETAIL.md#trc-p8-005--annotation-rest-api-endpoints)
- [x] **TRC-P8-006** Saved Views REST API Endpoints [details](./TASK-DETAIL.md#trc-p8-006--saved-views-rest-api-endpoints)
- [x] **TRC-P8-007** TriggerEvalService [details](./TASK-DETAIL.md#trc-p8-007--triggerevalservice)
- [x] **TRC-P8-008** Trigger Evaluation API Endpoints [details](./TASK-DETAIL.md#trc-p8-008--trigger-evaluation-api-endpoints)
- [x] **TRC-P8-009** AnnotationsExporter [details](./TASK-DETAIL.md#trc-p8-009--annotationsexporter)
- [x] **TRC-P8-010** Lifecycle Topic Configuration [details](./TASK-DETAIL.md#trc-p8-010--lifecycle-topic-configuration)
- [x] **TRC-P8-011** `AnnotationMarker.vue` and Annotation Overlay Integration [details](./TASK-DETAIL.md#trc-p8-011--annotationmarkervue-and-annotation-overlay-integration)
- [x] **TRC-P8-012** `AnnotationEditor.vue` and `AnnotationList.vue` [details](./TASK-DETAIL.md#trc-p8-012--annotationeditorvue-and-annotationlistvue)
- [x] **TRC-P8-013** `useAnnotations.ts` and `annotationStore.ts` [details](./TASK-DETAIL.md#trc-p8-013--useannotationsts-and-annotationstorets)
- [x] **TRC-P8-014** `SavedViewsView.vue` and `SaveViewButton.vue` [details](./TASK-DETAIL.md#trc-p8-014--savedviewsviewvue-and-saveviewbuttonvue)
- [x] **TRC-P8-015** `BookmarkBar.vue` and `useBookmarks.ts` [details](./TASK-DETAIL.md#trc-p8-015--bookmarkbarvue-and-usebookmarksts)
- [x] **TRC-P8-016** `TriggerEvalView.vue` and `TriggerEvalRow.vue` [details](./TASK-DETAIL.md#trc-p8-016--triggerevalviewvue-and-triggerevalrowvue)
- [x] **TRC-P8-017** `PersonaSwitcher.vue`, `usePersona.ts`, and `personaStore.ts` [details](./TASK-DETAIL.md#trc-p8-017--personaswitchervue-usepersonats-and-personastorets)
- [x] **TRC-P8-018** Phase 8 Tests (Backend Unit, Integration, Frontend) [details](./TASK-DETAIL.md#trc-p8-018--phase-8-tests-backend-unit-integration-frontend)

<!-- PHASE 8 TRACKER END -->

<!-- PHASE 9 TRACKER BEGIN -->

## Phase 9 — Replication Latency, Gap Detection, Network Topology

**Goal:** [tracer_phase9_design.md §1](./tracer_phase9_design.md#1-phase-9-scope-and-goals) — per-subscriber replication latency analysis, sequence-number gap detection, and network topology visualization; bundle mode only.

**Phase success criteria:** All conditions from [tracer_phase9_design.md §1.3](./tracer_phase9_design.md#13-success-criteria) AND all Phase 1–8 tests pass.

- [x] **TRC-P9-001** `LatencyBudget` and Core Latency Types — [details](./TASK-DETAIL.md#trc-p9-001--latencybudget-and-core-latency-types)
- [x] **TRC-P9-002** `FakeNetworkModel` — Synthetic Per-Subscriber Receive Times — [details](./TASK-DETAIL.md#trc-p9-002--fakenetworkmodel--synthetic-per-subscriber-receive-times)
- [x] **TRC-P9-003** `QuantileSink` and `HistogramSink` Utilities — [details](./TASK-DETAIL.md#trc-p9-003--quantilesink-and-histogramsink-utilities)
- [x] **TRC-P9-004** `LatencyDistributionService` — [details](./TASK-DETAIL.md#trc-p9-004--latencydistributionservice)
- [x] **TRC-P9-005** `LatencyTimeSeriesService` — [details](./TASK-DETAIL.md#trc-p9-005--latencytimeseriesservice)
- [x] **TRC-P9-006** `LatencyOutlierService` — [details](./TASK-DETAIL.md#trc-p9-006--latencyoutlierservice)
- [x] **TRC-P9-007** `GapDetectionService` — [details](./TASK-DETAIL.md#trc-p9-007--gapdetectionservice)
- [x] **TRC-P9-008** `TopologyService` — [details](./TASK-DETAIL.md#trc-p9-008--topologyservice)
- [x] **TRC-P9-009** `BudgetService` — [details](./TASK-DETAIL.md#trc-p9-009--budgetservice)
- [x] **TRC-P9-010** Phase 9 API Endpoints, DTOs, `BundleModeGate`, and DI Wiring — [details](./TASK-DETAIL.md#trc-p9-010--phase-9-api-endpoints-dtos-bundlemodelgate-and-di-wiring)
- [x] **TRC-P9-011** `ReplicationLatencyView.vue` — Main Latency View [details](./TASK-DETAIL.md#trc-p9-011--replicationlatencyviewvue--main-latency-view)
- [x] **TRC-P9-012** `LatencyDistributionChart.vue` and `histogramRenderer.ts` [details](./TASK-DETAIL.md#trc-p9-012--latencydistributionchartvue-and-histogramrendererts)
- [x] **TRC-P9-013** `LatencyTimeSeriesChart.vue` [details](./TASK-DETAIL.md#trc-p9-013--latencytimeserieschartvue)
- [x] **TRC-P9-014** `LatencyOutliersTable.vue` and Cross-View Pivot [details](./TASK-DETAIL.md#trc-p9-014--latencyoutlierstablevue-and-cross-view-pivot)
- [x] **TRC-P9-015** `PublisherSubscriberMatrix.vue` [details](./TASK-DETAIL.md#trc-p9-015--publishersubscribermatrixvue)
- [x] **TRC-P9-016** `GapDetectionView.vue` and `GapList.vue` [details](./TASK-DETAIL.md#trc-p9-016--gapdetectionviewvue-and-gaplistvue)
- [x] **TRC-P9-017** `NetworkTopologyView.vue` and `NetworkGraphCanvas.vue` [details](./TASK-DETAIL.md#trc-p9-017--networktopologyviewvue-and-networkgraphcanvasvue)
- [x] **TRC-P9-018** Composables: `useLatencyDistribution`, `useLatencyTimeSeries`, `useLatencyOutliers`, `useGapDetection`, `useTopology` [details](./TASK-DETAIL.md#trc-p9-018--composables-uselatencydistribution-uselatencytimeseries-uselatencyoutliers-usegapdetection-usetopology)
- [x] **TRC-P9-019** Phase 9 Tests (Backend Unit, Integration, Frontend) [details](./TASK-DETAIL.md#trc-p9-019--phase-9-tests-backend-unit-integration-frontend)
<!-- PHASE 9 TRACKER END -->

<!-- PHASE 10 TRACKER BEGIN -->

## Phase 10 — SQL Console, Saved Queries, Bundle Library

**Goal:** [tracer_phase10_design.md §1](./tracer_phase10_design.md#1-phase-10-scope-and-goals) — read-only SQL console with budget enforcement, saved/built-in query library, and first-class bundle library with tagging, filtering, archival, import/export.

**Phase success criteria:** All conditions from [tracer_phase10_design.md §1.3](./tracer_phase10_design.md#13-success-criteria) AND all Phase 1–9 tests pass.

- [x] **TRC-P10-001** Read-Only SQL Executor: `SqlGuardrails` and `SqlExecutorService` — [details](./TASK-DETAIL.md#trc-p10-001--read-only-sql-executor-sqlguardrails-and-sqlexecutorservice)
- [x] **TRC-P10-002** SQL API Endpoints: `/api/sql/execute`, `/api/sql/schema`, `/api/sql/explain` — [details](./TASK-DETAIL.md#trc-p10-002--sql-api-endpoints-apislexecute-apisqlschema-apisqlexplain)
- [x] **TRC-P10-003** Saved Queries Data Store — [details](./TASK-DETAIL.md#trc-p10-003--saved-queries-data-store)
- [x] **TRC-P10-004** Saved Queries API Endpoints — [details](./TASK-DETAIL.md#trc-p10-004--saved-queries-api-endpoints)
- [x] **TRC-P10-005** Built-In Saved Queries Seeding — [details](./TASK-DETAIL.md#trc-p10-005--built-in-saved-queries-seeding)
- [x] **TRC-P10-006** Bundle Library Metadata Store: `BundleLibraryService` — [details](./TASK-DETAIL.md#trc-p10-006--bundle-library-metadata-store-bundlelibraryservice)
- [x] **TRC-P10-007** Bundle Library API Endpoints — [details](./TASK-DETAIL.md#trc-p10-007--bundle-library-api-endpoints)
- [x] **TRC-P10-008** Bundle Import/Export Service — [details](./TASK-DETAIL.md#trc-p10-008--bundle-importexport-service)
- [x] **TRC-P10-009** "Show SQL for This View" Backend Template Endpoint — [details](./TASK-DETAIL.md#trc-p10-009--show-sql-for-this-view-backend-template-endpoint)
- [x] **TRC-P10-010** Phase 10 Wiring and DI — [details](./TASK-DETAIL.md#trc-p10-010--phase-10-wiring-and-di)
- [x] **TRC-P10-011** `SqlConsoleView.vue` — Editor and Result Table — [details](./TASK-DETAIL.md#trc-p10-011--sqlconsoleviewvue--editor-and-result-table)
- [x] **TRC-P10-012** SQL Console Chart View — [details](./TASK-DETAIL.md#trc-p10-012--sql-console-chart-view)
- [x] **TRC-P10-013** `SavedQueriesView.vue` — [details](./TASK-DETAIL.md#trc-p10-013--savedqueriesviewvue)
- [x] **TRC-P10-014** "Save Query" and "Open in SQL Console" Affordances — [details](./TASK-DETAIL.md#trc-p10-014--save-query-and-open-in-sql-console-affordances)
- [x] **TRC-P10-015** `BundleLibraryView.vue` — Full Bundle Library — [details](./TASK-DETAIL.md#trc-p10-015--bundlelibraryviewvue--full-bundle-library)
- [x] **TRC-P10-016** "Show SQL for This View" Affordance — [details](./TASK-DETAIL.md#trc-p10-016--show-sql-for-this-view-affordance)
- [x] **TRC-P10-017** Run-and-Pivot from SQL Results — [details](./TASK-DETAIL.md#trc-p10-017--run-and-pivot-from-sql-results)
- [x] **TRC-P10-018** Phase 10 Tests — [details](./TASK-DETAIL.md#trc-p10-018--phase-10-tests)

<!-- PHASE 10 TRACKER END -->

<!-- PHASE 11 TRACKER BEGIN -->

## Phase 11 — Real Adapter Integration: DDS, Sync, Shared Memory, NAS

**Goal:** [tracer_phase11_design.md §1](./tracer_phase11_design.md#1-phase-11-scope-and-goals) — production adapter implementations for DDS data source, shared-memory IPC transport, sync-system upload, and NAS storage reader; configuration-driven adapter selection; hardening; integration test suite.

**Phase success criteria:** All conditions from [tracer_phase11_design.md §1.3](./tracer_phase11_design.md#13-success-criteria) AND all Phase 1–10 tests pass.

- [ ] **TRC-P11-001** `Tracer.Adapters.DDS` — DDS Diagnostic Data Source [details](./TASK-DETAIL.md#trc-p11-001--traceradaptersdds-assembly--dds-diagnostic-data-source)
- [ ] **TRC-P11-002** `Tracer.Adapters.SharedMemory` — Ring Buffer IPC Transport [details](./TASK-DETAIL.md#trc-p11-002--traceradapterssharedmemory-assembly--ring-buffer-ipc-transport)
- [ ] **TRC-P11-003** `Tracer.Adapters.Sync` — Telemetry Upload via Sync System [details](./TASK-DETAIL.md#trc-p11-003--traceradapterssync-assembly--telemetry-upload-via-sync-system)
- [ ] **TRC-P11-004** `Tracer.Adapters.Nas` — NAS Storage Reader [details](./TASK-DETAIL.md#trc-p11-004--traceradaptersnas-assembly--nas-storage-reader)
- [ ] **TRC-P11-005** `Tracer.AdapterSelection` — Adapter Registry and DI [details](./TASK-DETAIL.md#trc-p11-005--traceradapterselection-assembly--adapter-registry-and-di)
- [ ] **TRC-P11-006** Configuration Additions — `appsettings.json` Adapter Sections [details](./TASK-DETAIL.md#trc-p11-006--configuration-additions--appsetingsjson-adapter-sections)
- [ ] **TRC-P11-007** Hardening — Resource Limits, Back-Pressure, and Error Recovery [details](./TASK-DETAIL.md#trc-p11-007--hardening--resource-limits-back-pressure-and-error-recovery)
- [ ] **TRC-P11-008** Integration Test Infrastructure — `Tracer.Tests.Integration.Real` [details](./TASK-DETAIL.md#trc-p11-008--integration-test-infrastructure--tracertestsintegrationreal)
- [ ] **TRC-P11-009** Soak Test and Final Validation [details](./TASK-DETAIL.md#trc-p11-009--soak-test-and-final-validation)

<!-- PHASE 11 TRACKER END -->
