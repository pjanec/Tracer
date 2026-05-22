# BATCH-53 — Phase 11 Part A: Real Adapter Assemblies

**Tasks:** TRC-P11-001 through TRC-P11-004  
**Depends on:** BATCH-52 (committed — Phase 10 complete)

---

## Context

Phase 11 introduces real production adapter implementations for DDS, SharedMemory transport, Sync upload, and NAS storage reader. These are independent of each other and can be implemented in parallel.

Read before starting:
- `docs/tracer_phase11_design.md` §3 (DDS), §4 (SharedMemory), §5 (Sync), §6 (NAS)
- `docs/CycloneDDS.NET.README.md` — the CycloneDDS.NET binding API
- `docs/sync_addendum_telemetry.md §A3, §A4` — NAS layout and REST API
- `docs/TASK-DETAIL.md` sections TRC-P11-001 through TRC-P11-004

Survey existing patterns:
- `src/Tracer.Core/Abstractions/IDiagnosticDataSource.cs` — `ReadAsync(CancellationToken ct)` interface
- `src/Tracer.Core/Abstractions/IAgentTransport.cs` — `ReadAsync` + `GetHealth()` interface
- `src/Tracer.Core/Abstractions/ITelemetryUploadService.cs` — upload interface
- `src/Tracer.Core/Abstractions/ITelemetryStorageReader.cs` — storage reader interface
- `src/Tracer.Core/Records/DiagnosticRecord.cs`, `EventRecord.cs`, `StateSampleRecord.cs` — domain records
- `src/Tracer.Adapters.Mock/Transport/InProcessChannelTransport.cs` — transport pattern
- `src/Tracer.Adapters.Mock/Storage/LocalFileSystemStorageReader.cs` — storage reader pattern
- `src/Tracer.Adapters.Mock/MockDataSource.cs` — data source pattern
- `src/Tracer.Adapters.Mock/Tracer.Adapters.Mock.csproj` — project structure to copy
- `Directory.Build.props` and `Directory.Packages.props` — package versions

---

## TRC-P11-001: `Tracer.Adapters.DDS` Assembly

### Project Setup

Create `src/Tracer.Adapters.DDS/Tracer.Adapters.DDS.csproj`:

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
    <PackageReference Include="CycloneDDS.NET" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Tracer.Core\Tracer.Core.csproj" />
  </ItemGroup>
</Project>
```

Add `CycloneDDS.NET` to `Directory.Packages.props` if not already present. Use latest stable version (check NuGet for the correct version).

Add to solution: `dotnet sln Tracer.sln add src/Tracer.Adapters.DDS/Tracer.Adapters.DDS.csproj`

### Files to Create

#### `src/Tracer.Adapters.DDS/IDdsSample.cs`

Tracer's abstraction over the customer's DDS binding, isolating Tracer.Core from CycloneDDS types:

```csharp
namespace Tracer.Adapters.DDS;

/// <summary>
/// Abstraction over a DDS sample, isolating Tracer.Core from CycloneDDS types.
/// </summary>
public interface IDdsSample
{
    /// <summary>Timestamp set by dds_write_ts() at publish time.</summary>
    DateTimeOffset SourceTimestamp { get; }

    /// <summary>DDS sequence number (monotonically increasing per writer).</summary>
    ulong SequenceNumber { get; }

    /// <summary>The typed sample payload object (customer's DDS IDL-generated class).</summary>
    object GetPayload();
}
```

#### `src/Tracer.Adapters.DDS/DdsTopicKind.cs`

```csharp
namespace Tracer.Adapters.DDS;

public enum DdsTopicKind { Event, SlowState, FastState }
```

#### `src/Tracer.Adapters.DDS/DdsTopicMetadata.cs`

```csharp
namespace Tracer.Adapters.DDS;

public sealed record DdsTopicMetadata
{
    public required string TopicName { get; init; }
    public required Type SampleType { get; init; }
    public required DdsTopicKind Kind { get; init; }
    public required string EntityIdField { get; init; }
    public string? OwningPlayerIdField { get; init; }
    public string? SeverityField { get; init; }
    public string? NotableLabelField { get; init; }
    public string? InstanceKeyField { get; init; }
    /// <summary>Fields that constitute typed values for FastState (mapped to Parquet columns).</summary>
    public IReadOnlyList<string> TypedValueFields { get; init; } = Array.Empty<string>();
}
```

#### `src/Tracer.Adapters.DDS/DdsTopicRegistry.cs`

Dictionary-backed catalog. Populated from config at startup.

```csharp
namespace Tracer.Adapters.DDS;

public sealed class DdsTopicRegistry
{
    private readonly Dictionary<string, DdsTopicMetadata> _byName;

    public DdsTopicRegistry(IEnumerable<DdsTopicMetadata> topics)
    {
        _byName = topics.ToDictionary(t => t.TopicName, StringComparer.Ordinal);
    }

    public DdsTopicMetadata? Lookup(string topicName) =>
        _byName.GetValueOrDefault(topicName);

    public IReadOnlyCollection<DdsTopicMetadata> All => _byName.Values;
}
```

#### `src/Tracer.Adapters.DDS/TraceContext.cs`

```csharp
using Tracer.Core.Identity;

namespace Tracer.Adapters.DDS;

public sealed record TraceContext
{
    public required ulong TraceId { get; init; }
    public required EventId EventId { get; init; }
    public required EventId ParentEventId { get; init; }

    public static TraceContext Empty => new()
    {
        TraceId = 0,
        EventId = new EventId(0),
        ParentEventId = new EventId(0),
    };
}
```

#### `src/Tracer.Adapters.DDS/DdsTraceContextExtractor.cs`

Use `System.Linq.Expressions` to compile typed delegate accessors per sample type (cached in `ConcurrentDictionary<Type, TraceContextAccessors>`). Fields checked in order: camelCase (`traceId`) then PascalCase (`TraceId`). If neither found, throw `InvalidOperationException` with the type name in the message. Non-Event topics return `TraceContext.Empty` immediately.

Pattern from design §3.6:
```csharp
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Tracer.Core.Identity;

namespace Tracer.Adapters.DDS;

public sealed class DdsTraceContextExtractor
{
    private readonly ConcurrentDictionary<Type, TraceContextAccessors> _cache = new();

    public TraceContext Extract(IDdsSample sample, DdsTopicMetadata meta)
    {
        if (meta.Kind != DdsTopicKind.Event)
            return TraceContext.Empty;

        var accessors = _cache.GetOrAdd(meta.SampleType, BuildAccessors);
        var payload = sample.GetPayload();
        return new TraceContext
        {
            TraceId = accessors.TraceId(payload),
            EventId = new EventId(accessors.EventId(payload)),
            ParentEventId = new EventId(accessors.ParentEventId(payload)),
        };
    }

    private static TraceContextAccessors BuildAccessors(Type t)
    {
        return new TraceContextAccessors
        {
            TraceId = BuildUlongAccessor(t, "traceId", "TraceId"),
            EventId = BuildUlongAccessor(t, "eventId", "EventId"),
            ParentEventId = BuildUlongAccessor(t, "parentEventId", "ParentEventId"),
        };
    }

    private static Func<object, ulong> BuildUlongAccessor(Type t, string camel, string pascal)
    {
        var prop = t.GetProperty(camel) ?? t.GetProperty(pascal)
            ?? throw new InvalidOperationException(
                $"Sample type {t.Name} missing {camel}/{pascal} property required for trace context");
        var param = Expression.Parameter(typeof(object), "obj");
        var cast = Expression.Convert(param, t);
        var access = Expression.Property(cast, prop);
        var convert = Expression.Convert(access, typeof(ulong));
        return Expression.Lambda<Func<object, ulong>>(convert, param).Compile();
    }

    private sealed class TraceContextAccessors
    {
        public required Func<object, ulong> TraceId { get; init; }
        public required Func<object, ulong> EventId { get; init; }
        public required Func<object, ulong> ParentEventId { get; init; }
    }
}
```

#### `src/Tracer.Adapters.DDS/DdsSampleTranslator.cs`

Translates `IDdsSample` to `DiagnosticRecord` based on topic kind. Uses reflection for non-trace fields (entity, player, severity, payload). The design §3.4 has full details. Key points:
- `publish_wallclock` = `sample.SourceTimestamp`
- `receive_wallclock` = `_clock.UtcNow` (IClock injected)
- loopback: `PublisherNode == SubscriberNode` (both = `config.PublisherNodeId`)
- Unknown topic returns null + logs warning
- Fast state: extract TypedValues via reflection on sample payload using `TypedValueFields` list
- Payload JSON: `JsonSerializer.Serialize(sample.GetPayload())` 

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;

namespace Tracer.Adapters.DDS;

public sealed class DdsSampleTranslator
{
    private readonly DdsTraceContextExtractor _traceExtractor;
    private readonly DdsTopicRegistry _topicRegistry;
    private readonly DdsAdapterConfig _config;
    private readonly IClock _clock;
    private readonly ILogger<DdsSampleTranslator> _logger;

    // ... constructor

    public DiagnosticRecord? Translate(IDdsSample sample, DdsTopicSubscription topicSub)
    {
        var meta = _topicRegistry.Lookup(topicSub.TopicName);
        if (meta is null)
        {
            _logger.LogWarning("Topic {Topic} not in DdsTopicRegistry; skipping sample", topicSub.TopicName);
            return null;
        }

        var publishWallclock = WallclockTime.FromDateTimeOffset(sample.SourceTimestamp);
        var receiveWallclock = WallclockTime.FromDateTimeOffset(_clock.UtcNow);
        var traceContext = _traceExtractor.Extract(sample, meta);
        var payload = sample.GetPayload();
        var payloadJson = JsonSerializer.Serialize(payload);

        var publisherNode = new AgentId(_config.PublisherNodeId);

        return meta.Kind switch
        {
            DdsTopicKind.Event => new EventRecord
            {
                SequenceNumber = sample.SequenceNumber,
                PublishWallclock = publishWallclock,
                ReceiveWallclock = receiveWallclock,
                PublisherNode = publisherNode,
                SubscriberNode = publisherNode,   // loopback
                Topic = new TopicName(topicSub.TopicName),
                EventId = traceContext.EventId,
                TraceId = new TraceId(traceContext.TraceId),
                ParentEventId = traceContext.ParentEventId.Value == 0 ? null : traceContext.ParentEventId,
                EntityId = ExtractStringField(payload, meta.EntityIdField) is { } eid ? new EntityId(eid) : null,
                OwningPlayerId = meta.OwningPlayerIdField is not null ? ExtractStringField(payload, meta.OwningPlayerIdField) : null,
                ScenarioPhase = null,
                Severity = null,
                NotableLabel = meta.NotableLabelField is not null ? ExtractStringField(payload, meta.NotableLabelField) : null,
                PayloadJson = payloadJson,
            },
            DdsTopicKind.SlowState => new StateSampleRecord
            {
                SequenceNumber = sample.SequenceNumber,
                PublishWallclock = publishWallclock,
                ReceiveWallclock = receiveWallclock,
                PublisherNode = publisherNode,
                SubscriberNode = publisherNode,
                Topic = new TopicName(topicSub.TopicName),
                InstanceKey = ExtractStringField(payload, meta.InstanceKeyField ?? meta.EntityIdField) ?? "",
                TraceId = traceContext.TraceId > 0 ? new TraceId(traceContext.TraceId) : null,
                PayloadJson = payloadJson,
                Rate = StateSampleRate.Slow,
            },
            DdsTopicKind.FastState => new StateSampleRecord
            {
                SequenceNumber = sample.SequenceNumber,
                PublishWallclock = publishWallclock,
                ReceiveWallclock = receiveWallclock,
                PublisherNode = publisherNode,
                SubscriberNode = publisherNode,
                Topic = new TopicName(topicSub.TopicName),
                InstanceKey = ExtractStringField(payload, meta.InstanceKeyField ?? meta.EntityIdField) ?? "",
                TraceId = null,
                PayloadJson = payloadJson,
                Rate = StateSampleRate.Fast,
            },
            _ => null,
        };
    }

    private static string? ExtractStringField(object payload, string fieldName)
    {
        return payload.GetType().GetProperty(fieldName)?.GetValue(payload)?.ToString();
    }
}
```

#### `src/Tracer.Adapters.DDS/DdsSubscriberFactory.cs`

Wraps CycloneDDS.NET API. Read `docs/CycloneDDS.NET.README.md` to understand the actual API. The key pattern:

The CycloneDDS.NET binding has `DdsParticipant`, `DdsReader<T>`, and event-based callbacks. Create a reader per topic using the `DdsParticipant`, register a data-available callback that calls `onSample`.

```csharp
using CycloneDDS;
using Microsoft.Extensions.Logging;

namespace Tracer.Adapters.DDS;

public sealed class DdsSubscriberFactory
{
    private readonly DdsParticipant _participant;
    private readonly ILogger<DdsSubscriberFactory> _logger;

    public DdsSubscriberFactory(DdsParticipant participant, ILogger<DdsSubscriberFactory> logger)
    {
        _participant = participant;
        _logger = logger;
    }

    public IDisposable Create(DdsTopicSubscription topicSub, Type sampleType, Action<IDdsSample> onSample)
    {
        // Use the CycloneDDS.NET API to create a typed reader via reflection
        // since we don't know the sample type at compile time.
        // Pattern: _participant.CreateReader<T>(topicName) → registers event handler
        // Returns IDisposable that disposes the reader
        
        // Use reflection to call the generic CreateReader<T> method
        var createReaderMethod = typeof(DdsParticipant)
            .GetMethod("CreateReader")!
            .MakeGenericMethod(sampleType);
        
        var reader = createReaderMethod.Invoke(_participant, new object[] { topicSub.TopicName });
        
        // Register callback - the specific event name/type depends on CycloneDDS.NET API
        // Register DataAvailable event using reflection
        // Each sample received: wrap in DdsSampleWrapper and call onSample
        
        return new ReaderHandle(reader as IDisposable);
    }

    private sealed class ReaderHandle : IDisposable
    {
        private readonly IDisposable? _reader;
        public ReaderHandle(IDisposable? reader) { _reader = reader; }
        public void Dispose() { _reader?.Dispose(); }
    }
}
```

**IMPORTANT**: Read `docs/CycloneDDS.NET.README.md` carefully for the actual API. The design has pseudocode; you must implement using the real CycloneDDS.NET API. The key points are:
1. `DdsParticipant` (or `DdsDomainParticipant`) is the entry point
2. Readers are generic `DdsReader<T>` where T is the sample type  
3. There's likely a `DataAvailable` event or similar callback mechanism
4. `IDdsSample` must be implemented as a wrapper around the actual sample

**If the actual CycloneDDS.NET API differs from the pseudocode in the design**, implement it correctly per the real API. The abstraction (`IDdsSample`) stays the same but the implementation adapts.

#### `src/Tracer.Adapters.DDS/DdsDiagnosticDataSource.cs`

Implements `IDiagnosticDataSource.ReadAsync(CancellationToken ct)`:

```csharp
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Tracer.Core.Abstractions;
using Tracer.Core.Records;

namespace Tracer.Adapters.DDS;

public sealed class DdsDiagnosticDataSource : IDiagnosticDataSource
{
    private readonly DdsAdapterConfig _config;
    private readonly DdsSubscriberFactory _subscriberFactory;
    private readonly DdsSampleTranslator _translator;
    private readonly ILogger<DdsDiagnosticDataSource> _logger;
    private int _dropBurstCount;   // for throttled warning logging

    // ... constructor

    public async IAsyncEnumerable<DiagnosticRecord> ReadAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateBounded<DiagnosticRecord>(
            new BoundedChannelOptions(_config.IngestBufferSize)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });

        var subscribers = new List<IDisposable>();
        foreach (var topicSub in _config.Topics)
        {
            var meta = _topicRegistry.Lookup(topicSub.TopicName);
            if (meta is null) continue;
            var sub = _subscriberFactory.Create(topicSub, meta.SampleType,
                sample => OnSampleReceived(sample, topicSub, channel.Writer));
            subscribers.Add(sub);
        }

        try
        {
            while (await channel.Reader.WaitToReadAsync(ct))
                while (channel.Reader.TryRead(out var record))
                    yield return record;
        }
        finally
        {
            foreach (var s in subscribers) s.Dispose();
        }
    }

    private void OnSampleReceived(IDdsSample sample, DdsTopicSubscription topicSub, ChannelWriter<DiagnosticRecord> writer)
    {
        try
        {
            var record = _translator.Translate(sample, topicSub);
            if (record is null) return;
            if (!writer.TryWrite(record))
            {
                // Throttle: only log one warning per drop burst
                if (Interlocked.Exchange(ref _dropBurstCount, 1) == 0)
                    _logger.LogWarning("DDS ingest channel full, dropping samples for topic {Topic}", topicSub.TopicName);
            }
            else
            {
                Interlocked.Exchange(ref _dropBurstCount, 0); // reset on successful write
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to translate DDS sample on topic {Topic}", topicSub.TopicName);
        }
    }
}
```

#### `src/Tracer.Adapters.DDS/Configuration/DdsAdapterConfig.cs`

```csharp
namespace Tracer.Adapters.DDS.Configuration;

public sealed class DdsAdapterConfig
{
    public required string PublisherNodeId { get; init; }
    public required IReadOnlyList<DdsTopicSubscription> Topics { get; init; }
    public int IngestBufferSize { get; init; } = 50_000;
    public required CycloneDdsParticipantConfig Participant { get; init; }
}

public sealed class DdsTopicSubscription
{
    public required string TopicName { get; init; }
    public required string SampleTypeName { get; init; }   // resolved at startup
}

public sealed class CycloneDdsParticipantConfig
{
    public required int DomainId { get; init; }
    public string? QosProfile { get; init; }
}
```

### Tests for TRC-P11-001

Create `tests/Tracer.Tests.Unit/Adapters/DDS/DdsSampleTranslatorTests.cs` — minimum 10 tests covering success conditions from TRC-P11-001 (see TASK-DETAIL.md). Key patterns:
- Create a mock `IDdsSample` implementation (POCO class implementing interface) 
- Create sample types with the appropriate fields as properties (can be in-test `private class`)
- Use `NullLogger<DdsSampleTranslator>.Instance`

Create `tests/Tracer.Tests.Unit/Adapters/DDS/DdsTraceContextExtractorTests.cs` — minimum 4 tests (success conditions 5-8).

Create `tests/Tracer.Tests.Unit/Adapters/DDS/DdsDiagnosticDataSourceTests.cs` — minimum 2 tests (success conditions 9-10).

---

## TRC-P11-002: `Tracer.Adapters.SharedMemory` Assembly

### Project Setup

Create `src/Tracer.Adapters.SharedMemory/Tracer.Adapters.SharedMemory.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Tracer.Core\Tracer.Core.csproj" />
  </ItemGroup>
</Project>
```

Add to solution.

### Files to Create

#### `src/Tracer.Adapters.SharedMemory/SharedMemoryRingBuffer.cs`

SPSC ring buffer over `MemoryMappedFile`. Key design from §4.3:

```csharp
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Tracer.Adapters.SharedMemory;

/// <summary>SPSC ring buffer backed by a named shared memory region.</summary>
public sealed class SharedMemoryRingBuffer : IDisposable
{
    // Header layout (all at fixed offsets from start of mapping):
    // [0..7]   magic "TRCRSHM\0" (8 bytes)
    // [8..11]  version = 1 (int32)
    // [12..19] capacity (int64)
    // [20..27] write_offset (int64, volatile)
    // [28..35] read_offset (int64, volatile)
    // [36..39] producer_pid (int32)
    // [40..43] consumer_pid (int32)
    // [44..51] producer_heartbeat (int64)
    // [52..59] consumer_heartbeat (int64)
    // [60..67] dropped_count (int64, volatile)
    // [68..4095] reserved / padding
    private const int HeaderSize = 4096;
    private const string Magic = "TRCRSHM\0";
    
    private const int OffsetVersion = 8;
    private const int OffsetCapacity = 12;
    private const int OffsetWriteOffset = 20;
    private const int OffsetReadOffset = 28;
    private const int OffsetProducerPid = 36;
    private const int OffsetConsumerPid = 40;
    private const int OffsetProducerHeartbeat = 44;
    private const int OffsetConsumerHeartbeat = 52;
    private const int OffsetDroppedCount = 60;

    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly long _capacity;
    private readonly bool _isProducer;
    private readonly unsafe byte* _basePtr;

    private SharedMemoryRingBuffer(MemoryMappedFile mmf, MemoryMappedViewAccessor accessor, long capacity, bool isProducer)
    {
        _mmf = mmf;
        _accessor = accessor;
        _capacity = capacity;
        _isProducer = isProducer;
        // Get the raw pointer for high-performance reads/writes
        unsafe
        {
            byte* ptr = null;
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            _basePtr = ptr;
        }
    }

    public static SharedMemoryRingBuffer Create(string name, long capacity)
    {
        var totalSize = HeaderSize + capacity;
        var mmf = MemoryMappedFile.CreateOrOpen(name, totalSize);
        var accessor = mmf.CreateViewAccessor(0, totalSize);
        
        // Write magic
        var magicBytes = Encoding.ASCII.GetBytes(Magic);
        accessor.WriteArray(0, magicBytes, 0, 8);
        accessor.Write(OffsetVersion, 1);
        accessor.Write(OffsetCapacity, capacity);
        accessor.Write(OffsetWriteOffset, 0L);
        accessor.Write(OffsetReadOffset, 0L);
        accessor.Write(OffsetProducerPid, Environment.ProcessId);
        accessor.Write(OffsetConsumerPid, 0);
        accessor.Write(OffsetProducerHeartbeat, 0L);
        accessor.Write(OffsetConsumerHeartbeat, 0L);
        accessor.Write(OffsetDroppedCount, 0L);
        
        return new SharedMemoryRingBuffer(mmf, accessor, capacity, isProducer: true);
    }

    public static SharedMemoryRingBuffer Open(string name)
    {
        var mmf = MemoryMappedFile.OpenExisting(name);
        // Create a small accessor to read the header
        var headerAccessor = mmf.CreateViewAccessor(0, HeaderSize);
        var magic = new byte[8];
        headerAccessor.ReadArray(0, magic, 0, 8);
        if (Encoding.ASCII.GetString(magic) != Magic)
            throw new InvalidOperationException("Shared memory region has invalid magic bytes");
        var capacity = headerAccessor.ReadInt64(OffsetCapacity);
        headerAccessor.Dispose();
        
        var accessor = mmf.CreateViewAccessor(0, HeaderSize + capacity);
        accessor.Write(OffsetConsumerPid, Environment.ProcessId);
        return new SharedMemoryRingBuffer(mmf, accessor, capacity, isProducer: false);
    }

    /// <summary>Producer: writes record bytes. Drop-oldest if full. Returns false if record too large for buffer.</summary>
    public bool TryWrite(ReadOnlySpan<byte> record)
    {
        if (!_isProducer) throw new InvalidOperationException("Cannot write from consumer side");
        if (record.Length + 4 > _capacity) return false;

        var writeOff = ReadAtomicLong(OffsetWriteOffset);
        var readOff = ReadAtomicLong(OffsetReadOffset);

        // If writing would straddle the end of the buffer, write a 0-length padding marker and wrap
        if (writeOff + 4 + record.Length > _capacity)
        {
            WriteLengthAt(writeOff, 0);  // padding marker
            writeOff = 0;
        }

        // Drop-oldest: advance read pointer past records until there's room
        long required = record.Length + 4;
        while (FreeSpace(writeOff, readOff) < required)
        {
            var skipped = AdvanceOnce(readOff);
            if (skipped < 0) break; // shouldn't happen
            readOff = skipped;
            IncrementDropped();
        }

        // Write the record
        WriteLengthAt(writeOff, record.Length);
        var dataOffset = HeaderSize + writeOff + 4;
        unsafe
        {
            fixed (byte* src = record)
                Buffer.MemoryCopy(src, _basePtr + dataOffset, record.Length, record.Length);
        }

        var newWriteOff = writeOff + 4 + record.Length;
        if (newWriteOff >= _capacity) newWriteOff = 0;
        WriteAtomicLong(OffsetReadOffset, readOff);   // commit dropped advances
        WriteAtomicLong(OffsetWriteOffset, newWriteOff);
        return true;
    }

    /// <summary>Consumer: reads and removes next record. Returns null if empty.</summary>
    public byte[]? TryRead()
    {
        if (_isProducer) throw new InvalidOperationException("Cannot read from producer side");
        var writeOff = ReadAtomicLong(OffsetWriteOffset);
        var readOff = ReadAtomicLong(OffsetReadOffset);
        if (writeOff == readOff) return null;

        var length = ReadLengthAt(readOff);
        if (length == 0)
        {
            // Padding marker: wrap
            WriteAtomicLong(OffsetReadOffset, 0);
            return TryRead();
        }

        var result = new byte[length];
        unsafe
        {
            fixed (byte* dst = result)
                Buffer.MemoryCopy(_basePtr + HeaderSize + readOff + 4, dst, length, length);
        }

        var newReadOff = readOff + 4 + length;
        if (newReadOff >= _capacity) newReadOff = 0;
        WriteAtomicLong(OffsetReadOffset, newReadOff);
        return result;
    }

    public long GetDroppedCount() => ReadAtomicLong(OffsetDroppedCount);

    private long FreeSpace(long write, long read)
    {
        var used = write >= read ? write - read : _capacity - read + write;
        return _capacity - used;
    }

    private long AdvanceOnce(long readOff)
    {
        var length = ReadLengthAt(readOff);
        if (length == 0) return 0; // padding marker → wrap
        return readOff + 4 + length;
    }

    private void IncrementDropped()
    {
        // Atomic increment
        unsafe
        {
            ref long dropped = ref Unsafe.AsRef<long>(_basePtr + OffsetDroppedCount);
            Interlocked.Increment(ref dropped);
        }
    }

    private unsafe long ReadAtomicLong(int offset)
    {
        return Volatile.Read(ref Unsafe.AsRef<long>(_basePtr + offset));
    }

    private unsafe void WriteAtomicLong(int offset, long value)
    {
        Volatile.Write(ref Unsafe.AsRef<long>(_basePtr + offset), value);
    }

    private unsafe int ReadLengthAt(long bufferOffset)
    {
        return Volatile.Read(ref Unsafe.AsRef<int>(_basePtr + HeaderSize + bufferOffset));
    }

    private unsafe void WriteLengthAt(long bufferOffset, int length)
    {
        Volatile.Write(ref Unsafe.AsRef<int>(_basePtr + HeaderSize + bufferOffset), length);
    }

    public void Dispose()
    {
        unsafe { _accessor.SafeMemoryMappedViewHandle.ReleasePointer(); }
        _accessor.Dispose();
        _mmf.Dispose();
    }
}
```

#### `src/Tracer.Adapters.SharedMemory/SharedMemoryDiagnosticRecordCodec.cs`

Source-generated JSON serializer for round-tripping DiagnosticRecord (EventRecord / StateSampleRecord):

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Tracer.Core.Records;

namespace Tracer.Adapters.SharedMemory;

/// <summary>Wrapper carrying record kind discriminator for polymorphic JSON.</summary>
internal sealed class SerializedRecord
{
    public required string Kind { get; set; }   // "Event" | "StateSlow" | "StateFast"
    public required string Json { get; set; }   // nested JSON of the actual record
}

[JsonSerializable(typeof(SerializedRecord))]
[JsonSerializable(typeof(EventRecord))]
[JsonSerializable(typeof(StateSampleRecord))]
internal partial class DiagnosticRecordSerializerContext : JsonSerializerContext { }

public sealed class SharedMemoryDiagnosticRecordCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = DiagnosticRecordSerializerContext.Default,
    };

    public byte[] Encode(DiagnosticRecord record)
    {
        var (kind, inner) = record switch
        {
            EventRecord e => ("Event", JsonSerializer.Serialize(e, DiagnosticRecordSerializerContext.Default.EventRecord)),
            StateSampleRecord s when s.Rate == StateSampleRate.Slow => ("StateSlow", JsonSerializer.Serialize(s, DiagnosticRecordSerializerContext.Default.StateSampleRecord)),
            StateSampleRecord s => ("StateFast", JsonSerializer.Serialize(s, DiagnosticRecordSerializerContext.Default.StateSampleRecord)),
            _ => throw new NotSupportedException($"Unsupported record type: {record.GetType()}")
        };
        var wrapper = new SerializedRecord { Kind = kind, Json = inner };
        return JsonSerializer.SerializeToUtf8Bytes(wrapper, DiagnosticRecordSerializerContext.Default.SerializedRecord);
    }

    public DiagnosticRecord? Decode(byte[] bytes)
    {
        var wrapper = JsonSerializer.Deserialize(bytes, DiagnosticRecordSerializerContext.Default.SerializedRecord);
        if (wrapper is null) return null;
        return wrapper.Kind switch
        {
            "Event" => JsonSerializer.Deserialize(wrapper.Json, DiagnosticRecordSerializerContext.Default.EventRecord),
            "StateSlow" or "StateFast" => JsonSerializer.Deserialize(wrapper.Json, DiagnosticRecordSerializerContext.Default.StateSampleRecord),
            _ => null,
        };
    }
}
```

**IMPORTANT**: EventRecord and StateSampleRecord use value types for most identity fields (AgentId, TopicName, etc.). The source-generated serializer context must be able to serialize/deserialize those. Survey `src/Tracer.Core/Identity/` and `src/Tracer.Core/Domain/` to understand the identity types, and add additional `[JsonSerializable]` attributes as needed. The codec should fall back gracefully if a field uses a value type that needs a custom converter.

#### `src/Tracer.Adapters.SharedMemory/SharedMemoryWriter.cs`

Producer-side helper:
```csharp
using Tracer.Core.Records;

namespace Tracer.Adapters.SharedMemory;

public sealed class SharedMemoryWriter : IDisposable
{
    private readonly SharedMemoryRingBuffer _buffer;
    private readonly SharedMemoryDiagnosticRecordCodec _codec;
    private readonly Semaphore _semaphore;

    public SharedMemoryWriter(string name, string semaphoreName, long capacity)
    {
        _buffer = SharedMemoryRingBuffer.Create(name, capacity);
        _codec = new SharedMemoryDiagnosticRecordCodec();
        _semaphore = new Semaphore(0, int.MaxValue, semaphoreName);
    }

    public bool Write(DiagnosticRecord record)
    {
        var bytes = _codec.Encode(record);
        var written = _buffer.TryWrite(bytes);
        if (written) _semaphore.Release(1);
        return written;
    }

    public long GetDroppedCount() => _buffer.GetDroppedCount();

    public void Dispose() { _buffer.Dispose(); _semaphore.Dispose(); }
}
```

#### `src/Tracer.Adapters.SharedMemory/SharedMemoryReader.cs`

Consumer-side helper:
```csharp
using Tracer.Core.Records;

namespace Tracer.Adapters.SharedMemory;

public sealed class SharedMemoryReader : IDisposable
{
    private readonly SharedMemoryRingBuffer _buffer;
    private readonly SharedMemoryDiagnosticRecordCodec _codec;
    private readonly Semaphore _semaphore;

    public SharedMemoryReader(string name, string semaphoreName)
    {
        _buffer = SharedMemoryRingBuffer.Open(name);
        _codec = new SharedMemoryDiagnosticRecordCodec();
        _semaphore = Semaphore.OpenExisting(semaphoreName);
    }

    /// <summary>Drain all available records without blocking.</summary>
    public IEnumerable<DiagnosticRecord> ReadAvailable()
    {
        while (true)
        {
            var bytes = _buffer.TryRead();
            if (bytes is null) yield break;
            var record = _codec.Decode(bytes);
            if (record is not null) yield return record;
        }
    }

    /// <summary>Wait up to timeout for data, then drain. Returns empty if timeout.</summary>
    public IEnumerable<DiagnosticRecord> WaitAndRead(TimeSpan timeout)
    {
        _semaphore.WaitOne(timeout);
        return ReadAvailable();
    }

    public void Dispose() { _buffer.Dispose(); _semaphore.Dispose(); }
}
```

#### `src/Tracer.Adapters.SharedMemory/SharedMemoryTransport.cs`

Implements `IAgentTransport`:

```csharp
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Tracer.Core.Abstractions;
using Tracer.Core.Records;
using Tracer.Core.Time;

namespace Tracer.Adapters.SharedMemory;

/// <summary>
/// Production IAgentTransport implementation using shared memory ring buffer IPC.
/// </summary>
public sealed class SharedMemoryTransport : IAgentTransport
{
    private readonly SharedMemoryConfig _config;
    private readonly ILogger<SharedMemoryTransport> _logger;
    private SharedMemoryWriter? _writer;
    private SharedMemoryReader? _reader;
    private long _totalReceived;

    public SharedMemoryTransport(SharedMemoryConfig config, ILogger<SharedMemoryTransport> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>Creates the producer side (simulation process calls this).</summary>
    public SharedMemoryWriter CreateProducer()
    {
        _writer = new SharedMemoryWriter(_config.SharedMemoryName, _config.SemaphoreName, _config.CapacityBytes);
        return _writer;
    }

    /// <summary>Creates the consumer side (TracerAgent calls this).</summary>
    public SharedMemoryReader CreateConsumer()
    {
        _reader = new SharedMemoryReader(_config.SharedMemoryName, _config.SemaphoreName);
        return _reader;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<DiagnosticRecord> ReadAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var reader = _reader ?? CreateConsumer();
        while (!ct.IsCancellationRequested)
        {
            var records = await Task.Run(() => reader.WaitAndRead(TimeSpan.FromMilliseconds(100)), ct)
                .ContinueWith(t => ct.IsCancellationRequested ? Enumerable.Empty<DiagnosticRecord>() : t.Result, ct);
            foreach (var r in records)
            {
                Interlocked.Increment(ref _totalReceived);
                yield return r;
            }
        }
    }

    /// <inheritdoc/>
    public TransportHealth GetHealth() => new()
    {
        PendingCount = 0,    // ring buffer doesn't expose pending count directly
        Capacity = (int)(_config.CapacityBytes / 1024),    // KB
        TotalReceived = Interlocked.Read(ref _totalReceived),
        DroppedCount = _writer?.GetDroppedCount() ?? 0,
        LastReceivedAt = default,
    };

    public long GetDroppedCount() => _writer?.GetDroppedCount() ?? (_reader is not null ? OpenDropCount() : 0);

    private long OpenDropCount()
    {
        // Read drop count from shared memory without creating a full reader
        return 0L; // simplified; the reader already has buffer access
    }

    public ValueTask DisposeAsync()
    {
        _writer?.Dispose();
        _reader?.Dispose();
        return ValueTask.CompletedTask;
    }
}
```

**Note**: `TransportHealth` may not have `DroppedCount` field in the existing interface. Check `src/Tracer.Core/Abstractions/IAgentTransport.cs` for the exact fields. Only implement what the interface requires. If `DroppedCount` is not there, expose it via a separate `GetDroppedCount()` method or add to the implementation only.

#### `src/Tracer.Adapters.SharedMemory/Configuration/SharedMemoryConfig.cs`

```csharp
namespace Tracer.Adapters.SharedMemory.Configuration;

public sealed class SharedMemoryConfig
{
    public string SharedMemoryName { get; init; } = "TracerRingBuffer";
    public string SemaphoreName { get; init; } = "TracerSyncSem";
    public long CapacityBytes { get; init; } = 64 * 1024 * 1024;   // 64 MB
}
```

### Tests for TRC-P11-002

Create `tests/Tracer.Tests.Unit/Adapters/SharedMemory/SharedMemoryRingBufferTests.cs` — minimum 5 tests:
1. Sequential write/read round-trip (3 records)
2. Wraparound (write until wrap, read back in order)
3. Drop-oldest on fill (dropped_count incremented)
4. Padding-marker handling (record near end wrapped to start)
5. Consumer TryRead returns null on empty

Create `tests/Tracer.Tests.Unit/Adapters/SharedMemory/SharedMemoryTransportTests.cs` — minimum 3 tests:
1. Round-trip (100 records, all arrive in order) — use in-process shared mapping
2. CancellationToken stops ReadAsync
3. Producer does not block (measure wall time of Write calls)

Create `tests/Tracer.Tests.Unit/Adapters/SharedMemory/SharedMemoryDiagnosticRecordCodecTests.cs` — minimum 2 tests:
1. EventRecord round-trip (all fields)
2. StateSampleRecord round-trip (slow + fast)

**Note**: For in-process testing, create both `SharedMemoryRingBuffer.Create(name, ...)` and `SharedMemoryRingBuffer.Open(name)` with the same `name` in the same test, using a GUID-based name to avoid conflicts.

---

## TRC-P11-003: `Tracer.Adapters.Sync` Assembly

### Project Setup

Create `src/Tracer.Adapters.Sync/Tracer.Adapters.Sync.csproj`:

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
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Http" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Tracer.Core\Tracer.Core.csproj" />
  </ItemGroup>
</Project>
```

Add `Microsoft.Extensions.Http` to `Directory.Packages.props` if not present.

### Files to Create

#### `src/Tracer.Adapters.Sync/Configuration/SyncAdapterConfig.cs`

```csharp
namespace Tracer.Adapters.Sync.Configuration;

public sealed class SyncAdapterConfig
{
    public required string SyncMasterBaseUrl { get; init; }
    public int RequestTimeoutSeconds { get; init; } = 30;
    public int RetryAttempts { get; init; } = 3;
    public int RetryBaseDelaySeconds { get; init; } = 2;
    public int RetryMaxDelaySeconds { get; init; } = 60;
}
```

#### `src/Tracer.Adapters.Sync/SyncMasterRestClient.cs`

Thin `HttpClient` wrapper for the sync system's Telemetry API endpoints (per `sync_addendum_telemetry.md §A4`):
- `POST /api/telemetry` — RegisterUploadIntentAsync (returns `{ intentId }`)
- `GET /api/telemetry/{nodeId}/{intervalTimestamp}` — GetIntentStatusAsync (returns status string)

Use `IHttpClientFactory` (named client "SyncMaster"). Throw `HttpRequestException` on non-success codes.

```csharp
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Tracer.Core.Abstractions;

namespace Tracer.Adapters.Sync;

public sealed class SyncMasterRestClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SyncMasterRestClient> _logger;

    public SyncMasterRestClient(IHttpClientFactory httpClientFactory, ILogger<SyncMasterRestClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> RegisterUploadIntentAsync(
        UploadIntentRequest request, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("SyncMaster");
        var response = await client.PostAsJsonAsync("/api/telemetry", request, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<UploadIntentResponse>(cancellationToken: ct);
        return result?.IntentId ?? throw new InvalidOperationException("No intentId in sync master response");
    }

    public async Task<string> GetIntentStatusAsync(string nodeId, string intervalTimestamp, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("SyncMaster");
        var response = await client.GetAsync($"/api/telemetry/{Uri.EscapeDataString(nodeId)}/{Uri.EscapeDataString(intervalTimestamp)}", ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<UploadStatusResponse>(cancellationToken: ct);
        return result?.Status ?? "Unknown";
    }
}

public sealed record UploadIntentRequest
{
    public required string NodeId { get; init; }
    public required string IntervalTimestamp { get; init; }
    public required string IntervalStartUtc { get; init; }
    public required string IntervalEndUtc { get; init; }
    public required IReadOnlyList<TelemetryFileEntry> Files { get; init; }
}

public sealed record TelemetryFileEntry
{
    public required string Name { get; init; }
    public required long SizeBytes { get; init; }
}

internal sealed record UploadIntentResponse { public string? IntentId { get; init; } }
internal sealed record UploadStatusResponse { public string? Status { get; init; } public string? ErrorMessage { get; init; } }
```

#### `src/Tracer.Adapters.Sync/SyncSystemUploadService.cs`

Implements `ITelemetryUploadService`. Key behaviors:
- `RequestUploadAsync` → calls `RegisterUploadIntentAsync` with retry on 5xx
- `GetStatusAsync` → calls `GetIntentStatusAsync` 
- `WaitForCompletionAsync` (bonus) → polls with exponential backoff until `Completed` or `Failed`
- Retry: up to `RetryAttempts`, using exponential backoff starting at `RetryBaseDelaySeconds`, capped at `RetryMaxDelaySeconds`
- After exhausting retries, log `LogWarning` and rethrow

The interface only has `RequestUploadAsync` and `GetStatusAsync`. Add `WaitForCompletionAsync` as a public helper on the class (not on the interface).

```csharp
using Microsoft.Extensions.Logging;
using Tracer.Core.Abstractions;
using Tracer.Adapters.Sync.Configuration;

namespace Tracer.Adapters.Sync;

public sealed class SyncSystemUploadService : ITelemetryUploadService
{
    private readonly SyncMasterRestClient _client;
    private readonly SyncAdapterConfig _config;
    private readonly ILogger<SyncSystemUploadService> _logger;

    // constructor

    public async Task<UploadIntentId> RequestUploadAsync(UploadRequest request, CancellationToken ct)
    {
        var intentRequest = new UploadIntentRequest
        {
            NodeId = request.NodeId.Value,
            IntervalTimestamp = request.Interval.Value,
            IntervalStartUtc = request.IntervalStartUtc.Value.ToString("O"),
            IntervalEndUtc = request.IntervalEndUtc.Value.ToString("O"),
            Files = request.Files.Select(f => new TelemetryFileEntry
            {
                Name = Path.GetFileName(f.Path),
                SizeBytes = f.SizeBytes,
            }).ToArray(),
        };

        return new UploadIntentId(await RetryAsync(
            () => _client.RegisterUploadIntentAsync(intentRequest, ct),
            "RegisterUploadIntent",
            ct));
    }

    public async Task<UploadStatus> GetStatusAsync(UploadIntentId intentId, CancellationToken ct)
    {
        // intentId.Value = "{nodeId}|{intervalTimestamp}" — encode this in RequestUploadAsync
        // Alternative: store mapping in a ConcurrentDictionary
        // Simplest: encode nodeId+interval in the intentId
        var parts = intentId.Value.Split('|', 2);
        if (parts.Length != 2) return UploadStatus.Unknown;
        var statusStr = await _client.GetIntentStatusAsync(parts[0], parts[1], ct);
        return statusStr switch
        {
            "Completed" or "Complete" => UploadStatus.Completed,
            "Failed" => UploadStatus.Failed,
            "Pending" or "InProgress" => UploadStatus.Pending,
            _ => UploadStatus.Unknown,
        };
    }

    public async Task<UploadStatus> WaitForCompletionAsync(UploadIntentId intentId, CancellationToken ct)
    {
        var delaySeconds = _config.RetryBaseDelaySeconds;
        while (!ct.IsCancellationRequested)
        {
            var status = await GetStatusAsync(intentId, ct);
            if (status is UploadStatus.Completed or UploadStatus.Failed) return status;
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
            delaySeconds = Math.Min(delaySeconds * 2, _config.RetryMaxDelaySeconds);
        }
        ct.ThrowIfCancellationRequested();
        return UploadStatus.Unknown;
    }

    private async Task<T> RetryAsync<T>(Func<Task<T>> operation, string operationName, CancellationToken ct)
    {
        var delaySeconds = _config.RetryBaseDelaySeconds;
        for (var attempt = 1; attempt <= _config.RetryAttempts; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (HttpRequestException ex) when (attempt < _config.RetryAttempts && IsTransient(ex))
            {
                _logger.LogWarning(ex, "{Op} attempt {Attempt}/{Max} failed, retrying in {Delay}s",
                    operationName, attempt, _config.RetryAttempts, delaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
                delaySeconds = Math.Min(delaySeconds * 2, _config.RetryMaxDelaySeconds);
            }
        }
        // Final attempt (exhausted)
        try
        {
            return await operation();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "{Op} exhausted {Max} retries", operationName, _config.RetryAttempts);
            throw;
        }
    }

    private static bool IsTransient(HttpRequestException ex) =>
        ex.StatusCode is null or
        System.Net.HttpStatusCode.ServiceUnavailable or
        System.Net.HttpStatusCode.GatewayTimeout or
        System.Net.HttpStatusCode.InternalServerError;
}
```

**Note**: Check if `UploadStatus` enum already exists in `Tracer.Core`. If not, add it to `Tracer.Core/Abstractions/ITelemetryUploadService.cs` or create it in this assembly.

### Tests for TRC-P11-003

Create `tests/Tracer.Tests.Unit/Adapters/Sync/SyncSystemUploadServiceTests.cs` — minimum 8 tests covering success conditions from TRC-P11-003. Pattern: use `HttpMessageHandler` mock (Moq) or a `FakeMessageHandler` to simulate HTTP responses without real HTTP calls.

Create `tests/Tracer.Tests.Unit/Adapters/Sync/SyncMasterRestClientTests.cs` — minimum 2 tests.

---

## TRC-P11-004: `Tracer.Adapters.Nas` Assembly

### Project Setup

Create `src/Tracer.Adapters.Nas/Tracer.Adapters.Nas.csproj`:

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
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Tracer.Core\Tracer.Core.csproj" />
  </ItemGroup>
</Project>
```

### Files to Create

#### `src/Tracer.Adapters.Nas/Configuration/NasAdapterConfig.cs`

```csharp
namespace Tracer.Adapters.Nas.Configuration;

public sealed class NasAdapterConfig
{
    public required string NasRoot { get; init; }
    public bool PreferLocalStaging { get; init; } = false;
    public int FileOperationTimeoutSeconds { get; init; } = 30;
    public int RetryOnTransientError { get; init; } = 3;
    public int CircuitBreakerThreshold { get; init; } = 5;
    public int CircuitBreakerResetIntervalSeconds { get; init; } = 60;
}
```

#### `src/Tracer.Adapters.Nas/SmbPathResolver.cs`

Maps logical (nodeId, intervalTimestamp) to filesystem path. Validates components to prevent path traversal.

```csharp
namespace Tracer.Adapters.Nas;

public sealed class SmbPathResolver
{
    private readonly string _nasRoot;

    public SmbPathResolver(string nasRoot)
    {
        _nasRoot = nasRoot;
    }

    public string Resolve(string nodeId, string intervalTimestamp)
    {
        ValidateComponent(nodeId, nameof(nodeId));
        ValidateComponent(intervalTimestamp, nameof(intervalTimestamp));
        return Path.Combine(_nasRoot, "telemetry", nodeId, $"{intervalTimestamp}.zip");
    }

    public string ResolveNodeDir(string nodeId)
    {
        ValidateComponent(nodeId, nameof(nodeId));
        return Path.Combine(_nasRoot, "telemetry", nodeId);
    }

    private static void ValidateComponent(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be empty", paramName);
        if (value.Contains("..") || value.Contains('/') || value.Contains('\\') || value.Contains('\0'))
            throw new ArgumentException(
                $"Path component '{value}' contains directory traversal characters", paramName);
    }
}
```

#### `src/Tracer.Adapters.Nas/NasStorageReader.cs`

Implements `ITelemetryStorageReader`. Key behaviors per TRC-P11-004:
- `ListNodesAsync`: enumerate `{NasRoot}/telemetry/*/` directories
- `ListIntervalsAsync`: enumerate `*.zip` files in node dir, check for `_ready` sentinel inside zip, skip incomplete (log warning)
- `ReadIntervalManifestAsync`: open zip, read `manifest.json`, deserialize `IntervalManifest`
- `GetIntervalZipPath`: delegate to `SmbPathResolver`
- Stage support: if `PreferLocalStaging`, copy zip to temp dir; return `StagedInterval` with `Dispose()` deleting temp

```csharp
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Adapters.Nas.Configuration;

namespace Tracer.Adapters.Nas;

public sealed class NasStorageReader : ITelemetryStorageReader
{
    private readonly NasAdapterConfig _config;
    private readonly SmbPathResolver _pathResolver;
    private readonly ILogger<NasStorageReader> _logger;

    // constructor

    public Task<IReadOnlyList<string>> ListNodesAsync(CancellationToken ct = default)
    {
        var telemetryRoot = Path.Combine(_config.NasRoot, "telemetry");
        if (!Directory.Exists(telemetryRoot))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        var nodes = Directory.GetDirectories(telemetryRoot)
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Cast<string>()
            .ToArray();
        return Task.FromResult<IReadOnlyList<string>>(nodes);
    }

    public async Task<IReadOnlyList<IntervalDescriptor>> ListIntervalsAsync(string nodeId, CancellationToken ct = default)
    {
        var nodeDir = _pathResolver.ResolveNodeDir(nodeId);
        if (!Directory.Exists(nodeDir)) return Array.Empty<IntervalDescriptor>();

        var result = new List<IntervalDescriptor>();
        foreach (var zipPath in Directory.GetFiles(nodeDir, "*.zip"))
        {
            var ts = Path.GetFileNameWithoutExtension(zipPath);
            if (!await IsIntervalComplete(zipPath)) 
            {
                _logger.LogWarning("Interval zip {Path} missing _ready sentinel, skipping", zipPath);
                continue;
            }
            result.Add(new IntervalDescriptor { NodeId = nodeId, IntervalId = ts });
        }
        return result;
    }

    private static async Task<bool> IsIntervalComplete(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            return zip.Entries.Any(e => e.FullName == "_ready");
        }
        catch { return false; }
    }

    public async Task<IntervalManifest?> ReadIntervalManifestAsync(
        string nodeId, IntervalDescriptor descriptor, CancellationToken ct = default)
    {
        var zipPath = GetIntervalZipPath(nodeId, descriptor);
        if (!File.Exists(zipPath)) return null;
        using var zip = ZipFile.OpenRead(zipPath);
        var manifestEntry = zip.Entries.FirstOrDefault(e => e.FullName == "manifest.json");
        if (manifestEntry is null) return null;
        await using var stream = manifestEntry.Open();
        return await JsonSerializer.DeserializeAsync<IntervalManifest>(stream, cancellationToken: ct);
    }

    public string GetIntervalZipPath(string nodeId, IntervalDescriptor descriptor)
        => _pathResolver.Resolve(nodeId, descriptor.IntervalId);
}
```

**IMPORTANT**: Check `Tracer.Core.Domain` for `IntervalDescriptor` and `IntervalManifest` types — they may already exist from Phase 4. Adapt the implementation to use the existing types. Look at `LocalFileSystemStorageReader.cs` as the reference.

### Tests for TRC-P11-004

Create `tests/Tracer.Tests.Unit/Adapters/Nas/NasStorageReaderTests.cs` — minimum 8 tests covering all success conditions from TRC-P11-004. Use temp directory `Path.GetTempPath()` with GUID subfolder for test isolation; create test zip files with `System.IO.Compression.ZipFile`.

Create `tests/Tracer.Tests.Unit/Adapters/Nas/SmbPathResolverTests.cs` — minimum 3 tests (valid path, directory traversal rejected, empty nodeId rejected).

---

## Step N — Add Projects to `tests` 

After creating the adapter assemblies, update the unit test project to reference them:

In `tests/Tracer.Tests.Unit/Tracer.Tests.Unit.csproj`, add:
```xml
<ProjectReference Include="..\..\src\Tracer.Adapters.DDS\Tracer.Adapters.DDS.csproj" />
<ProjectReference Include="..\..\src\Tracer.Adapters.SharedMemory\Tracer.Adapters.SharedMemory.csproj" />
<ProjectReference Include="..\..\src\Tracer.Adapters.Sync\Tracer.Adapters.Sync.csproj" />
<ProjectReference Include="..\..\src\Tracer.Adapters.Nas\Tracer.Adapters.Nas.csproj" />
```

---

## Verification

Kill stale testhost:
```powershell
Get-Process -Name "testhost" -ErrorAction SilentlyContinue | Stop-Process -Force
```

Build:
```
cd d:\Work\Tracer; dotnet build Tracer.sln -c Release --no-incremental 2>&1 | Select-Object -Last 5
```

Run new Phase 11 unit tests:
```
cd d:\Work\Tracer; dotnet test tests\Tracer.Tests.Unit -c Release --no-build --filter "FullyQualifiedName~DdsSample|FullyQualifiedName~DdsTrace|FullyQualifiedName~DdsDiagnostic|FullyQualifiedName~SharedMemory|FullyQualifiedName~SyncSystem|FullyQualifiedName~SyncMaster|FullyQualifiedName~NasStorage|FullyQualifiedName~SmbPath" 2>&1 | Select-Object -Last 8
```

Run ALL existing tests (must still pass):
```
cd d:\Work\Tracer; dotnet test tests\Tracer.Tests.Unit -c Release --no-build 2>&1 | Select-Object -Last 4
cd d:\Work\Tracer; dotnet test tests\Tracer.Tests.Integration -c Release --no-build 2>&1 | Select-Object -Last 4
```

---

## Report

Write report to: `d:\WORK\Tracer\.dev\tracer\reports\BATCH-53-REPORT.md`

Report must include:
- Files created per adapter assembly
- Test count (new + total per suite)
- Build output
- Any deviations (especially for DDS where the real API may differ from design pseudocode)
