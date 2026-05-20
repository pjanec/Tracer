namespace Tracer.Storage.DuckDB.Parquet;

/// <summary>
/// Well-known topic schemas shipped with the agent.
/// </summary>
public static class WellKnownTopicSchemas
{
    public static readonly ParquetTopicSchema Transforms = new()
    {
        TopicName = "topic.transforms",
        Columns = new[]
        {
            new ParquetColumn { Name = "pos_x",   Type = ParquetType.Float, JsonPath = "$.position.x" },
            new ParquetColumn { Name = "pos_y",   Type = ParquetType.Float, JsonPath = "$.position.y" },
            new ParquetColumn { Name = "pos_z",   Type = ParquetType.Float, JsonPath = "$.position.z" },
            new ParquetColumn { Name = "quat_w",  Type = ParquetType.Float, JsonPath = "$.orientation.w" },
            new ParquetColumn { Name = "quat_x",  Type = ParquetType.Float, JsonPath = "$.orientation.x" },
            new ParquetColumn { Name = "quat_y",  Type = ParquetType.Float, JsonPath = "$.orientation.y" },
            new ParquetColumn { Name = "quat_z",  Type = ParquetType.Float, JsonPath = "$.orientation.z" },
        }
    };

    public static IReadOnlyDictionary<string, ParquetTopicSchema> ToDictionary()
    {
        return new Dictionary<string, ParquetTopicSchema>
        {
            [Transforms.TopicName] = Transforms
        };
    }
}
