using System.Text;

namespace Tracer.WebApi.Queries;

/// <summary>
/// Lightweight tokenizer-based validator that enforces read-only SQL constraints.
/// No third-party SQL parser is used — see design §3.2.
/// </summary>
public static class SqlGuardrails
{
    private static readonly HashSet<string> ForbiddenKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "UPDATE", "DELETE", "MERGE",
        "CREATE", "DROP", "ALTER", "TRUNCATE", "RENAME",
        "ATTACH", "DETACH",
        "COPY", "EXPORT", "IMPORT",
        "INSTALL", "LOAD",
        "VACUUM", "ANALYZE",
        "PRAGMA",
        "BEGIN", "COMMIT", "ROLLBACK",
        "GRANT", "REVOKE",
        "SET",
        "FORCE",
    };

    private static readonly HashSet<string> AllowedLeadingKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "WITH", "EXPLAIN", "DESCRIBE", "SHOW", "VALUES",
    };

    private static readonly HashSet<string> ForbiddenFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "read_csv_auto", "read_csv", "read_parquet", "read_json_auto", "read_json", "scan_parquet",
    };

    public static SqlGuardrailsResult Validate(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return SqlGuardrailsResult.Reject("Empty query");

        var stripped = StripComments(sql);
        var tokens = Tokenize(stripped);

        if (tokens.Count == 0)
            return SqlGuardrailsResult.Reject("Empty query");

        // First meaningful token must be an allowed leading keyword
        var first = tokens[0];
        if (first.Kind != TokenKind.Keyword || !AllowedLeadingKeywords.Contains(first.Value))
            return SqlGuardrailsResult.Reject(
                $"Statement must begin with one of {string.Join(", ", AllowedLeadingKeywords)}; saw '{first.Value}'");

        // Multi-statement check: semicolons not at the very end
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind == TokenKind.Punctuation && t.Value == ";")
            {
                // Allow a trailing semicolon only as the very last (non-whitespace) token
                if (i != tokens.Count - 1)
                    return SqlGuardrailsResult.Reject("Only single statements are allowed");
            }
        }

        // Forbidden keywords anywhere in the token stream (skipping quoted identifiers)
        foreach (var t in tokens)
        {
            if (t.Kind == TokenKind.Keyword && ForbiddenKeywords.Contains(t.Value))
                return SqlGuardrailsResult.Reject($"Forbidden keyword: {t.Value}");
        }

        // Forbidden function names
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind == TokenKind.Identifier && ForbiddenFunctions.Contains(t.Value))
                return SqlGuardrailsResult.Reject(
                    $"Function '{t.Value}' is not allowed; use the existing schema tables instead");
        }

        return SqlGuardrailsResult.Accept();
    }

    internal static string StripComments(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        int i = 0;
        while (i < sql.Length)
        {
            // Line comment
            if (i + 1 < sql.Length && sql[i] == '-' && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                continue;
            }
            // Block comment
            if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/')) i++;
                i += 2;
                continue;
            }
            // Single-quoted string: pass through as-is (do not strip)
            if (sql[i] == '\'')
            {
                sb.Append(sql[i++]);
                while (i < sql.Length)
                {
                    if (sql[i] == '\'' && i + 1 < sql.Length && sql[i + 1] == '\'')
                    {
                        sb.Append(sql[i++]);
                        sb.Append(sql[i++]);
                        continue;
                    }
                    sb.Append(sql[i]);
                    if (sql[i++] == '\'') break;
                }
                continue;
            }
            // Double-quoted identifier: pass through
            if (sql[i] == '"')
            {
                sb.Append(sql[i++]);
                while (i < sql.Length && sql[i] != '"') sb.Append(sql[i++]);
                if (i < sql.Length) sb.Append(sql[i++]);
                continue;
            }
            sb.Append(sql[i++]);
        }
        return sb.ToString();
    }

    internal static List<SqlToken> Tokenize(string sql)
    {
        var tokens = new List<SqlToken>();
        int i = 0;
        while (i < sql.Length)
        {
            char c = sql[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            // Single-quoted string literal
            if (c == '\'')
            {
                int start = i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'' && i + 1 < sql.Length && sql[i + 1] == '\'') { i += 2; continue; }
                    if (sql[i] == '\'') { i++; break; }
                    i++;
                }
                tokens.Add(new SqlToken(TokenKind.StringLiteral, sql.Substring(start, i - start)));
                continue;
            }

            // Double-quoted identifier — NOT a keyword
            if (c == '"')
            {
                int start = i++;
                while (i < sql.Length && sql[i] != '"') i++;
                if (i < sql.Length) i++;
                var inner = sql.Substring(start + 1, Math.Max(i - start - 2, 0));
                tokens.Add(new SqlToken(TokenKind.Identifier, inner));
                continue;
            }

            // Dollar-sign parameter ($name)
            if (c == '$')
            {
                int start = i++;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_')) i++;
                tokens.Add(new SqlToken(TokenKind.Identifier, sql.Substring(start, i - start)));
                continue;
            }

            // Number
            if (char.IsDigit(c))
            {
                int start = i;
                while (i < sql.Length && (char.IsDigit(sql[i]) || sql[i] == '.' || sql[i] == 'x' || sql[i] == 'X'
                       || (sql[i] >= 'a' && sql[i] <= 'f') || (sql[i] >= 'A' && sql[i] <= 'F'))) i++;
                tokens.Add(new SqlToken(TokenKind.Number, sql.Substring(start, i - start)));
                continue;
            }

            // Identifier or keyword
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_')) i++;
                var word = sql.Substring(start, i - start);
                tokens.Add(SqlKeywords.Contains(word)
                    ? new SqlToken(TokenKind.Keyword, word)
                    : new SqlToken(TokenKind.Identifier, word));
                continue;
            }

            tokens.Add(new SqlToken(TokenKind.Punctuation, c.ToString()));
            i++;
        }
        return tokens;
    }

    private static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "GROUP", "BY", "ORDER", "LIMIT", "OFFSET",
        "INNER", "OUTER", "LEFT", "RIGHT", "FULL", "JOIN", "ON", "USING",
        "AS", "AND", "OR", "NOT", "IN", "IS", "NULL", "BETWEEN", "LIKE",
        "WITH", "RECURSIVE", "UNION", "INTERSECT", "EXCEPT", "ALL", "DISTINCT",
        "CASE", "WHEN", "THEN", "ELSE", "END", "EXISTS",
        "EXPLAIN", "DESCRIBE", "SHOW", "VALUES",
        "OVER", "PARTITION", "FOLLOWING", "PRECEDING", "ROW", "ROWS", "RANGE",
        "ASC", "DESC", "INTERVAL", "DATE", "TIME", "TIMESTAMP",
        // Forbidden — classified as keywords so the checker sees them
        "INSERT", "UPDATE", "DELETE", "MERGE",
        "CREATE", "DROP", "ALTER", "TRUNCATE", "RENAME",
        "ATTACH", "DETACH", "COPY", "EXPORT", "IMPORT",
        "INSTALL", "LOAD", "VACUUM", "ANALYZE", "PRAGMA",
        "BEGIN", "COMMIT", "ROLLBACK", "GRANT", "REVOKE", "SET",
        "FORCE",
    };
}

public enum TokenKind { Keyword, Identifier, Number, StringLiteral, Punctuation }

public sealed record SqlToken(TokenKind Kind, string Value);

public sealed record SqlGuardrailsResult(bool IsValid, string? RejectionReason)
{
    public static SqlGuardrailsResult Accept() => new(true, null);
    public static SqlGuardrailsResult Reject(string reason) => new(false, reason);
}
