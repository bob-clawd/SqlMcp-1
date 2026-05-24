using System.Text.RegularExpressions;
using Npgsql;
using SqlMcp.Tools.Models;
using SqlMcp.Tools.Security;

namespace SqlMcp.Tools.Drivers.Dialects;

internal sealed class PostgresDatabaseDriver(string connectionString) : IDatabaseDriver
{
    private readonly NpgsqlDataSource _dataSource = NpgsqlDataSource.Create(connectionString);

    public DbDialect Dialect => DbDialect.Postgres;

    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        _ = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TableInfo>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT table_name, table_type
FROM information_schema.tables
WHERE table_schema = current_schema()
  AND table_type IN ('BASE TABLE', 'VIEW')
ORDER BY table_name";

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

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var columns = new List<ColumnInfo>();
        var indexes = new Dictionary<string, (bool unique, List<(int seq, string col)> cols)>(StringComparer.OrdinalIgnoreCase);
        var foreignKeys = new List<ForeignKeyInfo>();

        // columns
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT column_name, data_type, udt_name, character_maximum_length,
       is_nullable, column_default, ordinal_position
FROM information_schema.columns
WHERE table_schema = current_schema() AND table_name = @t
ORDER BY ordinal_position";
            cmd.Parameters.AddWithValue("t", tableName);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var colName = reader.GetString(0);
                var dataType = reader.IsDBNull(3)
                    ? (reader.GetString(2) ?? reader.GetString(1))
                    : $"{reader.GetString(1)}({reader.GetInt32(3)})";

                var nullable = string.Equals(reader.GetString(4), "YES", StringComparison.OrdinalIgnoreCase);
                var def = reader.IsDBNull(5) ? null : reader.GetValue(5)?.ToString();

                columns.Add(new ColumnInfo(
                    Name: colName,
                    DataType: dataType,
                    Nullable: nullable,
                    DefaultValue: def,
                    IsPrimaryKey: false,
                    IsUnique: false,
                    Extra: def != null && def.Contains("nextval", StringComparison.OrdinalIgnoreCase) ? "AUTO_INCREMENT" : null));
            }
        }

        // primary keys
        var pkCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT a.attname
FROM pg_index i
JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
JOIN pg_class c ON c.oid = i.indrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = current_schema() AND c.relname = @t AND i.indisprimary";
            cmd.Parameters.AddWithValue("t", tableName);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                pkCols.Add(reader.GetString(0));
        }

        columns = columns.Select(c => c with { IsPrimaryKey = pkCols.Contains(c.Name) }).ToList();

        // indexes
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT i.relname AS index_name, ix.indisunique AS is_unique,
       a.attname AS column_name, k.ordinal AS seq
FROM pg_index ix
JOIN pg_class i ON i.oid = ix.indexrelid
JOIN pg_class t ON t.oid = ix.indrelid
JOIN pg_namespace n ON n.oid = t.relnamespace
JOIN LATERAL unnest(ix.indkey) WITH ORDINALITY AS k(attnum, ordinal) ON true
JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
WHERE n.nspname = current_schema() AND t.relname = @t
ORDER BY i.relname, k.ordinal";
            cmd.Parameters.AddWithValue("t", tableName);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var idxName = reader.GetString(0);
                var unique = reader.GetBoolean(1);
                var colName = reader.GetString(2);
                var seq = reader.GetInt32(3);

                if (!indexes.TryGetValue(idxName, out var entry))
                    entry = (unique, new List<(int seq, string col)>());

                entry.cols.Add((seq, colName));
                indexes[idxName] = entry;
            }
        }

        // unique columns via single-column unique indexes
        var uniqueSingleCols = indexes
            .Where(i => i.Value.unique && i.Value.cols.Count == 1)
            .Select(i => i.Value.cols[0].col)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        columns = columns.Select(c => c with { IsUnique = !c.IsPrimaryKey && uniqueSingleCols.Contains(c.Name) }).ToList();

        // foreign keys
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT kcu.constraint_name, kcu.column_name,
       ccu.table_name AS referenced_table, ccu.column_name AS referenced_column
FROM information_schema.key_column_usage kcu
JOIN information_schema.referential_constraints rc
  ON rc.constraint_name = kcu.constraint_name AND rc.constraint_schema = kcu.constraint_schema
JOIN information_schema.constraint_column_usage ccu
  ON ccu.constraint_name = rc.unique_constraint_name AND ccu.constraint_schema = rc.unique_constraint_schema
WHERE kcu.table_schema = current_schema() AND kcu.table_name = @t
ORDER BY kcu.constraint_name, kcu.ordinal_position";
            cmd.Parameters.AddWithValue("t", tableName);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                foreignKeys.Add(new ForeignKeyInfo(
                    ConstraintName: reader.GetString(0),
                    Column: reader.GetString(1),
                    ReferencedTable: reader.GetString(2),
                    ReferencedColumn: reader.GetString(3)));
            }
        }

        var indexList = indexes.Select(kvp =>
                new IndexInfo(kvp.Key, kvp.Value.unique, kvp.Value.cols.OrderBy(x => x.seq).Select(x => x.col).ToArray()))
            .ToArray();

        return new TableDescription(tableName, columns, indexList, foreignKeys);
    }

    public async Task<IReadOnlyList<TableDescription>> GetSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var tableMap = new Dictionary<string, (List<ColumnInfo> cols, List<ForeignKeyInfo> fks)>(StringComparer.OrdinalIgnoreCase);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT table_name, column_name, data_type, udt_name, character_maximum_length,
       is_nullable, column_default, ordinal_position
FROM information_schema.columns
WHERE table_schema = current_schema()
ORDER BY table_name, ordinal_position";

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var table = reader.GetString(0);
                var colName = reader.GetString(1);
                var dataType = reader.IsDBNull(4)
                    ? (reader.GetString(3) ?? reader.GetString(2))
                    : $"{reader.GetString(2)}({reader.GetInt32(4)})";

                var nullable = string.Equals(reader.GetString(5), "YES", StringComparison.OrdinalIgnoreCase);
                var def = reader.IsDBNull(6) ? null : reader.GetValue(6)?.ToString();

                if (!tableMap.TryGetValue(table, out var entry))
                    entry = (new List<ColumnInfo>(), new List<ForeignKeyInfo>());

                entry.cols.Add(new ColumnInfo(
                    Name: colName,
                    DataType: dataType,
                    Nullable: nullable,
                    DefaultValue: def,
                    IsPrimaryKey: false,
                    IsUnique: false,
                    Extra: def != null && def.Contains("nextval", StringComparison.OrdinalIgnoreCase) ? "AUTO_INCREMENT" : null));

                tableMap[table] = entry;
            }
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT kcu.table_name, kcu.column_name, kcu.constraint_name,
       ccu.table_name AS referenced_table, ccu.column_name AS referenced_column
FROM information_schema.key_column_usage kcu
JOIN information_schema.referential_constraints rc
  ON rc.constraint_name = kcu.constraint_name AND rc.constraint_schema = kcu.constraint_schema
JOIN information_schema.constraint_column_usage ccu
  ON ccu.constraint_name = rc.unique_constraint_name AND ccu.constraint_schema = rc.unique_constraint_schema
WHERE kcu.table_schema = current_schema()";

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var table = reader.GetString(0);
                if (!tableMap.TryGetValue(table, out var entry))
                    entry = (new List<ColumnInfo>(), new List<ForeignKeyInfo>());

                entry.fks.Add(new ForeignKeyInfo(
                    ConstraintName: reader.GetString(2),
                    Column: reader.GetString(1),
                    ReferencedTable: reader.GetString(3),
                    ReferencedColumn: reader.GetString(4)));

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
        var orderClause = orderByColumn is null ? string.Empty : $" ORDER BY \"{orderByColumn}\" {(orderDescending ? "DESC" : "ASC")}";

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM \"{tableName}\"{orderClause} LIMIT @l";
        cmd.Parameters.AddWithValue("l", safeLimit);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await ReadResultAsync(reader, cancellationToken).ConfigureAwait(false);
    }

    public async Task<QueryResult> ExecuteQueryAsync(string sql, bool isReadOnly, int maxRows, CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
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
            return new QueryResult(Array.Empty<string>(), Array.Empty<IReadOnlyDictionary<string, object?>>(), affected, null);
        }
    }

    public async Task<AnalyzeResult> AnalyzeQueryAsync(string sql, bool execute, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var stripped = sql.Trim().TrimEnd(';');

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        if (!execute)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"EXPLAIN {stripped}";
            cmd.CommandTimeout = (int)Math.Ceiling(timeout.TotalSeconds);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var raw = await ReadExplainLinesAsync(reader, cancellationToken).ConfigureAwait(false);
            return new AnalyzeResult(raw, false);
        }

        // execute=true: run inside transaction with statement_timeout.
        await using var tx = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using (var set = conn.CreateCommand())
            {
                set.Transaction = tx;
                set.CommandText = $"SET LOCAL statement_timeout = {(int)timeout.TotalMilliseconds}";
                await set.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"EXPLAIN (ANALYZE, BUFFERS, FORMAT TEXT) {stripped}";
                cmd.CommandTimeout = (int)Math.Ceiling(timeout.TotalSeconds);

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                var raw = await ReadExplainLinesAsync(reader, cancellationToken).ConfigureAwait(false);
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new AnalyzeResult(raw, true);
            }
        }
        catch (PostgresException ex) when (ex.SqlState == "57014")
        {
            try { await tx.RollbackAsync(cancellationToken).ConfigureAwait(false); } catch { }
            return new AnalyzeResult(string.Empty, true, TimedOut: true);
        }
        catch
        {
            try { await tx.RollbackAsync(cancellationToken).ConfigureAwait(false); } catch { }
            throw;
        }
    }

    public async ValueTask DisposeAsync() => await _dataSource.DisposeAsync().ConfigureAwait(false);

    private static void GuardIdentifier(string name)
    {
        if (!SqlIdentifier.IsValid(name))
            throw new ArgumentException($"Invalid identifier '{name}'.", nameof(name));
    }

    private static async Task<QueryResult> ReadResultAsync(NpgsqlDataReader reader, CancellationToken cancellationToken)
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

    private static async Task<string> ReadExplainLinesAsync(NpgsqlDataReader reader, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            lines.Add(reader.GetString(0));
        return string.Join("\n", lines);
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