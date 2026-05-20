using System.Text.Json;
using Tracer.Core.Records;
using Tracer.Core.Time;

namespace Tracer.Storage.DuckDB.Parquet;

/// <summary>
/// Extracts typed column values from a <see cref="StateSampleRecord"/> payload
/// according to a <see cref="ParquetTopicSchema"/>.
/// </summary>
internal static class ColumnExtractor
{
    /// <summary>
    /// Extracts a row of values from <paramref name="record"/> using <paramref name="schema"/>.
    /// Returns an array with standard columns first, then schema columns in order.
    /// </summary>
    public static object?[] ExtractRow(StateSampleRecord record, ParquetTopicSchema schema)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(schema);

        // Standard columns: publish_wallclock, receive_wallclock, publisher_node,
        //                   instance_key, sequence_number
        var standardCount = 5;
        var row = new object?[standardCount + schema.Columns.Count];

        row[0] = record.PublishWallclock.ToDateTimeOffset().UtcDateTime;
        row[1] = record.ReceiveWallclock.ToDateTimeOffset().UtcDateTime;
        row[2] = record.PublisherNode.Value;
        row[3] = record.InstanceKey;
        row[4] = record.SequenceNumber;

        // Parse payload JSON once; treat parse failure as missing path for all columns.
        JsonDocument? doc = null;
        if (!string.IsNullOrWhiteSpace(record.PayloadJson))
        {
            try { doc = JsonDocument.Parse(record.PayloadJson); }
            catch (JsonException) { /* leave doc null; all payload columns get zero/null */ }
        }

        for (var i = 0; i < schema.Columns.Count; i++)
        {
            var col = schema.Columns[i];
            row[standardCount + i] = ExtractValue(doc, col);
        }

        doc?.Dispose();
        return row;
    }

    private static object? ExtractValue(JsonDocument? doc, ParquetColumn col)
    {
        if (doc is null)
            return DefaultValue(col);

        var element = NavigatePath(doc.RootElement, col.JsonPath);
        if (!element.HasValue)
            return DefaultValue(col);

        var el = element.Value;
        try
        {
            return col.Type switch
            {
                ParquetType.Int32 => el.ValueKind == JsonValueKind.Number ? el.GetInt32() : (col.Nullable ? null : (object?)0),
                ParquetType.Int64 => el.ValueKind == JsonValueKind.Number ? el.GetInt64() : (col.Nullable ? null : (object?)0L),
                ParquetType.UInt64 => el.ValueKind == JsonValueKind.Number ? el.GetUInt64() : (col.Nullable ? null : (object?)0UL),
                ParquetType.Float => el.ValueKind == JsonValueKind.Number ? el.GetSingle() : (col.Nullable ? null : (object?)0f),
                ParquetType.Double => el.ValueKind == JsonValueKind.Number ? el.GetDouble() : (col.Nullable ? null : (object?)0d),
                ParquetType.Bool => el.ValueKind == JsonValueKind.True ? true :
                                    el.ValueKind == JsonValueKind.False ? false :
                                    (col.Nullable ? null : (object?)false),
                ParquetType.String => el.ValueKind == JsonValueKind.String ? el.GetString() : (col.Nullable ? null : (object?)string.Empty),
                ParquetType.TimestampNs => el.ValueKind == JsonValueKind.Number
                    ? WallclockTime.FromUnixNanoseconds(el.GetInt64()).ToDateTimeOffset().UtcDateTime
                    : (col.Nullable ? null : (object?)DateTime.UnixEpoch),
                _ => null
            };
        }
        catch
        {
            return DefaultValue(col);
        }
    }

    private static object? DefaultValue(ParquetColumn col)
    {
        if (col.Nullable)
            return null;

        return col.Type switch
        {
            ParquetType.Int32 => (object?)0,
            ParquetType.Int64 => 0L,
            ParquetType.UInt64 => 0UL,
            ParquetType.Float => 0f,
            ParquetType.Double => 0d,
            ParquetType.Bool => false,
            ParquetType.String => string.Empty,
            ParquetType.TimestampNs => DateTime.UnixEpoch,
            _ => null
        };
    }

    /// <summary>
    /// Navigates a simple JSON path (e.g. "$.position.x") through a <see cref="JsonElement"/>.
    /// Supports only property access separated by dots; does not support array indexing.
    /// </summary>
    private static JsonElement? NavigatePath(JsonElement root, string jsonPath)
    {
        if (string.IsNullOrEmpty(jsonPath))
            return null;

        // Strip leading "$."
        var path = jsonPath.StartsWith("$.", StringComparison.Ordinal)
            ? jsonPath[2..]
            : jsonPath.TrimStart('$').TrimStart('.');

        if (path.Length == 0)
            return root;

        var parts = path.Split('.');
        var current = root;
        foreach (var part in parts)
        {
            if (current.ValueKind != JsonValueKind.Object)
                return null;
            if (!current.TryGetProperty(part, out var next))
                return null;
            current = next;
        }
        return current;
    }
}
