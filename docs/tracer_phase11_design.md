# Tracer Phase 11 — Detailed Design
## Real Adapter Integration: DDS, Sync, Shared Memory, NAS

*Companion to `tracer_architecture_v1.md` and `tracer_phase1_design.md` through `tracer_phase10_design.md`*
*Phase 11 of the build sequence — the final scoped phase (architecture §18)*
*C# / .NET 8 · Cyclone DDS · Customer's sync system · May 2026*

*Phases 1-10 designed and built Tracer against mock adapters. The full system runs end-to-end on synthetic data, exercises every code path, hits its performance targets. Phase 11 is the moment Tracer meets the customer's actual environment. The interfaces Phase 1 defined finally get their production implementations.*

*The architectural promise from §6 is that this is a focused effort at the adapter layer, not a system-wide refactor. Phase 11 is the test of that promise. If it holds, Phase 11 is bounded integration work. If it doesn't, Phase 11 surfaces real architectural concerns that Phases 1-10 didn't anticipate.*

*Phase 11 has no UI work. It builds four adapter assemblies (`Tracer.Adapters.DDS`, `Tracer.Adapters.Sync`, `Tracer.Adapters.SharedMemory`, `Tracer.Adapters.Nas`), wires them as alternatives to the mocks via configuration, and validates the end-to-end pipeline against the customer's actual simulation engine. Integration testing is the bulk of the work.*

---

## 1. Phase 11 Scope and Goals

### 1.1 What Phase 11 Delivers

**Four Real Adapter Assemblies**

- `Tracer.Adapters.DDS` — `DdsDiagnosticDataSource` implementing `IDiagnosticDataSource`. Subscribes to the simulation's DDS topics in loopback mode (same machine), translates DDS samples to `DiagnosticRecord` instances.
- `Tracer.Adapters.SharedMemory` — `SharedMemoryTransport` implementing `IAgentTransport`. Production-grade IPC between the simulation engine and the TracerAgent, replacing the in-process channel transport.
- `Tracer.Adapters.Sync` — `SyncSystemUploadService` implementing `ITelemetryUploadService`. Per-node upload of per-interval data via the customer's sync system's Telemetry category (per the sync addendum from Phase 0).
- `Tracer.Adapters.Nas` — `NasStorageReader` implementing `ITelemetryStorageReader`. Read aggregator-side access to NAS-resident telemetry from upload-completed nodes (SMB-based).

**Configuration: Adapter Selection**

- TracerAgent's `appsettings.json` (and per-environment overrides) gain a `dataSource` and `transport` section selecting which adapter implementation to instantiate
- Aggregator gains a `storageReader` section
- Mock adapters remain available for testing — the `mock` value in config selects them
- Production deployments default to the real adapters

**End-to-End Integration**

- The full Tracer system (Agent + Observer + Aggregator + Viewer) runs against the customer's actual simulation engine
- Real DDS samples flow into agents
- Real shared-memory IPC carries from simulation to agent
- Real intervals upload to NAS via the sync system
- Real bundles built from real session data
- All Phase 1-10 features validated against real data

**Hardening**

- Resource limits enforced (memory caps, file handle caps, network bandwidth caps)
- Graceful degradation under load (back-pressure, drop policies, telemetry on drops)
- Error recovery for transient infrastructure failures
- Monitoring hooks: structured log events for every adapter operation; per-adapter metrics

**Integration Test Suite**

- A new test category, "integration-real", run separately from the unit/integration test suites
- Tests run against a co-located simulation harness provided by the customer
- Validates: trace context propagation across real DDS, fast-state Parquet shape parity, sequence-number monotonicity, latency budget assertions on real data

### 1.2 What Phase 11 Does NOT Deliver

- **No new analytical views** — Phases 1-10 cover the view set. Phase 11 might surface needs for new views, but adding them is outside Phase 11's scope.
- **No simulation-side changes** — Tracer integrates with the simulation as it exists. Any required simulation changes (trace context propagation discipline, ensuring DDS sample timestamps are set) are documented as the **integration project's responsibility** and listed in handoff notes. The simulation team makes those changes; Tracer doesn't reach into the simulation codebase.
- **No sync-system development** — the sync system's Telemetry category is built by the sync team (per `sync_addendum_telemetry.md` from Phase 0). Tracer consumes the contract; it does not implement the sync side.
- **No NAS infrastructure work** — Tracer reads from where the sync system puts data. Provisioning, replication, retention of the NAS storage are operations concerns.
- **No alerting integration** — Tracer surfaces issues via its UI; tying it to operational alerting systems (PagerDuty, Slack, etc.) is out of scope.
- **No multi-NAS, multi-master, or multi-region topologies** — single-NAS, single-master is the assumption (architecture §1.2).
- **No production deployment automation** — operations team owns deployment scripts and CI/CD. Phase 11 produces deployable assemblies and documents their configuration; it does not own how the customer rolls them out.
- **Cyclone DDS bindings are customer-supplied** — the customer's Cyclone DDS C# bindings are not reimplemented; Tracer consumes them as an external dependency.

### 1.3 Success Criteria

1. **DDS data flows**: the customer's simulation, running with the DDS-loopback configuration, delivers samples to a TracerAgent. The agent's DuckDB shows events matching what was published.
2. **Shared memory transport stable**: simulation writes via `SharedMemoryTransport`, agent reads, no data loss under sustained 1000 events/sec throughput.
3. **Trace context propagation**: trace_id, event_id, parent_event_id round-trip through DDS → adapter → DuckDB without corruption. Verified in a multi-node integration test.
4. **Per-interval upload succeeds**: an agent's completed interval uploads to NAS via the sync system; the NAS shows the file at the expected path.
5. **Aggregator reads from NAS**: the aggregator successfully consumes uploaded intervals from multiple agents and produces a valid bundle.
6. **Cross-node receive times correct**: bundle has per-subscriber receive_wallclock values. Replication Latency view (Phase 9) renders meaningful output.
7. **Mock adapters still work**: the test suites that ran in Phases 1-10 continue to pass — Phase 11 does not regress mock-based testing.
8. **Multi-day soak**: a 48-hour continuous run of the simulation produces stable agent and observer behavior — no leaks, no unbounded queues, no degraded throughput.
9. **All Phase 1-10 tests pass**.
10. **At least one full real-data session results in a usable bundle that an engineer can productively analyze in the viewer.**

### 1.4 Estimated Duration

Six to ten calendar weeks. This is the most uncertain phase, since it depends on the customer environment's readiness and the depth of issues real integration surfaces. Distribution:

- Week 1-2: DDS adapter (loopback subscriber, sample → DiagnosticRecord translation)
- Week 2-3: SharedMemoryTransport (writer/reader, IPC primitives, back-pressure)
- Week 3-4: Sync system integration (Telemetry-category upload, retry, resume)
- Week 4-5: NAS reader (aggregator side)
- Week 5-6: End-to-end integration testing
- Week 6-8: Hardening based on real-data findings (resource limits, error recovery)
- Week 8-10: Soak testing and final polish

### 1.5 Critical Path

The DDS adapter and SharedMemoryTransport must both work before any meaningful end-to-end testing is possible. The other adapters (Sync, NAS) are lower-risk because they consume external contracts that are pre-defined.

The riskiest item is the DDS adapter's interaction with the customer's specific DDS type system and trace-context propagation discipline. If the simulation team's trace context implementation diverges from the architecture's design, that surfaces in Phase 11 and adds integration time.

---

## 2. Project Layout Additions

Building on Phase 10:

```
tracer/
  src/
    Tracer.Core/                                  (unchanged — interfaces already in place)
    Tracer.Adapters.Mock/                         (unchanged — kept for testing)
    Tracer.Adapters.DDS/                          NEW assembly
      Tracer.Adapters.DDS.csproj
      DdsDiagnosticDataSource.cs                  the IDiagnosticDataSource implementation
      DdsSubscriberFactory.cs                     creates per-topic subscribers
      DdsSampleTranslator.cs                      DDS sample → DiagnosticRecord
      DdsTraceContextExtractor.cs                 reads trace_id, event_id, parent_event_id
      DdsTopicRegistry.cs                         declared topics + their types
      Configuration/
        DdsAdapterConfig.cs
        DdsTopicSubscription.cs
    Tracer.Adapters.SharedMemory/                 NEW assembly
      Tracer.Adapters.SharedMemory.csproj
      SharedMemoryTransport.cs                    IAgentTransport implementation
      SharedMemoryWriter.cs                       producer side (simulation writes)
      SharedMemoryReader.cs                       consumer side (agent reads)
      SharedMemoryRingBuffer.cs                   the IPC primitive
      SharedMemoryDiagnosticRecordCodec.cs        marshalling
      Configuration/
        SharedMemoryConfig.cs
    Tracer.Adapters.Sync/                         NEW assembly
      Tracer.Adapters.Sync.csproj
      SyncSystemUploadService.cs                  ITelemetryUploadService implementation
      SyncMasterRestClient.cs                     HTTP/REST calls
      Configuration/
        SyncAdapterConfig.cs
    Tracer.Adapters.Nas/                          NEW assembly
      Tracer.Adapters.Nas.csproj
      NasStorageReader.cs                         ITelemetryStorageReader implementation
      SmbPathResolver.cs                          maps logical paths to UNC
      Configuration/
        NasAdapterConfig.cs
    Tracer.Agent/                                 (config additions only)
    Tracer.Observer/                              (config additions only)
    Tracer.Aggregator/                            (config additions only)
    Tracer.AdapterSelection/                      NEW assembly
      Tracer.AdapterSelection.csproj
      AdapterRegistry.cs                          chooses real vs mock from config
      AdapterRegistrationExtensions.cs            DI extension methods
  tests/
    Tracer.Tests.Integration.Real/                NEW test category
      Tracer.Tests.Integration.Real.csproj
      DdsRoundTripTests.cs
      SharedMemoryThroughputTests.cs
      SharedMemoryLossTests.cs
      SyncUploadTests.cs
      EndToEndSessionTests.cs                     the full pipeline against the real simulation
      SoakTests.cs
      TraceContextPropagationTests.cs
```

### 2.1 Dependencies

`Tracer.Adapters.DDS` depends on the customer's Cyclone DDS C# bindings — assumed available as a NuGet package or local reference assembly under the customer's name. Documented as an external dependency; Tracer doesn't ship the binding. See CycloneDDS.NET.README.md file for details on how the csharp bindings work for Cyclone DDS.

`Tracer.Adapters.SharedMemory` uses .NET's `System.IO.MemoryMappedFiles` for the shared region and `System.Threading.Semaphore` for synchronization — both BCL, no new dependencies.

`Tracer.Adapters.Sync` uses `HttpClient` (BCL) and the existing sync system contract (REST endpoints documented in `sync_addendum_telemetry.md`).

`Tracer.Adapters.Nas` uses .NET's built-in SMB support via `System.IO.File` (which on Windows transparently handles UNC paths).

No new NuGet packages beyond the customer-supplied DDS binding.

---

## 3. The DDS Adapter

### 3.1 Architectural Role

`DdsDiagnosticDataSource` is the production implementation of `IDiagnosticDataSource` (Phase 1 §6). It subscribes to the simulation's DDS topics as a **loopback subscriber** — running in the same address space as the simulation's DDS participant, capturing samples that the simulation publishes locally.

**Why loopback subscription instead of inter-process subscription?**

The customer's simulation runs in many processes per node. Each process publishes to DDS. Cross-process DDS subscription is expensive and would create network traffic from the local DDS participant. A loopback subscriber in the simulation process sees the local writes for free — no transport cost, no marshalling cost. The customer's DDS implementation supports this pattern; Tracer leverages it.

**Architectural consequence**: the `Tracer.Adapters.DDS` assembly is loaded **into the simulation's process**, alongside the simulation's own code. The TracerAgent runs as a separate Windows service, but receives data via the SharedMemoryTransport (§4) from the simulation-side DDS adapter.

```
┌─────────────────────────────────────┐    ┌──────────────────┐
│  Simulation process                  │    │  TracerAgent      │
│  ┌────────────────────────────────┐  │    │   (service)       │
│  │  Simulation code               │  │    │                   │
│  │  publishes DDS samples         │  │    │                   │
│  └──────────┬─────────────────────┘  │    │                   │
│             │                        │    │                   │
│  ┌──────────▼─────────────────────┐  │    │                   │
│  │  Local DDS participant         │  │    │                   │
│  │  (Cyclone DDS)                 │  │    │                   │
│  └──────────┬─────────────────────┘  │    │                   │
│             │                        │    │                   │
│  ┌──────────▼─────────────────────┐  │    │                   │
│  │  Tracer.Adapters.DDS           │  │    │                   │
│  │  loopback subscriber           │──┼────┼──── Shared        │
│  │  → DiagnosticRecord            │  │    │      memory       │
│  └────────────────────────────────┘  │    │       ↓            │
└─────────────────────────────────────┘    │  Reads, writes    │
                                            │  to DuckDB,        │
                                            │  manages intervals │
                                            └──────────────────┘
```

The DDS adapter has **no direct disk write responsibility** — it converts samples and pushes them across the shared-memory transport. The TracerAgent on the other side is responsible for durability.

### 3.2 IDiagnosticDataSource Interface

Recap from Phase 1 §6:

```csharp
namespace Tracer.Core.Adapters;

public interface IDiagnosticDataSource
{
    /// <summary>
    /// Starts the data source and returns an async-enumerable of records.
    /// Cancellation completes the enumerable.
    /// </summary>
    IAsyncEnumerable<DiagnosticRecord> GenerateAsync(CancellationToken ct);
}
```

The DDS adapter's job is to produce that enumerable from subscribed DDS topics.

### 3.3 DdsDiagnosticDataSource

```csharp
namespace Tracer.Adapters.DDS;

public sealed class DdsDiagnosticDataSource : IDiagnosticDataSource
{
    private readonly DdsAdapterConfig _config;
    private readonly DdsSubscriberFactory _subscriberFactory;
    private readonly DdsSampleTranslator _translator;
    private readonly ILogger<DdsDiagnosticDataSource> _logger;

    public DdsDiagnosticDataSource(
        DdsAdapterConfig config,
        DdsSubscriberFactory subscriberFactory,
        DdsSampleTranslator translator,
        ILogger<DdsDiagnosticDataSource> logger)
    {
        _config = config;
        _subscriberFactory = subscriberFactory;
        _translator = translator;
        _logger = logger;
    }

    public async IAsyncEnumerable<DiagnosticRecord> GenerateAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateBounded<DiagnosticRecord>(new BoundedChannelOptions(_config.IngestBufferSize)
        {
            FullMode = BoundedChannelFullMode.DropOldest,    // back-pressure: prefer recent
            SingleReader = true,
            SingleWriter = false
        });
        
        // Create subscribers for each declared topic. Each subscriber pumps into the channel.
        var subscribers = new List<IDisposable>();
        foreach (var topicSub in _config.Topics)
        {
            var subscriber = await _subscriberFactory.CreateAsync(
                topicSub, 
                onSample: (sample) => OnSampleReceived(sample, topicSub, channel.Writer),
                ct);
            subscribers.Add(subscriber);
        }
        
        try
        {
            while (await channel.Reader.WaitToReadAsync(ct))
            {
                while (channel.Reader.TryRead(out var record))
                    yield return record;
            }
        }
        finally
        {
            foreach (var s in subscribers) s.Dispose();
        }
    }

    private void OnSampleReceived(
        IDdsSample sample, DdsTopicSubscription topicSub, ChannelWriter<DiagnosticRecord> writer)
    {
        try
        {
            var record = _translator.Translate(sample, topicSub);
            if (record is null) return;     // translator may filter
            if (!writer.TryWrite(record))
            {
                // Channel full — DropOldest mode ate something; log telemetry
                _logger.LogWarning("Ingest channel full, dropped record for topic {Topic}", topicSub.TopicName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to translate DDS sample on topic {Topic}", topicSub.TopicName);
        }
    }
}
```

The DDS adapter's pattern: one subscriber per topic, all funneling into a single bounded channel. The async-enumerable contract is satisfied by reading from that channel.

**Back-pressure decision**: `DropOldest` is the architecturally correct choice here. Phases 1-10 designed for robust handling of dropped data (e.g., gap detection in Phase 9). What matters is that we never block the simulation's DDS callback thread — that would back-pressure the simulation itself, the worst possible failure mode. Drop oldest, log the drop, move on.

### 3.4 DdsSampleTranslator

Where the adapter does the most work: turning a DDS sample (whatever shape the customer's binding produces) into a `DiagnosticRecord`. This is necessarily customer-specific code.

The translator is pluggable per-topic — different topics produce different `DiagnosticRecord` subtypes (event vs state sample) with different payload shapes.

```csharp
namespace Tracer.Adapters.DDS;

public sealed class DdsSampleTranslator
{
    private readonly DdsTraceContextExtractor _traceExtractor;
    private readonly DdsTopicRegistry _topicRegistry;
    private readonly IClock _clock;
    private readonly ILogger<DdsSampleTranslator> _logger;

    public DdsSampleTranslator(
        DdsTraceContextExtractor traceExtractor,
        DdsTopicRegistry topicRegistry,
        IClock clock,
        ILogger<DdsSampleTranslator> logger)
    {
        _traceExtractor = traceExtractor;
        _topicRegistry = topicRegistry;
        _clock = clock;
        _logger = logger;
    }

    public DiagnosticRecord? Translate(IDdsSample sample, DdsTopicSubscription topicSub)
    {
        // 1. Determine record kind from topic metadata
        var meta = _topicRegistry.Lookup(topicSub.TopicName);
        if (meta is null)
        {
            _logger.LogWarning("Topic {Topic} not in registry; skipping sample", topicSub.TopicName);
            return null;
        }
        
        // 2. Extract publish_wallclock from the DDS source timestamp
        var publishTimestamp = sample.SourceTimestamp;
        var publishWallclock = WallclockTime.FromDateTimeOffset(publishTimestamp);
        
        // 3. Stamp receive_wallclock at translation time
        var receiveWallclock = WallclockTime.FromDateTimeOffset(_clock.UtcNow);
        
        // 4. Extract trace context from the sample payload
        var traceContext = _traceExtractor.Extract(sample, meta);
        
        // 5. Serialize the payload (only the simulation-specific fields, not trace context)
        var payloadJson = SerializePayload(sample, meta);
        
        // 6. Build the record
        return meta.Kind switch
        {
            DdsTopicKind.Event => new EventRecord
            {
                EventId            = traceContext.EventId,
                TraceId            = traceContext.TraceId,
                ParentEventId      = traceContext.ParentEventId,
                Topic              = topicSub.TopicName,
                PublishWallclock   = publishWallclock,
                ReceiveWallclock   = receiveWallclock,
                PublisherNode      = _config.PublisherNodeId,
                SubscriberNode     = _config.PublisherNodeId,   // loopback: same as publisher
                SequenceNumber     = sample.SequenceNumber,
                EntityId           = ExtractEntityId(sample, meta),
                OwningPlayerId     = ExtractPlayerId(sample, meta),
                ScenarioPhase      = ExtractScenarioPhase(sample, meta),
                Severity           = ExtractSeverity(sample, meta),
                NotableLabel       = ExtractNotableLabel(sample, meta),
                PayloadJson        = payloadJson,
            },
            DdsTopicKind.SlowState => new StateSampleRecord
            {
                Topic              = topicSub.TopicName,
                Kind               = StateSampleKind.Slow,
                PublishWallclock   = publishWallclock,
                ReceiveWallclock   = receiveWallclock,
                PublisherNode      = _config.PublisherNodeId,
                SubscriberNode     = _config.PublisherNodeId,
                EntityId           = ExtractEntityId(sample, meta),
                InstanceKey        = ExtractInstanceKey(sample, meta),
                PayloadJson        = payloadJson,
                TraceId            = traceContext.TraceId,
            },
            DdsTopicKind.FastState => new StateSampleRecord
            {
                Topic              = topicSub.TopicName,
                Kind               = StateSampleKind.Fast,
                PublishWallclock   = publishWallclock,
                ReceiveWallclock   = receiveWallclock,
                PublisherNode      = _config.PublisherNodeId,
                SubscriberNode     = _config.PublisherNodeId,
                EntityId           = ExtractEntityId(sample, meta),
                InstanceKey        = ExtractInstanceKey(sample, meta),
                // Fast state's typed columns come from reflection on the sample type
                TypedValues        = ExtractTypedValues(sample, meta),
                TraceId            = 0,    // fast state typically not on a trace
            },
            _ => null
        };
    }

    // ExtractEntityId, ExtractPlayerId, ExtractScenarioPhase, ExtractSeverity, ExtractNotableLabel,
    // ExtractInstanceKey, ExtractTypedValues, SerializePayload — all reflective access into the
    // sample's typed payload, guided by the DdsTopicMetadata.

    private DdsAdapterConfig _config;   // injected for PublisherNodeId
}
```

The translator is necessarily long because it bridges two type systems. Key choices:

- **Source timestamp = publish_wallclock**: the architecture requires this (§3 — publisher's synchronized wall-clock). The customer's simulation must call `dds_write_ts()` to stamp samples with their wall-clock time. Documented as integration project responsibility.
- **Receive timestamp at translation**: the adapter stamps `receive_wallclock` when it gets the sample. In loopback this is microseconds after publish; for the published-once data shape it's effectively the same node's receive time.
- **Publisher = subscriber in loopback**: the DDS loopback subscriber represents the publisher's own observation of its publish. The bundle consolidation step (Phase 4) is what adds the per-subscriber rows from other nodes' agents.
- **TypedValues for fast state**: extracted via reflection over the sample's typed properties; the Parquet writer (Phase 2) consumes these into typed columns.

### 3.5 DdsTopicRegistry

The catalog of which topics Tracer subscribes to and what their semantics are.

```csharp
namespace Tracer.Adapters.DDS;

public sealed class DdsTopicRegistry
{
    private readonly Dictionary<string, DdsTopicMetadata> _byName;

    public DdsTopicRegistry(IEnumerable<DdsTopicMetadata> topics)
    {
        _byName = topics.ToDictionary(t => t.TopicName);
    }

    public DdsTopicMetadata? Lookup(string topicName) =>
        _byName.GetValueOrDefault(topicName);
}

public sealed record DdsTopicMetadata
{
    public required string TopicName { get; init; }
    public required Type SampleType { get; init; }           // the C# type from the customer's binding
    public required DdsTopicKind Kind { get; init; }
    public required string EntityIdField { get; init; }      // payload field name carrying entity_id
    public string? OwningPlayerIdField { get; init; }
    public string? SeverityField { get; init; }
    public string? NotableLabelField { get; init; }
    public string? InstanceKeyField { get; init; }
}

public enum DdsTopicKind { Event, SlowState, FastState }
```

The registry is populated via configuration at startup:

```json
{
  "ddsTopics": [
    {
      "topicName": "weapons.fire",
      "sampleTypeName": "SimEngine.Topics.WeaponsFire, SimEngine.Topics",
      "kind": "Event",
      "entityIdField": "weaponId",
      "owningPlayerIdField": "firingPlayerId",
      "severityField": "severity",
      "notableLabelField": "notableLabel"
    },
    {
      "topicName": "scenario.phase_change",
      "sampleTypeName": "SimEngine.Topics.ScenarioPhaseChange, SimEngine.Topics",
      "kind": "Event",
      "entityIdField": "scenarioId"
    },
    {
      "topicName": "vehicle.transform",
      "sampleTypeName": "SimEngine.Topics.VehicleTransform, SimEngine.Topics",
      "kind": "FastState",
      "entityIdField": "vehicleId",
      "instanceKeyField": "vehicleId"
    }
  ]
}
```

The configuration is **deployment-specific** — the customer's topic list lives in their environment's `appsettings.{Environment}.json`. Tracer ships an example schema; customers populate it for their simulation.

### 3.6 DdsTraceContextExtractor

Architecture §7 mandates three uint64 fields on every event IDL: `trace_id`, `event_id`, `parent_event_id`. The extractor reads these fields by name from the sample.

```csharp
namespace Tracer.Adapters.DDS;

public sealed class DdsTraceContextExtractor
{
    private readonly ConcurrentDictionary<Type, TraceContextAccessors> _accessorCache = new();

    public TraceContext Extract(IDdsSample sample, DdsTopicMetadata meta)
    {
        if (meta.Kind != DdsTopicKind.Event)
            return TraceContext.Empty;        // only events carry trace context
        
        var accessors = _accessorCache.GetOrAdd(meta.SampleType, BuildAccessors);
        var payload = sample.GetPayload();
        
        return new TraceContext
        {
            TraceId       = accessors.TraceIdAccessor(payload),
            EventId       = new EventId(accessors.EventIdAccessor(payload)),
            ParentEventId = new EventId(accessors.ParentEventIdAccessor(payload)),
        };
    }

    private static TraceContextAccessors BuildAccessors(Type sampleType)
    {
        // Use reflection (cached) to build typed delegate accessors for the three fields
        var traceIdProp       = sampleType.GetProperty("traceId") ?? sampleType.GetProperty("TraceId")
            ?? throw new InvalidOperationException($"Sample type {sampleType.Name} missing traceId/TraceId property");
        var eventIdProp       = sampleType.GetProperty("eventId") ?? sampleType.GetProperty("EventId")
            ?? throw new InvalidOperationException($"Sample type {sampleType.Name} missing eventId/EventId property");
        var parentEventIdProp = sampleType.GetProperty("parentEventId") ?? sampleType.GetProperty("ParentEventId")
            ?? throw new InvalidOperationException($"Sample type {sampleType.Name} missing parentEventId/ParentEventId property");
        
        return new TraceContextAccessors
        {
            TraceIdAccessor       = BuildUlongAccessor(traceIdProp),
            EventIdAccessor       = BuildUlongAccessor(eventIdProp),
            ParentEventIdAccessor = BuildUlongAccessor(parentEventIdProp),
        };
    }

    private static Func<object, ulong> BuildUlongAccessor(PropertyInfo prop)
    {
        // Compiled accessor for speed; reflection is slow per-call but cheap per-build
        var param = Expression.Parameter(typeof(object), "obj");
        var cast = Expression.Convert(param, prop.DeclaringType!);
        var access = Expression.Property(cast, prop);
        var convert = Expression.Convert(access, typeof(ulong));
        return Expression.Lambda<Func<object, ulong>>(convert, param).Compile();
    }

    private sealed class TraceContextAccessors
    {
        public required Func<object, ulong> TraceIdAccessor { get; init; }
        public required Func<object, ulong> EventIdAccessor { get; init; }
        public required Func<object, ulong> ParentEventIdAccessor { get; init; }
    }
}

public sealed record TraceContext
{
    public required ulong TraceId { get; init; }
    public required EventId EventId { get; init; }
    public required EventId ParentEventId { get; init; }
    
    public static TraceContext Empty => new()
    {
        TraceId = 0,
        EventId = new EventId(0),
        ParentEventId = new EventId(0)
    };
}
```

**Compiled accessors via `System.Linq.Expressions`**: reflective property access per-sample would be a hot-path performance hit at 1000+ events/sec. Compiling once per sample type gives near-direct-access speed.

### 3.7 DdsSubscriberFactory

The factory wraps the customer's Cyclone DDS binding to produce a subscriber per topic. The exact API depends on the binding; the pattern:

Please see CycloneDDS.NET.README.md (and lookup the package sources on gihub) for what the csharp bindings API actually look like.

```csharp
namespace Tracer.Adapters.DDS;

public sealed class DdsSubscriberFactory
{
    private readonly ICycloneDdsParticipant _participant;
    private readonly ILogger<DdsSubscriberFactory> _logger;

    public DdsSubscriberFactory(ICycloneDdsParticipant participant, ILogger<DdsSubscriberFactory> logger)
    {
        _participant = participant;
        _logger = logger;
    }

    public async Task<IDisposable> CreateAsync(
        DdsTopicSubscription topicSub,
        Action<IDdsSample> onSample,
        CancellationToken ct)
    {
        // Cyclone DDS binding-specific API. The shape (probably):
        // 1. Get or create a Topic for topicSub.TopicName with type topicSub.SampleType
        // 2. Create a DataReader on the Topic
        // 3. Register a listener that calls onSample for each new sample
        // 4. Return an IDisposable that unregisters the listener and disposes the reader
        
        var topic = await _participant.GetTopicAsync(topicSub.TopicName, topicSub.SampleType, ct);
        var reader = await _participant.CreateReaderAsync(topic, ct);
        reader.OnSampleReceived += (s, e) => onSample(e.Sample);
        
        return new SubscriberHandle(reader);
    }

    private sealed class SubscriberHandle : IDisposable
    {
        private readonly object _reader;
        public SubscriberHandle(object reader) { _reader = reader; }
        public void Dispose() { /* Cyclone DDS-specific cleanup */ }
    }
}

// The IDdsSample interface is Tracer's abstraction over the customer's binding-specific sample type.
// It exposes the fields Tracer needs without forcing the binding to be referenced from Tracer.Core.
public interface IDdsSample
{
    DateTimeOffset SourceTimestamp { get; }     // dds_write_ts() value
    ulong SequenceNumber { get; }
    object GetPayload();                         // the typed sample payload object
}
```

The `IDdsSample` abstraction matters: Tracer.Core never references Cyclone DDS types directly. `Tracer.Adapters.DDS` wraps the binding's sample type behind `IDdsSample` so the rest of the adapter operates against Tracer's own interfaces. If the customer changes DDS implementation (e.g., from Cyclone to OpenDDS), only the wrapper changes.

### 3.8 Configuration

```csharp
namespace Tracer.Adapters.DDS.Configuration;

public sealed class DdsAdapterConfig
{
    public required string PublisherNodeId { get; init; }      // this node's identity
    public required IReadOnlyList<DdsTopicSubscription> Topics { get; init; }
    public int IngestBufferSize { get; init; } = 50_000;        // channel size
    public required CycloneDdsParticipantConfig Participant { get; init; }
}

public sealed class DdsTopicSubscription
{
    public required string TopicName { get; init; }
    public required string SampleTypeName { get; init; }       // resolved at startup
}

public sealed class CycloneDdsParticipantConfig
{
    public required int DomainId { get; init; }
    public string? QosProfile { get; init; }
}
```

---

## 4. The Shared Memory Transport

### 4.1 Architectural Role

`SharedMemoryTransport` is the production implementation of `IAgentTransport` (Phase 1 §6). It carries `DiagnosticRecord` instances from the **simulation process** (where the DDS adapter runs) to the **TracerAgent process** (a separate Windows service).

The mock `InProcessChannelTransport` (Phase 1) is a `Channel<DiagnosticRecord>` in the same address space. The real transport must cross process boundaries.

Requirements:

| Requirement | Implication |
|---|---|
| Cross-process, single-machine | Shared memory or named pipes |
| Low latency (< 1ms enqueue cost) | Lock-free or fine-grained locking |
| 1000+ events/sec sustained, 5000+ burst | Throughput must not bottleneck |
| Survive transient agent restart | Producer-side cannot block forever |
| Bounded memory | Fixed-size ring buffer |
| Producer never blocks | Drop-oldest under back-pressure |
| Consumer can reconnect | Producer continues writing; consumer resumes |

**Choice: shared memory + ring buffer + semaphore**

- **Shared memory** (`MemoryMappedFile` with `CreateOrOpen`) gives raw cross-process byte access. No serialization overhead beyond marshalling into the buffer.
- **Ring buffer** is the canonical data structure for SPMC-ish IPC with bounded memory.
- **Semaphore** for notifying the consumer that new data is available. Named semaphores cross process boundaries.

The alternative (named pipes) was rejected because each enqueue requires a system call; shared memory enqueue is a memory write plus a semaphore signal.

### 4.2 Layout

```
┌─────────────────────────────────────────────────────────────┐
│ Header (fixed size, 4096 bytes for alignment)              │
│  - magic = "TRCRSHM\0"                                      │
│  - version = 1                                              │
│  - capacity (bytes)                                          │
│  - write_offset (atomic, producer-managed)                  │
│  - read_offset (atomic, consumer-managed)                   │
│  - producer_pid                                              │
│  - consumer_pid                                              │
│  - producer_heartbeat_ticks                                 │
│  - consumer_heartbeat_ticks                                 │
│  - dropped_count (atomic, producer-incremented)             │
├─────────────────────────────────────────────────────────────┤
│ Ring buffer (capacity bytes; default 64 MB)                 │
│                                                              │
│  Each record:                                                │
│   - length (4 bytes)                                         │
│   - record bytes (length bytes)                              │
│                                                              │
│  Wraparound: a record never crosses the buffer end; if it    │
│  would, the producer writes a padding marker and starts the  │
│  next record at offset 0.                                    │
└─────────────────────────────────────────────────────────────┘

Plus: a named semaphore "TracerSyncSem" signaled by producer on write,
waited on by consumer.
```

The buffer is single-producer (the simulation's DDS adapter), single-consumer (the TracerAgent). SPSC simplifies the synchronization to atomic-pointer-pair semantics with no need for compare-and-swap.

### 4.3 SharedMemoryRingBuffer

```csharp
namespace Tracer.Adapters.SharedMemory;

/// <summary>
/// Single-producer, single-consumer ring buffer backed by shared memory.
/// </summary>
public sealed class SharedMemoryRingBuffer : IDisposable
{
    private const string MagicString = "TRCRSHM\0";
    private const int HeaderSize = 4096;
    
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly long _capacity;
    private readonly bool _isProducer;

    private SharedMemoryRingBuffer(MemoryMappedFile mmf, MemoryMappedViewAccessor accessor, long capacity, bool isProducer)
    {
        _mmf = mmf;
        _accessor = accessor;
        _capacity = capacity;
        _isProducer = isProducer;
    }

    public static SharedMemoryRingBuffer Create(string name, long capacity)
    {
        var totalSize = HeaderSize + capacity;
        var mmf = MemoryMappedFile.CreateOrOpen(name, totalSize);
        var accessor = mmf.CreateViewAccessor(0, totalSize);
        
        // Initialize header
        accessor.WriteArray(0, Encoding.ASCII.GetBytes(MagicString), 0, 8);
        accessor.Write(8, 1);                              // version
        accessor.Write(12, capacity);
        accessor.Write(20, 0L);                            // write_offset
        accessor.Write(28, 0L);                            // read_offset
        accessor.Write(36, Environment.ProcessId);
        accessor.Write(40, 0);                             // consumer_pid (unset)
        accessor.Write(44, 0L);                            // producer_heartbeat
        accessor.Write(52, 0L);                            // consumer_heartbeat
        accessor.Write(60, 0L);                            // dropped_count
        
        return new SharedMemoryRingBuffer(mmf, accessor, capacity, isProducer: true);
    }

    public static SharedMemoryRingBuffer Open(string name)
    {
        var mmf = MemoryMappedFile.OpenExisting(name);
        var accessor = mmf.CreateViewAccessor();
        // Verify magic
        var magicBytes = new byte[8];
        accessor.ReadArray(0, magicBytes, 0, 8);
        if (Encoding.ASCII.GetString(magicBytes) != MagicString)
            throw new InvalidOperationException("Shared memory has bad magic");
        var capacity = accessor.ReadInt64(12);
        accessor.Write(40, Environment.ProcessId);   // claim consumer PID
        return new SharedMemoryRingBuffer(mmf, accessor, capacity, isProducer: false);
    }
    
    /// <summary>
    /// Producer side: writes record bytes to the buffer, dropping oldest data on full.
    /// Returns true if written; false if the record is too large for the buffer at all.
    /// </summary>
    public bool TryWrite(ReadOnlySpan<byte> record)
    {
        if (!_isProducer) throw new InvalidOperationException("Cannot write on consumer side");
        if (record.Length + 4 > _capacity) return false;    // record larger than entire buffer
        
        var writeOff = ReadAtomicLong(20);
        var readOff = ReadAtomicLong(28);
        
        // Wraparound check: if writing this record would cross the capacity boundary,
        // pad to the end and wrap.
        if (writeOff + 4 + record.Length > _capacity)
        {
            // Pad the remainder with a length-0 marker so the consumer knows to wrap
            WriteLengthMarker(writeOff, 0);
            writeOff = 0;
        }
        
        // Drop-oldest: if we'd write past the read pointer, advance read past N records
        // until there's room. Logically: free space = capacity - (writeOff - readOff) mod capacity.
        var requiredBytes = record.Length + 4;
        while (requiredBytes > AvailableSpace(writeOff, readOff))
        {
            readOff = AdvancePastNextRecord(readOff);
            IncrementDropped();
        }
        
        // Write the record
        WriteLengthMarker(writeOff, record.Length);
        WriteBytes(HeaderSize + writeOff + 4, record);
        
        var newWriteOff = writeOff + 4 + record.Length;
        if (newWriteOff >= _capacity) newWriteOff = 0;
        WriteAtomicLong(20, newWriteOff);
        WriteAtomicLong(28, readOff);   // commit any drop-oldest read pointer advancement
        
        return true;
    }
    
    /// <summary>
    /// Consumer side: try to read the next record. Returns null if buffer empty.
    /// </summary>
    public byte[]? TryRead()
    {
        if (_isProducer) throw new InvalidOperationException("Cannot read on producer side");
        var writeOff = ReadAtomicLong(20);
        var readOff = ReadAtomicLong(28);
        if (writeOff == readOff) return null;
        
        var length = ReadLengthMarker(readOff);
        if (length == 0)
        {
            // Padding marker → wrap
            readOff = 0;
            WriteAtomicLong(28, readOff);
            return TryRead();
        }
        
        var result = new byte[length];
        ReadBytes(HeaderSize + readOff + 4, result);
        var newReadOff = readOff + 4 + length;
        if (newReadOff >= _capacity) newReadOff = 0;
        WriteAtomicLong(28, newReadOff);
        return result;
    }

    private long ReadAtomicLong(long offset)
    {
        // Volatile read for cross-process visibility
        return Volatile.Read(ref Unsafe.AsRef<long>(GetPointer(offset)));
    }
    private void WriteAtomicLong(long offset, long value)
    {
        Volatile.Write(ref Unsafe.AsRef<long>(GetPointer(offset)), value);
    }
    // GetPointer, WriteLengthMarker, ReadLengthMarker, WriteBytes, ReadBytes, AvailableSpace,
    // AdvancePastNextRecord, IncrementDropped — details elided; standard unsafe-pointer arithmetic.
    
    public void Dispose() { _accessor.Dispose(); _mmf.Dispose(); }
}
```

**Atomic memory operations**: `Volatile.Read` and `Volatile.Write` provide cross-process memory visibility for the offset fields. On x86/x64 Windows this maps to ordered loads/stores. The producer and consumer don't need locks for the offset coordination — they're a single producer, single consumer setup.

**Wraparound discipline**: the producer never writes a record that would straddle the end of the buffer. Instead, it writes a length-0 marker meaning "padding, wrap to zero" and starts the next record at offset 0. The consumer reads the marker, knows to wrap.

### 4.4 SharedMemoryDiagnosticRecordCodec

Records cross the boundary as bytes. We need a fast, schema-stable codec.

**Choice: System.Text.Json with source-generated serializers**

Reasons:
- Source-gen avoids reflection overhead per-call
- Already used elsewhere in Tracer; schema knowledge is in one place
- The JSON-as-text overhead is acceptable at ~1KB per record × 1000/sec = 1 MB/sec — well under the buffer's throughput
- An alternative like MessagePack would be more compact but introduces a new dependency; not justified

```csharp
namespace Tracer.Adapters.SharedMemory;

[JsonSerializable(typeof(EventRecord))]
[JsonSerializable(typeof(StateSampleRecord))]
[JsonSerializable(typeof(SerializedRecord))]    // wrapper that carries kind
public partial class DiagnosticRecordSerializerContext : JsonSerializerContext { }

public sealed class SharedMemoryDiagnosticRecordCodec
{
    public byte[] Encode(DiagnosticRecord record)
    {
        var wrapper = record switch
        {
            EventRecord e        => new SerializedRecord { Kind = "Event",       EventRecord = e },
            StateSampleRecord s  => new SerializedRecord { Kind = "StateSample", StateSampleRecord = s },
            _ => throw new InvalidOperationException($"Unknown record type {record.GetType()}")
        };
        return JsonSerializer.SerializeToUtf8Bytes(wrapper, DiagnosticRecordSerializerContext.Default.SerializedRecord);
    }

    public DiagnosticRecord Decode(ReadOnlySpan<byte> bytes)
    {
        var wrapper = JsonSerializer.Deserialize(bytes, DiagnosticRecordSerializerContext.Default.SerializedRecord)
            ?? throw new InvalidOperationException("Failed to deserialize");
        return wrapper.Kind switch
        {
            "Event"        => wrapper.EventRecord ?? throw new InvalidOperationException("Missing EventRecord"),
            "StateSample"  => wrapper.StateSampleRecord ?? throw new InvalidOperationException("Missing StateSampleRecord"),
            _ => throw new InvalidOperationException($"Unknown kind {wrapper.Kind}")
        };
    }
}

public sealed record SerializedRecord
{
    public required string Kind { get; init; }
    public EventRecord? EventRecord { get; init; }
    public StateSampleRecord? StateSampleRecord { get; init; }
}
```

**If JSON proves too slow**: a follow-up swap to MessagePack is a contained refactor — only this file changes. Phase 11's instinct is to ship JSON and let real measurements drive the decision.

### 4.5 SharedMemoryTransport

```csharp
namespace Tracer.Adapters.SharedMemory;

/// <summary>
/// IAgentTransport implementation. On the producer side (simulation process), exposes
/// EnqueueAsync. On the consumer side (TracerAgent), exposes ConsumeAsync.
/// </summary>
public sealed class SharedMemoryTransport : IAgentTransport, IDisposable
{
    private readonly SharedMemoryRingBuffer _buffer;
    private readonly Semaphore _signal;
    private readonly SharedMemoryDiagnosticRecordCodec _codec;
    private readonly ILogger<SharedMemoryTransport> _logger;
    private readonly bool _isProducer;

    private SharedMemoryTransport(SharedMemoryRingBuffer buffer, Semaphore signal,
        SharedMemoryDiagnosticRecordCodec codec, bool isProducer, ILogger<SharedMemoryTransport> logger)
    {
        _buffer = buffer;
        _signal = signal;
        _codec = codec;
        _isProducer = isProducer;
        _logger = logger;
    }

    public static SharedMemoryTransport CreateProducer(SharedMemoryConfig config, SharedMemoryDiagnosticRecordCodec codec, ILogger<SharedMemoryTransport> logger)
    {
        var buffer = SharedMemoryRingBuffer.Create(config.SharedMemoryName, config.CapacityBytes);
        var signal = new Semaphore(0, int.MaxValue, config.SemaphoreName);
        return new SharedMemoryTransport(buffer, signal, codec, isProducer: true, logger);
    }

    public static SharedMemoryTransport CreateConsumer(SharedMemoryConfig config, SharedMemoryDiagnosticRecordCodec codec, ILogger<SharedMemoryTransport> logger)
    {
        // Open with retries — agent may start before simulation
        SharedMemoryRingBuffer? buffer = null;
        for (int attempt = 0; attempt < 60; attempt++)
        {
            try { buffer = SharedMemoryRingBuffer.Open(config.SharedMemoryName); break; }
            catch (FileNotFoundException) { Thread.Sleep(1000); }
        }
        if (buffer is null) throw new InvalidOperationException("Producer never came up");
        
        var signal = new Semaphore(0, int.MaxValue, config.SemaphoreName);
        return new SharedMemoryTransport(buffer, signal, codec, isProducer: false, logger);
    }

    /// <summary>Producer side: enqueue a record.</summary>
    public ValueTask EnqueueAsync(DiagnosticRecord record, CancellationToken ct)
    {
        if (!_isProducer) throw new InvalidOperationException("Producer method on consumer");
        var bytes = _codec.Encode(record);
        var written = _buffer.TryWrite(bytes);
        if (!written)
        {
            _logger.LogWarning("Record too large for buffer ({Length} bytes)", bytes.Length);
        }
        else
        {
            // Signal consumer
            _signal.Release();
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>Consumer side: yields records until cancellation.</summary>
    public async IAsyncEnumerable<DiagnosticRecord> ConsumeAsync([EnumeratorCancellation] CancellationToken ct)
    {
        if (_isProducer) throw new InvalidOperationException("Consumer method on producer");
        
        while (!ct.IsCancellationRequested)
        {
            // Wait for a signal (up to 100ms then re-check buffer in case we missed a signal)
            await Task.Run(() => _signal.WaitOne(100), ct).ConfigureAwait(false);
            
            // Drain
            while (true)
            {
                var bytes = _buffer.TryRead();
                if (bytes is null) break;
                DiagnosticRecord record;
                try { record = _codec.Decode(bytes); }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to decode record");
                    continue;
                }
                yield return record;
            }
        }
    }

    public void Dispose()
    {
        _buffer.Dispose();
        _signal.Dispose();
    }
}
```

**Why `WaitOne(100)` instead of `WaitOne()`**: a small spurious-wakeup safety. If a signal is missed (which shouldn't happen but is defensive), we re-check every 100 ms. The trade-off: 100ms maximum latency on idle-then-resume. For an active producer this is invisible.

**Why not block in WaitOne forever**: the consumer needs to honor cancellation. The 100ms wait gives a regular cancellation check point.

### 4.6 Producer-Side Drop Telemetry

The drop-oldest behavior must be visible to operators. The shared memory header has a `dropped_count` field; both producer and consumer can read it.

The TracerAgent (consumer) reads `dropped_count` periodically and logs structured events:

```csharp
// In TracerAgent's monitoring loop:
private async Task MonitorTransportAsync(CancellationToken ct)
{
    var lastDropped = 0L;
    while (!ct.IsCancellationRequested)
    {
        var currentDropped = _transport.GetDroppedCount();
        if (currentDropped > lastDropped)
        {
            _logger.LogWarning(
                "SharedMemoryTransport dropped {NewDrops} records (total {Total}) — consumer is falling behind",
                currentDropped - lastDropped, currentDropped);
            // Phase 9 latency budget surfaces this; Phase 8 annotations can flag the affected interval
        }
        lastDropped = currentDropped;
        await Task.Delay(5000, ct);
    }
}
```

Visibility of drops is essential — a silent transport that loses data is worse than a noisy one.

### 4.7 Bounded Memory Behavior

Default buffer capacity: 64 MB. At ~1 KB per record, that's ~65,000 records — enough for a 1-second burst at 5000/sec plus headroom.

If the consumer (agent) is down for an extended period, the ring fills and drop-oldest kicks in. The simulation never blocks; data history is sacrificed gracefully.

Configurable per-deployment via `SharedMemoryConfig.CapacityBytes`. Larger means more tolerance for consumer-side delays; smaller means tighter memory footprint.

---

## 5. The Sync Adapter

### 5.1 Architectural Role

`SyncSystemUploadService` is the production implementation of `ITelemetryUploadService` (Phase 1 §6). When a TracerAgent completes an interval (Phase 2 §6.7 — interval rotation), it calls `UploadAsync` with the interval directory; the upload service makes the data available to the aggregator at session-end time.

The sync system addendum (from Phase 0, `sync_addendum_telemetry.md`) defined the contract:

- **Telemetry category**: a sync-system category for Tracer's per-interval data
- **REST endpoints on the sync master**: register an upload intent, report completion
- **NAS upload**: the sync system handles the actual file transfer to NAS via its own agent topology
- **Per-node directory layout** on NAS: `<NasRoot>/<sessionId>/<nodeId>/<intervalId>/...`

`SyncSystemUploadService` is therefore thin — it's an HTTP client that registers intents with the sync master. The actual file movement is the sync system's responsibility.

### 5.2 ITelemetryUploadService Interface

Recap from Phase 1 §6:

```csharp
namespace Tracer.Core.Adapters;

public interface ITelemetryUploadService
{
    /// <summary>
    /// Submit a completed interval directory for upload. Returns when the intent
    /// is registered with the upload system; actual upload happens asynchronously.
    /// The returned UploadIntentId can be queried for status.
    /// </summary>
    Task<UploadIntentId> SubmitAsync(IntervalUploadRequest request, CancellationToken ct);
    
    Task<UploadStatus> GetStatusAsync(UploadIntentId intentId, CancellationToken ct);
    
    /// <summary>
    /// Block (with cancellation) until the upload completes or fails terminally.
    /// </summary>
    Task<UploadResult> WaitForCompletionAsync(UploadIntentId intentId, CancellationToken ct);
}

public sealed record IntervalUploadRequest
{
    public required string SessionId { get; init; }
    public required string NodeId { get; init; }
    public required string IntervalId { get; init; }       // e.g., "20260521T140000Z"
    public required string LocalIntervalPath { get; init; } // directory containing events.duckdb + slow_state.duckdb + fast_state/
}

public sealed record UploadIntentId(string Value);

public enum UploadStatus { Pending, Uploading, Completed, Failed }

public sealed record UploadResult
{
    public required UploadStatus Status { get; init; }
    public string? ErrorMessage { get; init; }
    public string? RemotePath { get; init; }
}
```

### 5.3 SyncSystemUploadService

```csharp
namespace Tracer.Adapters.Sync;

public sealed class SyncSystemUploadService : ITelemetryUploadService
{
    private readonly SyncMasterRestClient _restClient;
    private readonly SyncAdapterConfig _config;
    private readonly ILogger<SyncSystemUploadService> _logger;

    public SyncSystemUploadService(
        SyncMasterRestClient restClient,
        SyncAdapterConfig config,
        ILogger<SyncSystemUploadService> logger)
    {
        _restClient = restClient;
        _config = config;
        _logger = logger;
    }

    public async Task<UploadIntentId> SubmitAsync(IntervalUploadRequest request, CancellationToken ct)
    {
        var intent = await _restClient.RegisterUploadIntentAsync(new SyncUploadIntentRequest
        {
            Category = "Telemetry",
            SessionId = request.SessionId,
            NodeId = request.NodeId,
            IntervalId = request.IntervalId,
            LocalPath = request.LocalIntervalPath,
            DestinationPath = $"telemetry/{request.SessionId}/{request.NodeId}/{request.IntervalId}",
        }, ct);
        
        _logger.LogInformation(
            "Registered upload intent {IntentId} for {SessionId}/{NodeId}/{IntervalId}",
            intent.IntentId, request.SessionId, request.NodeId, request.IntervalId);
        
        return new UploadIntentId(intent.IntentId);
    }

    public async Task<UploadStatus> GetStatusAsync(UploadIntentId intentId, CancellationToken ct)
    {
        var status = await _restClient.GetIntentStatusAsync(intentId.Value, ct);
        return MapStatus(status);
    }

    public async Task<UploadResult> WaitForCompletionAsync(UploadIntentId intentId, CancellationToken ct)
    {
        // Poll with exponential backoff up to a reasonable maximum
        var delay = TimeSpan.FromSeconds(2);
        var maxDelay = TimeSpan.FromSeconds(60);
        
        while (!ct.IsCancellationRequested)
        {
            var status = await _restClient.GetIntentStatusAsync(intentId.Value, ct);
            switch (status.State)
            {
                case "Completed":
                    return new UploadResult
                    {
                        Status = UploadStatus.Completed,
                        RemotePath = status.RemotePath
                    };
                case "Failed":
                    return new UploadResult
                    {
                        Status = UploadStatus.Failed,
                        ErrorMessage = status.ErrorMessage
                    };
                default:
                    await Task.Delay(delay, ct);
                    delay = TimeSpan.FromTicks(Math.Min(maxDelay.Ticks, delay.Ticks * 2));
                    break;
            }
        }
        ct.ThrowIfCancellationRequested();
        throw new OperationCanceledException();
    }

    private static UploadStatus MapStatus(SyncUploadIntentStatus status) => status.State switch
    {
        "Pending"   => UploadStatus.Pending,
        "Uploading" => UploadStatus.Uploading,
        "Completed" => UploadStatus.Completed,
        "Failed"    => UploadStatus.Failed,
        _ => UploadStatus.Pending
    };
}
```

### 5.4 SyncMasterRestClient

A thin HTTP client wrapping the sync system's REST API.

```csharp
namespace Tracer.Adapters.Sync;

public sealed class SyncMasterRestClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SyncMasterRestClient> _logger;

    public SyncMasterRestClient(HttpClient httpClient, ILogger<SyncMasterRestClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<SyncUploadIntentResponse> RegisterUploadIntentAsync(
        SyncUploadIntentRequest request, CancellationToken ct)
    {
        // POST /api/sync/intents — per the sync addendum contract
        using var response = await _httpClient.PostAsJsonAsync("/api/sync/intents", request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SyncUploadIntentResponse>(cancellationToken: ct))!;
    }

    public async Task<SyncUploadIntentStatus> GetIntentStatusAsync(string intentId, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync($"/api/sync/intents/{intentId}", ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SyncUploadIntentStatus>(cancellationToken: ct))!;
    }
}

public sealed record SyncUploadIntentRequest
{
    public required string Category { get; init; }
    public required string SessionId { get; init; }
    public required string NodeId { get; init; }
    public required string IntervalId { get; init; }
    public required string LocalPath { get; init; }
    public required string DestinationPath { get; init; }
}

public sealed record SyncUploadIntentResponse
{
    public required string IntentId { get; init; }
}

public sealed record SyncUploadIntentStatus
{
    public required string State { get; init; }
    public string? RemotePath { get; init; }
    public string? ErrorMessage { get; init; }
}
```

### 5.5 Configuration

```csharp
namespace Tracer.Adapters.Sync.Configuration;

public sealed class SyncAdapterConfig
{
    public required string SyncMasterBaseUrl { get; init; }      // e.g., "https://sync-master.internal/"
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public int RetryAttempts { get; init; } = 3;
}
```

### 5.6 Failure Handling

Two failure modes to consider:

**Sync master unreachable**: REST calls fail. The agent should:
1. Retry with exponential backoff
2. After N failures, log a warning but **do not block the simulation**
3. Keep the local interval directory intact — it will be uploaded next time the sync master is reachable
4. Surface the issue via Tracer's operator-message-queue pattern (Phase 8)

**Upload eventually fails**: the sync system signals Failed. The agent:
1. Logs the failure structurally
2. Optionally retries via a new intent (configurable)
3. Surfaces to the operator message queue

The agent's interval directory is the durable record. Even if upload never succeeds, the data is still on the agent's disk and the operator can manually retrieve it.

---

## 6. The NAS Adapter

### 6.1 Architectural Role

`NasStorageReader` is the production implementation of `ITelemetryStorageReader` (Phase 1 §6) used by the **Aggregator** (Phase 4) to read per-node interval data from NAS during bundle building.

The mock implementation (`LocalFileSystemStorageReader`) reads from a local directory. The NAS version reads from a UNC path the sync system populated.

### 6.2 ITelemetryStorageReader Interface

```csharp
namespace Tracer.Core.Adapters;

public interface ITelemetryStorageReader
{
    Task<IReadOnlyList<NodeIntervalDescriptor>> ListIntervalsAsync(
        string sessionId, CancellationToken ct);
    
    /// <summary>
    /// Provides a readable copy of the interval's local file structure. Implementations
    /// may stream from the source or stage to a local temp directory. The returned
    /// disposable cleans up the staging when disposed.
    /// </summary>
    Task<StagedInterval> StageAsync(NodeIntervalDescriptor descriptor, CancellationToken ct);
}

public sealed record NodeIntervalDescriptor
{
    public required string SessionId { get; init; }
    public required string NodeId { get; init; }
    public required string IntervalId { get; init; }
    public required string SourcePath { get; init; }   // UNC or local path
    public required long EstimatedBytes { get; init; }
}

public sealed class StagedInterval : IDisposable
{
    public required string LocalPath { get; init; }
    private readonly Action? _cleanup;
    public StagedInterval(Action? cleanup) { _cleanup = cleanup; }
    public void Dispose() { _cleanup?.Invoke(); }
}
```

### 6.3 NasStorageReader

```csharp
namespace Tracer.Adapters.Nas;

public sealed class NasStorageReader : ITelemetryStorageReader
{
    private readonly NasAdapterConfig _config;
    private readonly ILogger<NasStorageReader> _logger;

    public NasStorageReader(NasAdapterConfig config, ILogger<NasStorageReader> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<IReadOnlyList<NodeIntervalDescriptor>> ListIntervalsAsync(
        string sessionId, CancellationToken ct)
    {
        var sessionDir = Path.Combine(_config.NasRoot, "telemetry", sessionId);
        if (!Directory.Exists(sessionDir))
        {
            _logger.LogWarning("No NAS directory for session {SessionId} at {Path}", sessionId, sessionDir);
            return Array.Empty<NodeIntervalDescriptor>();
        }
        
        var descriptors = new List<NodeIntervalDescriptor>();
        foreach (var nodeDir in Directory.EnumerateDirectories(sessionDir))
        {
            var nodeId = Path.GetFileName(nodeDir);
            foreach (var intervalDir in Directory.EnumerateDirectories(nodeDir))
            {
                var intervalId = Path.GetFileName(intervalDir);
                // Verify the interval is complete (has the expected files)
                if (!IsIntervalComplete(intervalDir))
                {
                    _logger.LogWarning(
                        "Skipping incomplete interval {NodeId}/{IntervalId}", nodeId, intervalId);
                    continue;
                }
                
                descriptors.Add(new NodeIntervalDescriptor
                {
                    SessionId = sessionId,
                    NodeId = nodeId,
                    IntervalId = intervalId,
                    SourcePath = intervalDir,
                    EstimatedBytes = ComputeDirSize(intervalDir)
                });
            }
        }
        return descriptors;
    }

    public Task<StagedInterval> StageAsync(NodeIntervalDescriptor descriptor, CancellationToken ct)
    {
        // For SMB-mounted NAS the UNC path is directly readable by DuckDB. No staging needed
        // unless we want to copy locally for performance.
        if (_config.PreferLocalStaging)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "tracer-staging", Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            CopyDirectoryRecursive(descriptor.SourcePath, tempDir, ct);
            return Task.FromResult(new StagedInterval { LocalPath = tempDir, cleanup = () => Directory.Delete(tempDir, true) });
        }
        else
        {
            return Task.FromResult(new StagedInterval { LocalPath = descriptor.SourcePath });
            // No staging, no cleanup
        }
    }

    private static bool IsIntervalComplete(string intervalDir)
    {
        // The agent writes a sentinel file when the interval rotates cleanly
        return File.Exists(Path.Combine(intervalDir, ".complete"));
    }

    private static long ComputeDirSize(string dir) =>
        new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
    
    private static void CopyDirectoryRecursive(string source, string dest, CancellationToken ct)
    {
        // Standard recursive copy; details elided
    }
}
```

### 6.4 The `.complete` Sentinel

The agent writes a `.complete` file to each interval directory after a clean rotation. The NAS reader uses its presence as the "this interval is fully uploaded and consistent" signal. Without it, a partial upload (sync system writing files into the directory) would look like a complete interval to the aggregator and produce corrupt bundles.

Update to Phase 2's interval rotation: the agent writes `.complete` as the last step before declaring the interval done. The sync system's upload preserves it (uploads the entire directory atomically into NAS). Aggregator skips intervals without the sentinel.

If the agent crashes mid-rotation, the sentinel doesn't get written; the partial interval gets cleaned up on next startup (Phase 2 §6.10 recovery).

### 6.5 Configuration

```csharp
namespace Tracer.Adapters.Nas.Configuration;

public sealed class NasAdapterConfig
{
    public required string NasRoot { get; init; }              // e.g., "\\\\nas-server\\tracer"
    public bool PreferLocalStaging { get; init; } = false;     // copy intervals locally before reading
}
```

The aggregator typically runs on a machine with fast NAS access; `PreferLocalStaging = false` is the default. For NAS over WAN (a rare configuration), local staging avoids per-query NAS round trips.

---

## 7. Adapter Selection: Configuration-Driven DI

The TracerAgent's DI container needs to choose between mock and real adapters based on configuration.

### 7.1 The Configuration Section

```json
{
  "adapters": {
    "dataSource": "dds",
    "transport": "shared-memory",
    "upload": "sync",
    "storageReader": "nas",
    "clock": "system"
  },
  "dds": { /* DdsAdapterConfig */ },
  "sharedMemory": { /* SharedMemoryConfig */ },
  "sync": { /* SyncAdapterConfig */ },
  "nas": { /* NasAdapterConfig */ }
}
```

Values for each adapter slot:

- `dataSource`: `"mock"` | `"dds"`
- `transport`: `"in-process"` | `"shared-memory"`
- `upload`: `"local-file-system"` | `"sync"`
- `storageReader`: `"local-file-system"` | `"nas"`
- `clock`: `"system"` | `"simulated"`

Test deployments use the mock values; production uses the real ones. Mixed configurations are supported (e.g., real DDS but mock upload) for development scenarios.

### 7.2 AdapterRegistry

```csharp
namespace Tracer.AdapterSelection;

public sealed class AdapterRegistry
{
    private readonly IConfiguration _config;

    public AdapterRegistry(IConfiguration config) { _config = config; }

    public void RegisterAdapters(IServiceCollection services)
    {
        RegisterDataSource(services);
        RegisterTransport(services);
        RegisterUpload(services);
        RegisterStorageReader(services);
        RegisterClock(services);
    }

    private void RegisterDataSource(IServiceCollection services)
    {
        var choice = _config["adapters:dataSource"] ?? "mock";
        switch (choice)
        {
            case "mock":
                services.AddSingleton<IDiagnosticDataSource, MockDataSource>();
                // ... mock config wiring ...
                break;
            case "dds":
                services.Configure<DdsAdapterConfig>(_config.GetSection("dds"));
                services.AddSingleton<DdsSubscriberFactory>();
                services.AddSingleton<DdsSampleTranslator>();
                services.AddSingleton<DdsTraceContextExtractor>();
                services.AddSingleton<DdsTopicRegistry>(sp =>
                {
                    var ddsConfig = sp.GetRequiredService<IOptions<DdsAdapterConfig>>().Value;
                    var topics = ddsConfig.Topics.Select(t => /* resolve SampleTypeName to Type */).ToList();
                    return new DdsTopicRegistry(topics);
                });
                services.AddSingleton<IDiagnosticDataSource, DdsDiagnosticDataSource>();
                break;
            default:
                throw new InvalidOperationException($"Unknown dataSource adapter: {choice}");
        }
    }

    // RegisterTransport, RegisterUpload, RegisterStorageReader, RegisterClock — same pattern
}
```

### 7.3 Service Startup

In `TracerAgentHostBuilder` and `AggregatorHostBuilder` (and Observer's), startup includes:

```csharp
var registry = new AdapterRegistry(builder.Configuration);
registry.RegisterAdapters(builder.Services);
```

The rest of the host's DI is unchanged. Code that depends on `IDiagnosticDataSource`, `IAgentTransport`, etc. gets whatever was registered — mock or real, transparently.

### 7.4 Defaults Per Deployment

The `appsettings.json` in each deployment's directory selects the adapter values:

| File | Use |
|---|---|
| `appsettings.json` | Defaults: all mocks. Safe for `dotnet run` from a fresh checkout. |
| `appsettings.Development.json` | Mocks; useful for IDE F5 debugging. |
| `appsettings.IntegrationReal.json` | All real adapters. Used by Phase 11's integration-real test suite and by customer deployments. |
| `appsettings.Production.json` | Real adapters with production-tuned parameters. |

The active environment is picked via `ASPNETCORE_ENVIRONMENT` (or `DOTNET_ENVIRONMENT`) — standard .NET conventions.

---

## 8. The Integration-Real Test Suite

A new test category — `Tracer.Tests.Integration.Real` — exercises the full system against real adapters. Run manually or on a dedicated integration-real CI lane, not on every PR (these tests require the customer's simulation harness or a stand-in).

### 8.1 Test Environment

The integration-real tests assume:

- The customer provides a **simulation harness** that can run in CI: a small Cyclone DDS publisher emitting representative traffic with valid trace context, scenario metadata events, and configurable timing
- A real (or fake) sync master is reachable
- A network share at a known UNC path is writable (or a local-fs equivalent for CI)

If the customer's simulation isn't available in a given environment, the corresponding tests are skipped — not failed.

### 8.2 Test Categories

**Trace context propagation** (`TraceContextPropagationTests.cs`)

- Single-process: simulation publishes 1000 events forming a known trace chain
- Adapter captures them
- Bundle built
- Assert: `trace_id`, `event_id`, `parent_event_id` round-trip exactly
- Assert: Phase 6 causal-tree endpoint returns the expected tree shape

**Throughput and back-pressure** (`SharedMemoryThroughputTests.cs`)

- Simulation generates 5000 events/sec for 60 seconds
- Agent consumes them via the SharedMemoryTransport
- Assert: < 0.1% drop rate
- Assert: agent's CPU stays below 50% (single core)
- Assert: agent's memory stays below the configured limit

**Drop behavior under stall** (`SharedMemoryLossTests.cs`)

- Producer runs steadily
- Consumer process is paused (e.g., SIGSTOP)
- Producer keeps writing
- After ring fills, drop-oldest activates
- Consumer resumes
- Assert: `dropped_count` matches the actual deficit
- Assert: producer never crashed or blocked

**Upload happy path** (`SyncUploadTests.cs`)

- Agent completes an interval
- Calls `SyncSystemUploadService.SubmitAsync`
- Polling shows progression Pending → Uploading → Completed
- Assert: NAS path exists and contains the expected files
- Assert: `.complete` sentinel present

**Upload retry** (`SyncUploadTests.cs`)

- Sync master returns 503 transiently
- Agent retries
- Eventually upload succeeds
- Assert: total elapsed within reasonable bound; final state = Completed

**Aggregator → NAS read** (`NasReaderTests.cs`)

- Place known interval directories on NAS
- Aggregator runs
- Assert: discovers intervals, builds bundle, bundle is valid

**End-to-end** (`EndToEndSessionTests.cs`)

- Start: real simulation harness, multiple agent processes (loopback per agent), real sync master, real NAS
- Run for 5 minutes simulated time
- Assert: every published event appears in at least one agent's interval files
- Build bundle
- Assert: bundle contains events from all participating agents
- Assert: cross-node receive times present in bundle (Phase 9's data shape)
- Assert: Replication Latency view query returns non-trivial p99 values
- Assert: Network Topology query returns the expected graph

**Multi-day soak** (`SoakTests.cs`)

- 48-hour continuous run
- Periodic checks: agent memory, observer memory, CPU, disk
- Assert: no monotonic resource growth
- Assert: agent restarts (simulated mid-run) recover correctly
- Assert: observer SSE clients remain healthy

### 8.3 Trace Context Discipline Verification

The most architecturally important verification: that trace context propagates correctly through the customer's simulation code.

```csharp
namespace Tracer.Tests.Integration.Real;

[TestClass]
public class TraceContextPropagationTests
{
    [TestMethod]
    public async Task ParentChildRelationshipsPreserved()
    {
        // Arrange: simulation harness configured to emit a known trace structure:
        //   Root event A (trace_id=1, event_id=100, parent=0)
        //     → child event B (trace_id=1, event_id=101, parent=100)
        //       → grandchild event C (trace_id=1, event_id=102, parent=101)
        var harness = await StartHarnessAsync();
        await harness.EmitKnownTraceAsync(traceId: 1, depth: 3);
        
        // Act: capture, rotate, build bundle, query
        var agent = await StartAgentAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));
        await agent.RotateAsync();
        var bundlePath = await BuildBundleAsync(agent.SessionId);
        var client = await OpenBundleClientAsync(bundlePath);
        
        // Assert: causal tree endpoint returns the chain
        var tree = await client.GetAsync<TraceTreeDto>($"/api/traces/1/tree");
        Assert.AreEqual(3, tree.Nodes.Count);
        Assert.AreEqual(2, tree.Edges.Count);   // A→B, B→C
        
        // Specifically: verify event IDs are exactly what the simulation set
        var rootNode = tree.Nodes.First(n => n.ParentEventId is null || n.ParentEventId == "0000000000000000");
        Assert.AreEqual("0000000000000064", rootNode.EventId);  // 100 in hex
    }
}
```

This test is **the single most important integration-real test**. If trace context doesn't propagate, Phase 6's causal tree view is broken in production regardless of how well it works on mock data.

### 8.4 Continuous Integration Lane

The integration-real suite runs on a separate CI lane:

- Triggered manually or on a nightly schedule, not on every PR
- Requires access to the simulation harness (provisioned per environment)
- Failure does not block PR merges to main (preserves PR cycle velocity)
- Failure does block release tags (releases require integration-real success)

This split prevents flaky external infrastructure from blocking development while ensuring releases are validated end-to-end.

---

## 9. Hardening Items

Phase 11 surfaces real-world concerns the mock-based development couldn't expose. Each gets dedicated attention.

### 9.1 Resource Limits

| Resource | Limit | Enforcement |
|---|---|---|
| Agent process RSS | 2 GB default | OS-level via Windows Job Object; agent crashes if exceeded (durability preserved by interval-on-disk model) |
| Observer process RSS | 4 GB default | Same |
| Agent ingest channel | 50,000 records | Bounded `Channel<DiagnosticRecord>` (already in place) |
| SharedMemoryTransport buffer | 64 MB default | Ring buffer drop-oldest |
| DuckDB query memory | 1 GB per query | `PRAGMA memory_limit` per query (Phase 10 §3.3) |
| Per-interval disk | unlimited but monitored | Operational alerting (out of Tracer scope) |
| Open file handles per agent | < 200 | Verified via test; intervals close on rotation |

### 9.2 Graceful Degradation

When the system is overloaded, behavior should degrade predictably:

**Producer-side overload** (DDS subscriber can't keep up):
- DDS adapter's bounded channel drops oldest
- Drop count logged
- Eventually surfaces in the operator message queue
- Simulation continues unaffected

**Transport-side overload** (SharedMemoryTransport saturated):
- Drop-oldest in the ring buffer
- `dropped_count` increments
- Agent's monitor logs the deficit
- Simulation continues unaffected

**Disk-side overload** (writer can't keep up):
- DuckDB Appender's internal queue grows
- Eventually OOM if not stemmed — but the agent's bounded channels prevent this in practice
- If disk truly fills: writes fail, agent surfaces critical error, simulation continues (intervals lost but recoverable via reduced sample rate)

**NAS upload backlog**:
- Multiple completed intervals queue up on the agent
- Agent's disk grows
- Each upload retry attempt logged
- Eventually if NAS comes back, all backlog uploads
- Operator alerted if backlog exceeds N intervals or T hours

### 9.3 Error Recovery

The agent and observer are designed to recover from transient failures without losing data:

- **Agent crash during interval**: interval directory has uncommitted DuckDB writes. On restart, agent verifies the interval's `.complete` sentinel; if missing, the interval is discarded as incomplete (the SharedMemoryTransport ring is the source of any unprocessed records, which are lost — accepted as a recovery cost). The agent starts a new interval at the current wallclock.
- **Agent crash mid-rotation**: same as above. Phase 2's rotation discipline ensures `.complete` is written last.
- **Observer crash**: observer is disposable (architecture §6). Restart, replay nothing — observer is a derived process, not durable storage.
- **Aggregator crash mid-bundle**: bundle build is idempotent (Phase 4 §7). Restart, the build resumes from a checkpoint or restarts entirely.
- **Sync master unreachable**: agent buffers intervals locally; uploads catch up when master returns.
- **NAS unreachable for aggregator**: bundle build fails clearly; operator retries when NAS returns.

### 9.4 Monitoring Hooks

Every adapter operation emits structured log events. Conventional schema:

```json
{
  "timestamp": "2026-05-21T14:23:17Z",
  "level": "Information",
  "category": "Tracer.Adapters.DDS",
  "message": "DDS sample captured",
  "properties": {
    "topicName": "weapons.fire",
    "publishWallclock": "2026-05-21T14:23:17.143Z",
    "translationDurationMicros": 12
  }
}
```

Operators can ingest these into their existing log aggregation (Splunk, ELK, etc.) for monitoring and alerting. Tracer doesn't ship its own metrics platform — the structured logs are the contract.

Aggregate metrics surfaced via the existing `/api/health` endpoint (Phase 3) with new fields:

```json
{
  "status": "Healthy",
  "agent": {
    "sharedMemoryDropped": 0,
    "ingestChannelDepth": 142,
    "intervalsAwaitingUpload": 0,
    "lastIntervalCompletedAtUtc": "2026-05-21T13:00:00Z"
  },
  "observer": {
    "ingestChannelDepth": 0,
    "sseConnectionsActive": 3
  }
}
```

These fields populate operations dashboards and trigger alerts.

---

## 10. Test Plan for Phase 11

### 10.1 Unit Tests (Run on Every PR)

**Adapters.DDS/DdsSampleTranslatorTests.cs** (with mock DDS samples)
- Translates Event-kind samples into EventRecord
- Translates SlowState-kind samples into StateSampleRecord with Slow kind
- Translates FastState-kind samples into StateSampleRecord with Fast kind and typed values
- Missing trace context fields throw clear error
- Sample with unregistered topic returns null (logged, not crash)

**Adapters.DDS/DdsTraceContextExtractorTests.cs**
- Reflective extraction succeeds for properties named `traceId`, `TraceId`
- Missing `traceId` property throws on first access
- Compiled accessor returns the same value as direct property access
- Accessor cache: second use of the same type doesn't recompile

**Adapters.SharedMemory/SharedMemoryRingBufferTests.cs**
- Create + Open from another process (use a test subprocess)
- Write + Read in sequence
- Wraparound (write enough to wrap, verify subsequent reads work)
- Drop-oldest when buffer fills
- Concurrent producer/consumer at 1000+ ops/sec, no data corruption (use deterministic record content for verification)

**Adapters.SharedMemory/SharedMemoryTransportTests.cs**
- Producer.EnqueueAsync + Consumer.ConsumeAsync round-trips a known set
- Consumer respects CancellationToken
- Producer doesn't block when consumer is slow (verified by timing)

**Adapters.SharedMemory/SharedMemoryDiagnosticRecordCodecTests.cs**
- EventRecord round-trips bit-for-bit
- StateSampleRecord round-trips
- Large payload (~10 KB) round-trips
- Unicode in payload preserved
- Source-gen codec is faster than reflection-based JSON (benchmark assertion)

**Adapters.Sync/SyncSystemUploadServiceTests.cs** (with HttpClient mocked)
- SubmitAsync calls correct endpoint with correct body
- WaitForCompletionAsync polls until Completed
- Polling honors cancellation
- Failed status surfaces correctly

**Adapters.Nas/NasStorageReaderTests.cs** (against local-fs simulating NAS)
- ListIntervalsAsync discovers intervals with `.complete` sentinel
- Skips intervals without sentinel
- StageAsync without local staging: returns SourcePath directly
- StageAsync with local staging: copies and cleans up

**AdapterSelection/AdapterRegistryTests.cs**
- `dataSource: "mock"` registers MockDataSource
- `dataSource: "dds"` registers DdsDiagnosticDataSource
- Invalid value: throws on registration
- Mixed config (mock data + real upload) works

### 10.2 Integration-Real Tests (Separate Lane)

See §8.2 above. Run nightly, on demand, and on release candidates.

### 10.3 Soak Tests

- 48-hour continuous run with real simulation harness
- Monitor resource trends (memory, CPU, disk, file handles)
- Assert: no leak slopes
- Assert: bundle build succeeds at any time
- Assert: viewer queries succeed throughout

---

## 11. Phase 11 Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Customer's DDS sample type system diverges from Tracer's assumptions | High | High | The translator is the only code that touches DDS-specific types; bugs surface as test failures in `Adapters.DDS` tests with mock samples. Real-data validation in integration-real catches the rest. Iterate. |
| Trace context fields aren't actually populated by all simulation code paths | High | High | Document this as the integration project's responsibility. Build a verification utility that scans a bundle's events for `trace_id=0`/`event_id=0` and reports the fraction — high values flag missing instrumentation. |
| Cyclone DDS C# bindings have surprising semantics (e.g., callback threading, sample ownership) | Medium | High | Wrap the binding behind `IDdsSample` so changes are local. Document threading expectations explicitly. Run with the customer's actual binding from day 1, not a mock. |
| Shared memory transport fails on Windows version older than expected | Low | High | Use System.IO.MemoryMappedFiles which is supported since .NET 8 baseline. Document minimum Windows version. |
| Source-gen JSON proves slow enough to throttle the SharedMemoryTransport | Medium | Medium | Bench early. If too slow, swap to MessagePack — contained refactor in `SharedMemoryDiagnosticRecordCodec.cs` only. |
| Sync system contract diverges from `sync_addendum_telemetry.md` | Medium | High | The sync addendum is owned by both teams jointly; divergence is detected at the contract layer. Versioned API endpoints would help; for Phase 11, assume the contract is stable and iterate via direct communication. |
| NAS path resolution differs between dev machines and production | Medium | Low | Configuration-driven; `NasRoot` is per-environment. Document examples. |
| `.complete` sentinel is missed by sync system's atomic upload | Medium | High | Verify in integration-real tests that the sync system writes `.complete` last. If it doesn't, the agent writes the sentinel into a side-channel (`completion-marker.json`) the aggregator checks via a parallel path. |
| Real simulation has bugs that look like Tracer bugs (e.g., `parent_event_id` pointing at events on different traces) | High | Low | The bundle's causal tree query is robust to this — it shows what the data says. The engineer recognizes the inconsistency; this is itself diagnostic value. |
| Process-level resource limits cause crashes that look like leaks | Medium | Medium | OS-level limits with `unhandled` event logged so the recovery path can identify "I was killed for memory". Documented as "agent will restart cleanly". |
| Aggregator running on a different machine than agents lacks NAS access | Low | Medium | Configuration-driven; aggregator's NAS path may be remote-mounted SMB. If NAS isn't reachable, bundle build fails cleanly with a clear message. |
| Soak test reveals a memory leak after 36 hours | Medium | Medium | Phase 11's hardening time budget includes a buffer for this. Each leak found is fixed; soak re-runs validate. |

---

## 12. Definition of Done for Phase 11

### Build & Run

- [ ] All adapter assemblies build clean: `Tracer.Adapters.DDS`, `Tracer.Adapters.SharedMemory`, `Tracer.Adapters.Sync`, `Tracer.Adapters.Nas`
- [ ] `Tracer.AdapterSelection` builds and exposes `AdapterRegistry`
- [ ] All host builders consume `AdapterRegistry` for DI
- [ ] Phase 1-10 mock-adapter tests continue to pass
- [ ] Customer's Cyclone DDS binding resolves as a NuGet reference (or vendored assembly)

### DDS Adapter

- [ ] `DdsDiagnosticDataSource` subscribes to all configured topics
- [ ] `DdsSampleTranslator` produces correct `EventRecord` / `StateSampleRecord` for each topic kind
- [ ] `DdsTraceContextExtractor` reads `trace_id`, `event_id`, `parent_event_id` correctly
- [ ] Bounded channel drops oldest on full; drop count logged
- [ ] Unit tests for translator with synthetic samples pass

### Shared Memory Transport

- [ ] `SharedMemoryRingBuffer` create/open works cross-process
- [ ] Write/read round-trip preserves bytes
- [ ] Wraparound handled correctly
- [ ] Drop-oldest activates on buffer full; `dropped_count` increments
- [ ] Producer never blocks
- [ ] Consumer respects cancellation with bounded latency (< 100 ms)
- [ ] Sustained 1000+ events/sec for 60 s with < 0.1% drop rate

### Sync Adapter

- [ ] `SyncSystemUploadService.SubmitAsync` registers intent via REST
- [ ] Polling progresses through Pending → Uploading → Completed
- [ ] Retries on transient failures
- [ ] Failed intent surfaces with error message
- [ ] Agent disk-side interval retained until upload Completed

### NAS Adapter

- [ ] `NasStorageReader.ListIntervalsAsync` discovers complete intervals via `.complete` sentinel
- [ ] Skips incomplete intervals
- [ ] `StageAsync` works with and without local staging

### Adapter Selection

- [ ] Configuration `adapters: { dataSource: "mock" | "dds", ... }` selects correctly
- [ ] Invalid value throws on startup with clear error
- [ ] Mock and real adapters interchangeable

### End-to-End Validation

- [ ] Customer simulation harness publishes; events appear in a TracerAgent's intervals
- [ ] Trace context propagates correctly through the full pipeline
- [ ] Bundle built from real-data session is valid
- [ ] All Phase 5-10 views render meaningfully against real-data bundle
- [ ] Phase 9 replication latency view shows realistic per-pair distributions

### Hardening

- [ ] Process-level resource limits enforced
- [ ] Drop telemetry visible via `/api/health` and operator message queue
- [ ] Agent restart recovery validated under simulated crash
- [ ] Observer restart preserves no data (it's disposable) but resumes cleanly
- [ ] Aggregator recovery: bundle build can be retried after failure

### Testing

- [ ] Phase 11 unit tests pass (target: 40+)
- [ ] Integration-real test suite passes on the dedicated lane
- [ ] 48-hour soak test passes: no resource leaks, agent and observer stable
- [ ] Performance: agent CPU < 50% (single core) at 5000 events/sec
- [ ] Performance: agent memory < configured limit

### Documentation

- [ ] `docs/adapters.md` documents the mock-vs-real choice and configuration
- [ ] `docs/dds-integration.md` explains required simulation-side discipline (trace context, source timestamps)
- [ ] `docs/shared-memory-transport.md` documents the IPC contract for the simulation team
- [ ] `docs/sync-integration.md` references the sync addendum and documents Tracer-side configuration
- [ ] `docs/operations.md` covers monitoring, resource limits, recovery procedures
- [ ] CHANGELOG entry — this is the **1.0** release

---

## 13. After Phase 11: The Road Forward

Phase 11 closes the planned build sequence. Tracer is a production-deployable diagnostic platform. Architecture §18 closes:

> *After Phase 11, Tracer is a production-deployable diagnostic platform. Further phases (performance optimization, additional specialized views, alerts, cross-session analysis if needed) are driven by specific operational needs.*

### 13.1 What's Done

Eleven phases, ten user-facing views, full per-node telemetry pipeline, real-adapter integration. The system answers:

- **Temporal questions** ("what happened across all nodes at this time?") — Timeline
- **Causal questions** ("what caused this?") — Causal Tree
- **Entity questions** ("what happened to this thing?") — Entity History
- **Performance questions** ("are we meeting latency budgets?") — Replication Latency
- **Topology questions** ("who talks to whom?") — Network Topology
- **Loss questions** ("are we losing messages?") — Gap Detection
- **Scenario questions** ("why didn't this trigger fire?") — Trigger Evaluation Log
- **Scenario flow questions** ("what was the engagement structure?") — Scenario View
- **Power-user questions** ("what about questions I haven't anticipated?") — SQL Console

Plus persistence (annotations, saved views, saved queries), organization (bundle library), and a clean live-mode/offline-mode duality (observer for current analysis, bundle for retrospective).

### 13.2 What Phase 11 Does NOT Close

- **Production-grade scaling beyond 200 nodes** — architecture targets ~200 nodes per architecture §1.1; larger fleets require additional design (sharded aggregator, hierarchical observers)
- **Multi-region or multi-master topologies** — single sync-master, single NAS
- **Authoritative authorization or audit trails** — Phase 8 noted personas are not authorization
- **External alerting integration** — log events are the contract; downstream pipelines are operator-owned
- **Adversarial security** — Phase 10's read-only SQL filtering is best-effort; the system trusts its operators

### 13.3 Likely Next Asks

Realistic post-1.0 work the customer is likely to surface:

- **Cross-session analysis**: comparing two bundles side by side ("yesterday vs. today's latency"). Either a new view or a SQL-Console pattern.
- **Custom dashboards**: arrangements of saved-query results in a layout. The Phase 10 SQL Console is the building block.
- **Specific simulation-team views**: triggered by specific debugging needs the team encounters. Add as Phase 12+.
- **Performance optimization**: as data volumes grow, specific queries get slower. Address via DuckDB tuning, additional indexes, materialized rollups.
- **Long-term storage**: bundles accumulate; archival to cold storage with read-on-demand is a future capability.

### 13.4 Closing Note

The architecture document set is the durable record of what Tracer is and how it works. Each phase document is a contract: what was built, how it was tested, what was deliberately deferred. Future maintainers reading these documents should be able to recover the design intent without consulting the original authors.

If a Phase 12 becomes necessary, it follows the same shape: scope, project layout, technical design, tests, risks, definition of done, handoff. The cadence persists.
