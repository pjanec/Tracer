using Microsoft.Extensions.Logging;
using Tracer.Core.Time;
using Tracer.Storage.Parquet;

namespace Tracer.WebApi.Queries;

public sealed record FastStateTopicSchema
{
    public required string EntityId { get; init; }
    public required string Topic { get; init; }
    public required IReadOnlyList<ParquetColumn> Columns { get; init; }
}

public sealed record EntityFastStateResult
{
    public required string EntityId { get; init; }
    public required string Topic { get; init; }
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<ParquetSample> Samples { get; init; }
    public required long TotalSamples { get; init; }
    public required bool Downsampled { get; init; }
}

public sealed class EntityFastStateService(
    ParquetReader parquet,
    FastStateFileLocator locator,
    ILogger<EntityFastStateService> logger)
{
    public IReadOnlyList<string> GetAvailableTopics(string entityId)
        => locator.GetAvailableTopicsForEntity(entityId);

    public async Task<FastStateTopicSchema?> GetSchemaAsync(
        string entityId,
        string topic,
        CancellationToken ct)
    {
        var paths = locator.LocateFiles(topic, entityId);
        if (paths.Count == 0)
        {
            logger.LogDebug("GetSchemaAsync: no files for entity {EntityId} topic {Topic}", entityId, topic);
            return null;
        }

        var schema = await parquet.InspectSchemaAsync(paths[0], ct);
        var cols = schema.Columns
            .Where(c => c.Name != "publish_wallclock" && c.Name != "instance_key")
            .ToList();

        return new FastStateTopicSchema { EntityId = entityId, Topic = topic, Columns = cols };
    }

    public async Task<EntityFastStateResult> ReadAsync(
        string entityId,
        string topic,
        IReadOnlyList<string> columns,
        WallclockTime from,
        WallclockTime to,
        int maxSamples,
        CancellationToken ct)
    {
        var paths = locator.LocateFiles(topic, entityId);
        if (paths.Count == 0)
        {
            logger.LogDebug("ReadAsync: no files for entity {EntityId} topic {Topic}", entityId, topic);
            return new EntityFastStateResult
            {
                EntityId = entityId,
                Topic = topic,
                Columns = Array.Empty<string>(),
                Samples = Array.Empty<ParquetSample>(),
                TotalSamples = 0,
                Downsampled = false,
            };
        }

        var result = await parquet.ReadTimeSeriesAsync(paths, entityId, columns, from, to, maxSamples, ct);
        return new EntityFastStateResult
        {
            EntityId = entityId,
            Topic = topic,
            Columns = columns.ToList(),
            Samples = result.Samples,
            TotalSamples = result.TotalSamples,
            Downsampled = result.Downsampled,
        };
    }
}
