using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DuckDB.NET.Data;

namespace Tracer.Storage.DuckDB.MultiInterval;

/// <summary>
/// Attaches and detaches read-only DuckDB files to a single DuckDB connection.
/// </summary>
public sealed class AttachedDatabaseManager : IAsyncDisposable
{
    private readonly DuckDBConnection _connection;
    private readonly Dictionary<string, string> _attachments = new(StringComparer.Ordinal);

    public AttachedDatabaseManager(DuckDBConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    /// <summary>Live attachments: alias → file path.</summary>
    public IReadOnlyDictionary<string, string> Attachments => _attachments;

    /// <summary>
    /// Attaches <paramref name="file"/> to the connection as a read-only database.
    /// Returns the generated SQL alias.
    /// </summary>
    public async Task<string> AttachAsync(IntervalDbFile file, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        var alias = GenerateAlias(file.AliasHint);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"ATTACH '{EscapePath(file.FilePath)}' AS {alias} (READ_ONLY)";
        await cmd.ExecuteNonQueryAsync(ct);

        _attachments[alias] = file.FilePath;
        return alias;
    }

    /// <summary>Detaches the database with the given alias and removes it from <see cref="Attachments"/>.</summary>
    public async Task DetachAsync(string alias, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(alias);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"DETACH {alias}";
        await cmd.ExecuteNonQueryAsync(ct);

        _attachments.Remove(alias);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var alias in _attachments.Keys.ToArray())
        {
            try
            {
                await DetachAsync(alias);
            }
            catch
            {
                // best-effort: swallow individual errors during disposal
            }
        }
    }

    /// <summary>
    /// Generates a unique, SQL-safe alias: <c>db_{normalized_hint}_{6hex}</c>.
    /// The normalized hint has all non-[a-z0-9] characters replaced with '_'.
    /// </summary>
    private static string GenerateAlias(string hint)
    {
        var normalized = Regex.Replace(hint.ToLowerInvariant(), @"[^a-z0-9]", "_");
        if (string.IsNullOrEmpty(normalized))
            normalized = "interval";

        // 6-char random hex suffix for uniqueness
        var randomBytes = RandomNumberGenerator.GetBytes(3);
        var suffix = Convert.ToHexString(randomBytes).ToLowerInvariant();

        return $"db_{normalized}_{suffix}";
    }

    private static string EscapePath(string path) =>
        path.Replace("'", "''");
}
