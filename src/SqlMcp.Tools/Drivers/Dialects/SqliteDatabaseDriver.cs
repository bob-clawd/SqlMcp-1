using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using SqlMcp.Tools.Models;
using SqlMcp.Tools.Security;

namespace SqlMcp.Tools.Drivers.Dialects;

internal sealed class SqliteDatabaseDriver : IDatabaseDriver
{
    private readonly SqliteConnection _connection;

    public SqliteDatabaseDriver(string filePath)
    {
        // Allow a plain path. SQLite also supports "Data Source=:memory:" etc.
        var cs = filePath.Contains('=')
            ? filePath
            : new SqliteConnectionStringBuilder { DataSource = filePath, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared }.ToString();

        _connection = new SqliteConnection(cs);
    }

    public DbDialect Dialect => DbDialect.Sqlite;

    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT 1";
        _ = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TableInfo>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureOpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"SELECT name, type FROM sqlite_master
WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%'
ORDER BY name";

        var result = new List<TableInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            var type = reader.GetString(1);
            result.Add(new TableInfo(name, string.Equals(type, "view", StringComparison.OrdinalIgnoreCase) ? DbTableType.View : DbTableType.Table));
        }
        return result;
    }

    public async Task<TableDescription> DescribeTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        GuardIdentifier(tableName);
        await EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        var columns = new List<ColumnInfo>();
        await using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA table_info(\"{tableName}\")";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var name = reader.GetString(reader.GetOrdinal("name"));
                var type = reader.IsDBNull(reader.GetOrdinal("type")) ? "TEXT" : reader.GetString(reader.GetOrdinal("type"));
                var notNull = reader.GetInt32(reader.GetOrdinal("notnull")) != 0;
                var def = reader.IsDBNull(reader.GetOrdinal("dflt_value")) ? null : reader.GetValue(reader.GetOrdinal("dflt_value"))?.ToString();
                var pk = reader.GetInt32(reader.GetOrdinal("pk")) != 0;

                columns.Add(new ColumnInfo(name, type, Nullable: !notNull, DefaultValue: def, IsPrimaryKey: pk, IsUnique: false, Extra: null));
            }
        }

        if (columns.Count == 0)
            throw new ArgumentException($"Table '{tableName}' does not exist.", nameof(tableName));

        var indexes = new List<IndexInfo>();
        var uniqueCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA index_list(\"{tableName}\")";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var idxName = reader.GetString(reader.GetOrdinal("name"));
                var unique = reader.GetInt32(reader.GetOrdinal("unique")) == 1;

                var cols = await ReadIndexColumnsAsync(idxName, cancellationToken).ConfigureAwait(false);
                indexes.Add(new IndexInfo(idxName, unique, cols));

                if (unique && cols.Count == 1)
                    uniqueCols.Add(cols[0]);
            }
        }

        columns = columns.Select(c => c with { IsUnique = !c.IsPrimaryKey && uniqueCols.Contains(c.Name) }).ToList();

        var foreignKeys = new List<ForeignKeyInfo>();
        await using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA foreign_key_list(\"{tableName}\")";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = reader.GetInt32(reader.GetOrdinal("id"));
                foreignKeys.Add(new ForeignKeyInfo(
                    ConstraintName: $"fk_{tableName}_{id}",
                    Column: reader.GetString(reader.GetOrdinal("from")),
                    ReferencedTable: reader.GetString(reader.GetOrdinal("table")),
                    ReferencedColumn: reader.GetString(reader.GetOrdinal("to"))));
            }
        }

        return new TableDescription(tableName, columns, indexes, foreignKeys);
    }

    public async Task<QueryResult> ExecuteQueryAsync(string sql, bool isReadOnly, int maxRows, CancellationToken cancellationToken = default)
    {
        await EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = isReadOnly ? ApplyLimitIfMissing(sql, maxRows) : sql;

        if (isReadOnly)
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await ReadResultAsync(reader, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            // SQLite insert id is connection-scoped; expose as string for uniformity.
            string? insertId = null;
            try
            {
                await using var idCmd = _connection.CreateCommand();
                idCmd.CommandText = "SELECT last_insert_rowid();";
                var id = await idCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (id is not null && long.TryParse(id.ToString(), out var parsed) && parsed > 0)
                    insertId = parsed.ToString();
            }
            catch
            {
                // best-effort only
            }
            return new QueryResult(Array.Empty<string>(), Array.Empty<IReadOnlyDictionary<string, object?>>(), affected, insertId);
        }
    }

    public async Task<AnalyzeResult> AnalyzeQueryAsync(string sql, bool execute, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // SQLite does not have a safe built-in "EXPLAIN ANALYZE" equivalent; use EXPLAIN QUERY PLAN.
        var stripped = sql.Trim().TrimEnd(';');

        await EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"EXPLAIN QUERY PLAN {stripped}";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(cts.Token).ConfigureAwait(false);
            var lines = new List<string>();
            while (await reader.ReadAsync(cts.Token).ConfigureAwait(false))
                lines.Add(reader.GetString(reader.GetOrdinal("detail")));

            var raw = string.Join("\n", lines);
            return new AnalyzeResult(raw, Executed: false);
        }
        catch (OperationCanceledException)
        {
            return new AnalyzeResult(string.Empty, Executed: false, TimedOut: true);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.CloseAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private static void GuardIdentifier(string name)
    {
        if (!SqlIdentifier.IsValid(name))
            throw new ArgumentException($"Invalid identifier '{name}'.", nameof(name));
    }

    private async Task EnsureOpenAsync(CancellationToken cancellationToken)
    {
        if (_connection.State != System.Data.ConnectionState.Open)
        {
            await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var pragma = _connection.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<string>> ReadIndexColumnsAsync(string indexName, CancellationToken cancellationToken)
    {
        var cols = new List<string>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA index_info(\"{indexName}\")";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            cols.Add(reader.GetString(reader.GetOrdinal("name")));
        return cols;
    }

    private static async Task<QueryResult> ReadResultAsync(SqliteDataReader reader, CancellationToken cancellationToken)
    {
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var rows = new List<IReadOnlyDictionary<string, object?>>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
                row[columns[i]] = await reader.IsDBNullAsync(i, cancellationToken).ConfigureAwait(false) ? null : reader.GetValue(i);

            rows.Add(row);
        }

        return new QueryResult(columns, rows);
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