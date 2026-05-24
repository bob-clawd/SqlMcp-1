using System.Text.RegularExpressions;
using MySqlConnector;
using SqlMcp.Tools.Models;
using SqlMcp.Tools.Security;

namespace SqlMcp.Tools.Drivers.Dialects;

internal sealed class MySqlDatabaseDriver(string connectionString) : IDatabaseDriver
{
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
        GuardIdentifier(tableName);

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

    public async Task<IReadOnlyList<TableDescription>> GetSchemaAsync(CancellationToken cancellationToken = default)
    {
        // Read columns and fks via information_schema; indexes are omitted for speed.
        await using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        var tableMap = new Dictionary<string, (List<ColumnInfo> cols, List<ForeignKeyInfo> fks)>(StringComparer.OrdinalIgnoreCase);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT TABLE_NAME, COLUMN_NAME, ORDINAL_POSITION, COLUMN_DEFAULT,
IS_NULLABLE, COLUMN_TYPE, COLUMN_KEY, EXTRA
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
ORDER BY TABLE_NAME, ORDINAL_POSITION";

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var table = reader.GetString("TABLE_NAME");
                var col = new ColumnInfo(
                    Name: reader.GetString("COLUMN_NAME"),
                    DataType: reader.GetString("COLUMN_TYPE"),
                    Nullable: string.Equals(reader.GetString("IS_NULLABLE"), "YES", StringComparison.OrdinalIgnoreCase),
                    DefaultValue: reader.IsDBNull(reader.GetOrdinal("COLUMN_DEFAULT")) ? null : reader.GetValue(reader.GetOrdinal("COLUMN_DEFAULT"))?.ToString(),
                    IsPrimaryKey: string.Equals(reader.GetString("COLUMN_KEY"), "PRI", StringComparison.OrdinalIgnoreCase),
                    IsUnique: string.Equals(reader.GetString("COLUMN_KEY"), "UNI", StringComparison.OrdinalIgnoreCase),
                    Extra: reader.IsDBNull(reader.GetOrdinal("EXTRA")) ? null : reader.GetString("EXTRA").ToUpperInvariant());

                if (!tableMap.TryGetValue(table, out var entry))
                    entry = (new List<ColumnInfo>(), new List<ForeignKeyInfo>());

                entry.cols.Add(col);
                tableMap[table] = entry;
            }
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT TABLE_NAME, COLUMN_NAME, REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME, CONSTRAINT_NAME
FROM information_schema.KEY_COLUMN_USAGE
WHERE TABLE_SCHEMA = DATABASE() AND REFERENCED_TABLE_NAME IS NOT NULL";

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var table = reader.GetString("TABLE_NAME");
                if (!tableMap.TryGetValue(table, out var entry))
                    entry = (new List<ColumnInfo>(), new List<ForeignKeyInfo>());

                entry.fks.Add(new ForeignKeyInfo(
                    ConstraintName: reader.GetString("CONSTRAINT_NAME"),
                    Column: reader.GetString("COLUMN_NAME"),
                    ReferencedTable: reader.GetString("REFERENCED_TABLE_NAME"),
                    ReferencedColumn: reader.GetString("REFERENCED_COLUMN_NAME")));

                tableMap[table] = entry;
            }
        }

        return tableMap.Select(kvp => new TableDescription(kvp.Key, kvp.Value.cols, Array.Empty<IndexInfo>(), kvp.Value.fks)).ToArray();
    }

    public async Task<QueryResult> GetSampleDataAsync(string tableName, int limit, string? orderByColumn, bool orderDescending,
        CancellationToken cancellationToken = default)
    {
        GuardIdentifier(tableName);
        if (orderByColumn is not null) GuardIdentifier(orderByColumn);

        var safeLimit = Math.Clamp(limit, 1, 100);
        var orderClause = orderByColumn is null ? string.Empty : $" ORDER BY `{orderByColumn}` {(orderDescending ? "DESC" : "ASC")}";

        await using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM `{tableName}`{orderClause} LIMIT {safeLimit}";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await ReadResultAsync(reader, cancellationToken).ConfigureAwait(false);
    }

    public async Task<QueryResult> ExecuteQueryAsync(string sql, bool isReadOnly, int maxRows, CancellationToken cancellationToken = default)
    {
        await using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = isReadOnly ? ApplyLimitIfMissing(sql, maxRows) : sql;

        if (isReadOnly)
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await ReadResultAsync(reader, cancellationToken).ConfigureAwait(false);
        }
        else
        {
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
            return new QueryResult([], [], affected, insertId);
        }
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

    private static void GuardIdentifier(string name)
    {
        if (!SqlIdentifier.IsValid(name))
            throw new ArgumentException($"Invalid identifier '{name}'.", nameof(name));
    }

    private static async Task<QueryResult> ReadResultAsync(MySqlDataReader reader, CancellationToken cancellationToken)
    {
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var rows = new List<IReadOnlyDictionary<string, object?>>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var val = await reader.IsDBNullAsync(i, cancellationToken).ConfigureAwait(false) ? null : reader.GetValue(i);
                row[columns[i]] = val;
            }
            rows.Add(row);
        }

        return new QueryResult(columns, rows);
    }

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

    private static string ApplyLimitIfMissing(string sql, int maxRows)
    {
        if (s_limitRegex.IsMatch(sql))
            return sql;

        var trimmed = sql.TrimEnd();
        if (trimmed.EndsWith(';'))
            trimmed = trimmed[..^1];

        return trimmed + $" LIMIT {maxRows}";
    }

    private static readonly Regex s_limitRegex = new(@"\bLIMIT\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
}