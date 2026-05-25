using System.Net;
using Microsoft.Data.SqlClient;
using SqlMcp.Tools.Models;
using SqlMcp.Tools.Security;

namespace SqlMcp.Tools.Drivers.Dialects;

internal sealed class MsSqlDatabaseDriver(string connectionString) : IDatabaseDriver
{
    public static IDatabaseDriver Create(string uri, bool ssl)
    {
        var parsedUri = new Uri(uri);
        var db = parsedUri.AbsolutePath.Trim('/');
        var userInfo = parsedUri.UserInfo.Split(':', 2);
        var user = WebUtility.UrlDecode(userInfo.ElementAtOrDefault(0) ?? "");
        var pass = WebUtility.UrlDecode(userInfo.ElementAtOrDefault(1) ?? "");

        var b = new SqlConnectionStringBuilder
        {
            DataSource = parsedUri.Host + (parsedUri.Port > 0 ? $",{parsedUri.Port}" : ",1433"),
            UserID = string.IsNullOrEmpty(user) ? null : user,
            Password = string.IsNullOrEmpty(pass) ? null : pass,
            InitialCatalog = db,
            Encrypt = ssl,
            TrustServerCertificate = false,
        };

        if (string.IsNullOrEmpty(user) && string.IsNullOrEmpty(pass))
            b.IntegratedSecurity = true;

        return new MsSqlDatabaseDriver(b.ConnectionString);
    }

    public DbDialect Dialect => DbDialect.Mssql;

    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        _ = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TableInfo>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT TABLE_NAME, TABLE_TYPE
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE IN ('BASE TABLE', 'VIEW')
  AND TABLE_CATALOG = DB_NAME()
ORDER BY TABLE_NAME";

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

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        var columns = new List<ColumnInfo>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH,
       IS_NULLABLE, COLUMN_DEFAULT, ORDINAL_POSITION
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_CATALOG = DB_NAME() AND TABLE_NAME = @t
ORDER BY ORDINAL_POSITION";
            cmd.Parameters.AddWithValue("@t", tableName);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var colName = reader.GetString(0);
                var dataType = reader.GetString(1);
                var maxLen = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
                var nullable = string.Equals(reader.GetString(3), "YES", StringComparison.OrdinalIgnoreCase);
                var def = reader.IsDBNull(4) ? null : reader.GetValue(4)?.ToString();

                var displayType = BuildDataType(dataType, maxLen);

                columns.Add(new ColumnInfo(
                    Name: colName,
                    DataType: displayType,
                    Nullable: nullable,
                    DefaultValue: def,
                    IsPrimaryKey: false,
                    IsUnique: false,
                    Extra: string.Equals(dataType, "uniqueidentifier", StringComparison.OrdinalIgnoreCase) &&
                           def is not null && def.Contains("newid", StringComparison.OrdinalIgnoreCase)
                        ? "DEFAULT_NEWID"
                        : null));
            }
        }

        // primary keys
        var pkCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT c.name
FROM sys.indexes i
JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
JOIN sys.tables t ON i.object_id = t.object_id
WHERE t.name = @t AND i.is_primary_key = 1";
            cmd.Parameters.AddWithValue("@t", tableName);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                pkCols.Add(reader.GetString(0));
        }

        columns = columns.Select(c => c with { IsPrimaryKey = pkCols.Contains(c.Name) }).ToList();

        // indexes and unique constraints
        var indexMap = new Dictionary<string, (bool unique, List<(int seq, string col)> cols)>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT i.name, i.is_unique, c.name, ic.key_ordinal
FROM sys.indexes i
JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
JOIN sys.tables t ON i.object_id = t.object_id
WHERE t.name = @t AND i.is_primary_key = 0
ORDER BY i.name, ic.key_ordinal";
            cmd.Parameters.AddWithValue("@t", tableName);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var idxName = reader.GetString(0);
                var unique = reader.GetBoolean(1);
                var colName = reader.GetString(2);
                var seq = reader.GetInt32(3);

                if (!indexMap.TryGetValue(idxName, out var entry))
                    entry = (unique, new List<(int seq, string col)>());

                entry.cols.Add((seq, colName));
                indexMap[idxName] = entry;
            }
        }

        var uniqueSingleCols = indexMap
            .Where(i => i.Value.unique && i.Value.cols.Count == 1)
            .Select(i => i.Value.cols[0].col)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        columns = columns.Select(c => c with { IsUnique = !c.IsPrimaryKey && uniqueSingleCols.Contains(c.Name) }).ToList();

        var indexes = indexMap
            .Select(kvp => new IndexInfo(kvp.Key, kvp.Value.unique, kvp.Value.cols.OrderBy(x => x.seq).Select(x => x.col).ToArray()))
            .ToArray();

        // foreign keys
        var foreignKeys = new List<ForeignKeyInfo>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT fk.name, c.name, rt.name, rc.name
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
JOIN sys.columns c ON fkc.parent_object_id = c.object_id AND fkc.parent_column_id = c.column_id
JOIN sys.tables rt ON fkc.referenced_object_id = rt.object_id
JOIN sys.columns rc ON fkc.referenced_object_id = rc.object_id AND fkc.referenced_column_id = rc.column_id
JOIN sys.tables t ON fk.parent_object_id = t.object_id
WHERE t.name = @t
ORDER BY fk.name, fkc.constraint_column_id";
            cmd.Parameters.AddWithValue("@t", tableName);

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

        return new TableDescription(tableName, columns, indexes, foreignKeys);
    }

    public async Task<QueryResult> ExecuteQueryAsync(string sql, bool isReadOnly, int maxRows, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        if (isReadOnly)
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await ReadResultAsync(reader, maxRows, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            string? insertId = null;
            try
            {
                await using var idCmd = conn.CreateCommand();
                idCmd.CommandText = "SELECT SCOPE_IDENTITY()";
                var id = await idCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (id is not null && id is not DBNull && decimal.TryParse(id.ToString(), out var parsed) && parsed > 0)
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

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            if (execute)
            {
                // SET STATISTICS PROFILE ON — returns plan alongside actual data.
                await using (var setCmd = conn.CreateCommand())
                {
                    setCmd.CommandText = "SET STATISTICS PROFILE ON";
                    await setCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    await using var queryCmd = conn.CreateCommand();
                    queryCmd.CommandText = stripped;
                    queryCmd.CommandTimeout = (int)Math.Ceiling(timeout.TotalSeconds);

                    await using var reader = await queryCmd.ExecuteReaderAsync(cts.Token).ConfigureAwait(false);
                    var lines = new List<string>();
                    do
                    {
                        while (await reader.ReadAsync(cts.Token).ConfigureAwait(false))
                        {
                            if (!await reader.IsDBNullAsync(0, cts.Token).ConfigureAwait(false))
                                lines.Add(reader.GetString(0));
                        }
                    } while (await reader.NextResultAsync(cts.Token).ConfigureAwait(false));

                    var raw = string.Join("\n", lines);
                    return new AnalyzeResult(raw, true);
                }
                finally
                {
                    await using (var setCmd = conn.CreateCommand())
                    {
                        setCmd.CommandText = "SET STATISTICS PROFILE OFF";
                        await setCmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                // SET SHOWPLAN_TEXT ON — estimated plan only, no execution.
                await using (var setCmd = conn.CreateCommand())
                {
                    setCmd.CommandText = "SET SHOWPLAN_TEXT ON";
                    await setCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    await using var queryCmd = conn.CreateCommand();
                    queryCmd.CommandText = stripped;
                    queryCmd.CommandTimeout = (int)Math.Ceiling(timeout.TotalSeconds);

                    await using var reader = await queryCmd.ExecuteReaderAsync(cts.Token).ConfigureAwait(false);
                    var lines = new List<string>();
                    while (await reader.ReadAsync(cts.Token).ConfigureAwait(false))
                        lines.Add(reader.GetString(0));

                    var raw = string.Join("\n", lines);
                    return new AnalyzeResult(raw, false);
                }
                finally
                {
                    await using (var setCmd = conn.CreateCommand())
                    {
                        setCmd.CommandText = "SET SHOWPLAN_TEXT OFF";
                        await setCmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            return new AnalyzeResult(string.Empty, execute, TimedOut: true);
        }
        catch (SqlException ex) when (ex.Number == -2) // timeout
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

    private static string BuildDataType(string baseType, int? maxLength)
    {
        if (maxLength is null or 0)
            return baseType;

        return maxLength.Value switch
        {
            -1 => $"{baseType}(MAX)",
            _ => $"{baseType}({maxLength})"
        };
    }

    private static async Task<QueryResult> ReadResultAsync(SqlDataReader reader, int maxRows, CancellationToken cancellationToken)
    {
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var rows = new List<IReadOnlyList<object?>>(capacity: Math.Min(maxRows, 1000));

        while (rows.Count < maxRows && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new List<object?>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var val = await reader.IsDBNullAsync(i, cancellationToken).ConfigureAwait(false) ? null : reader.GetValue(i);
                row.Add(val);
            }
            rows.Add(row);
        }

        return new QueryResult(columns, rows);
    }
}