using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Tracer.Core.Identity;

namespace Tracer.Adapters.DDS;

/// <summary>
/// Extracts distributed trace context from a DDS sample payload.
/// Uses compiled expression-based accessors cached per sample type.
/// </summary>
public sealed class DdsTraceContextExtractor
{
    private readonly ConcurrentDictionary<Type, TraceContextAccessors> _cache = new();

    /// <summary>
    /// Returns <see cref="TraceContext.Empty"/> for non-Event topics.
    /// For Event topics, compiles and caches accessors on first call per sample type.
    /// </summary>
    public TraceContext Extract(IDdsSample sample, DdsTopicMetadata meta)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(meta);

        if (meta.Kind != DdsTopicKind.Event)
            return TraceContext.Empty;

        var accessors = _cache.GetOrAdd(meta.SampleType, BuildAccessors);
        var payload = sample.GetPayload();

        return new TraceContext
        {
            TraceId = accessors.TraceIdAccessor(payload),
            EventId = new EventId(accessors.EventIdAccessor(payload)),
            ParentEventId = new EventId(accessors.ParentEventIdAccessor(payload)),
        };
    }

    private static TraceContextAccessors BuildAccessors(Type sampleType)
    {
        return new TraceContextAccessors
        {
            TraceIdAccessor = BuildUlongAccessor(sampleType, "traceId", "TraceId"),
            EventIdAccessor = BuildUlongAccessor(sampleType, "eventId", "EventId"),
            ParentEventIdAccessor = BuildUlongAccessor(sampleType, "parentEventId", "ParentEventId"),
        };
    }

    private static Func<object, ulong> BuildUlongAccessor(Type t, string camel, string pascal)
    {
        var prop = t.GetProperty(camel, BindingFlags.Public | BindingFlags.Instance)
                ?? t.GetProperty(pascal, BindingFlags.Public | BindingFlags.Instance)
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
        public required Func<object, ulong> TraceIdAccessor { get; init; }
        public required Func<object, ulong> EventIdAccessor { get; init; }
        public required Func<object, ulong> ParentEventIdAccessor { get; init; }
    }
}
