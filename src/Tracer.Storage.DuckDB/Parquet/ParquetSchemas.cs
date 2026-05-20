using Parquet.Schema;

namespace Tracer.Storage.DuckDB.Parquet;

/// <summary>
/// Builds Parquet.Net schemas for fast-state topics.
/// </summary>
internal static class ParquetSchemas
{
    // Standard columns that appear first in every fast-state Parquet file.
    private static readonly Field[] StandardFields =
    [
        new DataField<DateTime>("publish_wallclock"),
        new DataField<DateTime>("receive_wallclock"),
        new DataField<string>("publisher_node"),
        new DataField<string>("instance_key"),
        new DataField<ulong>("sequence_number"),
    ];

    public static ParquetSchema BuildSchema(ParquetTopicSchema topicSchema)
    {
        ArgumentNullException.ThrowIfNull(topicSchema);

        var fields = new List<Field>(StandardFields);
        foreach (var col in topicSchema.Columns)
        {
            fields.Add(BuildDataField(col));
        }

        return new ParquetSchema(fields);
    }

    private static Field BuildDataField(ParquetColumn col)
    {
        // Nullable columns use the nullable variant.
        return col.Type switch
        {
            ParquetType.Int32 when col.Nullable => new DataField<int?>(col.Name),
            ParquetType.Int32 => new DataField<int>(col.Name),
            ParquetType.Int64 when col.Nullable => new DataField<long?>(col.Name),
            ParquetType.Int64 => new DataField<long>(col.Name),
            ParquetType.UInt64 when col.Nullable => new DataField<ulong?>(col.Name),
            ParquetType.UInt64 => new DataField<ulong>(col.Name),
            ParquetType.Float when col.Nullable => new DataField<float?>(col.Name),
            ParquetType.Float => new DataField<float>(col.Name),
            ParquetType.Double when col.Nullable => new DataField<double?>(col.Name),
            ParquetType.Double => new DataField<double>(col.Name),
            ParquetType.Bool when col.Nullable => new DataField<bool?>(col.Name),
            ParquetType.Bool => new DataField<bool>(col.Name),
            ParquetType.String => new DataField<string>(col.Name),
            ParquetType.TimestampNs when col.Nullable => new DataField<DateTime?>(col.Name),
            ParquetType.TimestampNs => new DataField<DateTime>(col.Name),
            _ => throw new ArgumentOutOfRangeException(nameof(col), col.Type, "Unknown ParquetType")
        };
    }
}
