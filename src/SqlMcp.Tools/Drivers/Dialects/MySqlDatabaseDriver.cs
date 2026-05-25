using System.Net;
using MySqlConnector;
using SqlMcp.Tools.Models;
namespace SqlMcp.Tools.Drivers.Dialects;

internal sealed class MySqlDatabaseDriver(string connectionString) : IDatabaseDriver
{
    public static IDatabaseDriver Create(string uri, bool ssl)
    {
        var parsedUri = new Uri(uri);
        var db = parsedUri.AbsolutePath.Trim('/');
        var userInfo = parsedUri.UserInfo.Split(':', 2);
        var user = WebUtility.UrlDecode(userInfo.ElementAtOrDefault(0) ?? "");
        var pass = WebUtility.UrlDecode(userInfo.ElementAtOrDefault(1) ?? "");

        var b = new MySqlConnectionStringBuilder
        {
            Server = parsedUri.Host,
            Port = parsedUri.Port > 0 ? (uint)parsedUri.Port : 3306,
            UserID = user,
            Password = pass,
            Database = db,
            AllowUserVariables = false,
        };

        if (ssl)
            b.SslMode = MySqlSslMode.Required;

        return new MySqlDatabaseDriver(b.ConnectionString);
    }

    public DbDialect Dialect => DbDialect.MySql;

    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        _ = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TableInfo>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SHOW FULL TABLES";

        var result = new List<TableInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            var type = reader.GetString(1);
            result.Add(new TableInfo(name, string.Equals(type, "VIEW", StringComparison.OrdinalIgnoreCase) ? DbTableType.View : DbTableType.Table));
        }
        return result;
    }

    public async Task<TableDescription> DescribeTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        tableName.GuardIdentifier();

        await using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        var columns = new List<ColumnInfo>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"DESCRIBE `{tableName}`";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var field = reader.GetString("Field");
                var type = reader.GetString("Type");
                var nullable = string.Equals(reader.GetString("Null"), "YES", StringComparison.OrdinalIgnoreCase);
                var key = reader.IsDBNull(reader.GetOrdinal("Key")) ? "" : reader.GetString("Key");
                var defaultVal = reader.IsDBNull(reader.GetOrdinal("Default")) ? null : reader.GetValue(reader.GetOrdinal("Default"))?.ToString();
                var extra = reader.IsDBNull(reader.GetOrdinal("Extra")) ? null : reader.GetString("Extra").ToUpperInvariant();

                columns.Add(new ColumnInfo(
                    Name: field,
                    DataType: type,
                    Nullable: nullable,
                    DefaultValue: defaultVal,
                    IsPrimaryKey: string.Equals(key, "PRI", StringComparison.OrdinalIgnoreCase),
                    IsUnique: string.Equals(key, "UNI", StringComparison.OrdinalIgnoreCase),
                    Extra: extra));
            }
        }

        var indexMap = new Dictionary<string, (bool unique, SortedDictionary<int, string> cols)>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SHOW INDEX FROM `{tableName}`";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var keyName = reader.GetString("Key_name");
                var nonUnique = reader.GetInt32("Non_unique") != 0;
                var seq = reader.GetInt32("Seq_in_index");
                var col = reader.GetString("Column_name");

                if (!indexMap.TryGetValue(keyName, out var existing))
                    existing = (!nonUnique, new SortedDictionary<int, string>());

                existing.cols[seq] = col;
                indexMap[keyName] = existing;
            }
        }

        var indexes = indexMap
            .Select(kvp => new IndexInfo(kvp.Key, kvp.Value.unique, kvp.Value.cols.OrderBy(p => p.Key).Select(p => p.Value).ToArray()))
            .ToArray();

        var foreignKeys = new List<ForeignKeyInfo>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT COLUMN_NAME, REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME, CONSTRAINT_NAME
FROM information_schema.KEY_COLUMN_USAGE
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND REFERENCED_TABLE_NAME IS NOT NULL
ORDER BY CONSTRAINT_NAME, ORDINAL_POSITION";
            cmd.Parameters.AddWithValue("@table", tableName);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                foreignKeys.Add(new ForeignKeyInfo(
                    ConstraintName: reader.GetString("CONSTRAINT_NAME"),
                    Column: reader.GetString("COLUMN_NAME"),
                    ReferencedTable: reader.GetString("REFERENCED_TABLE_NAME"),
                    ReferencedColumn: reader.GetString("REFERENCED_COLUMN_NAME")));
            }
        }

        return new TableDescription(tableName, columns, indexes, foreignKeys);
    }

    public async Task<QueryResult> QueryAsync(string sql, int limit, CancellationToken cancellationToken = default)
    {
        await using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadResultAsync(limit, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ExecutionResult> ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        await using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        string? insertId = null;
        try
        {
            await using var idCmd = conn.CreateCommand();
            idCmd.CommandText = "SELECT LAST_INSERT_ID()";
            var id = await idCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (id is not null && long.TryParse(id.ToString(), out var parsed) && parsed > 0)
                insertId = parsed.ToString();
        }
        catch
        {
            // best-effort only
        }
        return new ExecutionResult(affected, insertId);
    }

    public async Task<AnalyzeResult> AnalyzeQueryAsync(string sql, bool execute, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var stripped = sql.Trim().TrimEnd(';');

        await using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = (int)Math.Ceiling(timeout.TotalSeconds);

        string explainSql;
        if (execute)
        {
            // MySQL: try EXPLAIN ANALYZE, fall back to plan-only if unsupported.
            explainSql = $"EXPLAIN ANALYZE {stripped}";
        }
        else
        {
            explainSql = $"EXPLAIN FORMAT=TRADITIONAL {stripped}";
        }

        cmd.CommandText = explainSql;

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var raw = await ReadExplainRawAsync(reader, cancellationToken).ConfigureAwait(false);
            return new AnalyzeResult(raw, execute);
        }
        catch (MySqlException ex) when (execute && ex.Message.Contains("EXPLAIN ANALYZE", StringComparison.OrdinalIgnoreCase))
        {
            cmd.CommandText = $"EXPLAIN FORMAT=TRADITIONAL {stripped}";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var raw = await ReadExplainRawAsync(reader, cancellationToken).ConfigureAwait(false);
            raw += "\n\n(Note: EXPLAIN ANALYZE requires MySQL 8.0.18+. Showed plan-only output.)";
            return new AnalyzeResult(raw, false);
        }
        catch (MySqlException ex) when (ex.Number == 3024 /* ER_QUERY_TIMEOUT */)
        {
            return new AnalyzeResult(string.Empty, execute, TimedOut: true);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task<string> ReadExplainRawAsync(MySqlDataReader reader, CancellationToken cancellationToken)
    {
        // plan-only is tabular; EXPLAIN ANALYZE returns a single column with tree text.
        if (reader.FieldCount == 1)
        {
            var lines = new List<string>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                lines.Add(reader.GetValue(0)?.ToString() ?? string.Empty);
            return string.Join("\n", lines);
        }

        var cols = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join("\t", cols));
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var vals = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
                vals[i] = reader.GetValue(i)?.ToString() ?? string.Empty;
            sb.AppendLine(string.Join("\t", vals));
        }
        return sb.ToString();
    }

}