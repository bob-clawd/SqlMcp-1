using System.Net;
using Npgsql;
using SqlMcp.Tools.Models;
namespace SqlMcp.Tools.Drivers.Dialects;

internal sealed class PostgresDatabaseDriver(string connectionString) : IDatabaseDriver
{
    public static IDatabaseDriver Create(string uri, bool ssl)
    {
        var parsedUri = new Uri(uri);
        var db = parsedUri.AbsolutePath.Trim('/');
        var userInfo = parsedUri.UserInfo.Split(':', 2);
        var user = WebUtility.UrlDecode(userInfo.ElementAtOrDefault(0) ?? "");
        var pass = WebUtility.UrlDecode(userInfo.ElementAtOrDefault(1) ?? "");

        var b = new NpgsqlConnectionStringBuilder
        {
            Host = parsedUri.Host,
            Port = parsedUri.Port > 0 ? parsedUri.Port : 5432,
            Username = user,
            Password = pass,
            Database = db,
        };

        if (ssl)
            b.SslMode = SslMode.Require;

        return new PostgresDatabaseDriver(b.ConnectionString);
    }

    private readonly NpgsqlDataSource _dataSource = NpgsqlDataSource.Create(connectionString);

    public DbDialect Dialect => DbDialect.Postgres;

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
        tableName.ValidateIdentifier();

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

    public async Task<QueryResult> QueryAsync(string sql, int limit, CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadResultAsync(limit, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ExecutionResult> ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return new ExecutionResult(affected);
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

    private static async Task<string> ReadExplainLinesAsync(NpgsqlDataReader reader, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            lines.Add(reader.GetString(0));
        return string.Join("\n", lines);
    }
}