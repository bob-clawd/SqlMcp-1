using System.Net;
using System.Text.RegularExpressions;
using Oracle.ManagedDataAccess.Client;
using SqlMcp.Tools.Models;
using SqlMcp.Tools.Security;

namespace SqlMcp.Tools.Drivers.Dialects;

internal sealed class OracleDatabaseDriver(string connectionString) : IDatabaseDriver
{
    public static IDatabaseDriver Create(string uri, bool ssl)
    {
        var parsedUri = new Uri(uri);
        var serviceName = parsedUri.AbsolutePath.Trim('/');
        var userInfo = parsedUri.UserInfo.Split(':', 2);
        var user = WebUtility.UrlDecode(userInfo.ElementAtOrDefault(0) ?? "");
        var pass = WebUtility.UrlDecode(userInfo.ElementAtOrDefault(1) ?? "");

        var b = new OracleConnectionStringBuilder
        {
            DataSource = $"{parsedUri.Host}:{(parsedUri.Port > 0 ? parsedUri.Port : 1521)}/{serviceName}",
            UserID = string.IsNullOrEmpty(user) ? null : user,
            Password = string.IsNullOrEmpty(pass) ? null : pass,
        };

        if (ssl)
            b["SSL"] = "TRUE";

        return new OracleDatabaseDriver(b.ConnectionString);
    }

    public DbDialect Dialect => DbDialect.Oracle;

    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new OracleConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM DUAL";
        _ = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TableInfo>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new OracleConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT TABLE_NAME, 'TABLE' FROM USER_TABLES
UNION ALL
SELECT VIEW_NAME, 'VIEW' FROM USER_VIEWS
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

        await using var conn = new OracleConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        var columns = new List<ColumnInfo>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT COLUMN_NAME, DATA_TYPE, DATA_LENGTH, DATA_PRECISION, DATA_SCALE,
       NULLABLE, DATA_DEFAULT, COLUMN_ID
FROM USER_TAB_COLUMNS
WHERE TABLE_NAME = :t
ORDER BY COLUMN_ID";
            cmd.Parameters.Add(new OracleParameter(":t", tableName));

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var colName = reader.GetString(0);
                var dataType = reader.GetString(1);
                var dataLen = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
                var precision = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3);
                var scale = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
                var nullable = string.Equals(reader.GetString(5), "Y", StringComparison.OrdinalIgnoreCase);
                var def = reader.IsDBNull(6) ? null : reader.GetValue(6)?.ToString();

                var displayType = BuildDataType(dataType, dataLen, precision, scale);

                columns.Add(new ColumnInfo(
                    Name: colName,
                    DataType: displayType,
                    Nullable: nullable,
                    DefaultValue: def,
                    IsPrimaryKey: false,
                    IsUnique: false,
                    Extra: null));
            }
        }

        if (columns.Count == 0)
            throw new ArgumentException($"Table '{tableName}' does not exist.", nameof(tableName));

        // primary keys
        var pkCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT cc.COLUMN_NAME
FROM USER_CONSTRAINTS c
JOIN USER_CONS_COLUMNS cc ON c.CONSTRAINT_NAME = cc.CONSTRAINT_NAME
WHERE c.TABLE_NAME = :t AND c.CONSTRAINT_TYPE = 'P'
ORDER BY cc.POSITION";
            cmd.Parameters.Add(new OracleParameter(":t", tableName));

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                pkCols.Add(reader.GetString(0));
        }

        columns = columns.Select(c => c with { IsPrimaryKey = pkCols.Contains(c.Name) }).ToList();

        // indexes (excluding pk indexes)
        var indexMap = new Dictionary<string, (bool unique, List<(int seq, string col)> cols)>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT i.INDEX_NAME, i.UNIQUENESS, ic.COLUMN_NAME, ic.COLUMN_POSITION
FROM USER_INDEXES i
JOIN USER_IND_COLUMNS ic ON i.INDEX_NAME = ic.INDEX_NAME AND i.TABLE_NAME = ic.TABLE_NAME
LEFT JOIN USER_CONSTRAINTS pk ON i.TABLE_NAME = pk.TABLE_NAME
  AND i.INDEX_NAME = pk.CONSTRAINT_NAME AND pk.CONSTRAINT_TYPE = 'P'
WHERE i.TABLE_NAME = :t AND pk.CONSTRAINT_NAME IS NULL
ORDER BY i.INDEX_NAME, ic.COLUMN_POSITION";
            cmd.Parameters.Add(new OracleParameter(":t", tableName));

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var idxName = reader.GetString(0);
                var unique = string.Equals(reader.GetString(1), "UNIQUE", StringComparison.OrdinalIgnoreCase);
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
            cmd.CommandText = @"SELECT rc.CONSTRAINT_NAME, rc.COLUMN_NAME,
       pk.TABLE_NAME AS REF_TABLE, pk.COLUMN_NAME AS REF_COLUMN
FROM (SELECT c.CONSTRAINT_NAME, cc.COLUMN_NAME, cc.POSITION, c.R_CONSTRAINT_NAME
      FROM USER_CONSTRAINTS c
      JOIN USER_CONS_COLUMNS cc ON c.CONSTRAINT_NAME = cc.CONSTRAINT_NAME
      WHERE c.TABLE_NAME = :t AND c.CONSTRAINT_TYPE = 'R') rc
JOIN USER_CONS_COLUMNS pk ON pk.CONSTRAINT_NAME = rc.R_CONSTRAINT_NAME AND pk.POSITION = rc.POSITION
ORDER BY rc.CONSTRAINT_NAME, rc.POSITION";
            cmd.Parameters.Add(new OracleParameter(":t", tableName));

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
        await using var conn = new OracleConnection(connectionString);
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
            return new QueryResult([], [], affected, null);
        }
    }

    public async Task<AnalyzeResult> AnalyzeQueryAsync(string sql, bool execute, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var stripped = sql.Trim().TrimEnd(';');

        await using var conn = new OracleConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            // Oracle: EXPLAIN PLAN FOR <query> then read plan via DBMS_XPLAN.DISPLAY.
            await using (var planCmd = conn.CreateCommand())
            {
                planCmd.CommandText = $"EXPLAIN PLAN FOR {stripped}";
                planCmd.CommandTimeout = (int)Math.Ceiling(timeout.TotalSeconds);
                await planCmd.ExecuteNonQueryAsync(cts.Token).ConfigureAwait(false);
            }

            await using var displayCmd = conn.CreateCommand();
            displayCmd.CommandText = "SELECT * FROM TABLE(DBMS_XPLAN.DISPLAY())";
            displayCmd.CommandTimeout = (int)Math.Ceiling(timeout.TotalSeconds);

            await using var reader = await displayCmd.ExecuteReaderAsync(cts.Token).ConfigureAwait(false);
            var lines = new List<string>();
            while (await reader.ReadAsync(cts.Token).ConfigureAwait(false))
            {
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    if (!await reader.IsDBNullAsync(i, cts.Token).ConfigureAwait(false))
                        lines.Add(reader.GetString(i));
                }
            }

            var raw = string.Join("\n", lines);
            return new AnalyzeResult(raw, false);
        }
        catch (OperationCanceledException)
        {
            return new AnalyzeResult(string.Empty, false, TimedOut: true);
        }
        catch (OracleException ex) when (ex.Number == 1013) // ORA-01013: user requested cancel
        {
            return new AnalyzeResult(string.Empty, false, TimedOut: true);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static void GuardIdentifier(string name)
    {
        if (!SqlIdentifier.IsValid(name))
            throw new ArgumentException($"Invalid identifier '{name}'.", nameof(name));
    }

    private static string BuildDataType(string baseType, int? dataLength, int? precision, int? scale)
    {
        if (baseType is "CLOB" or "BLOB" or "NCLOB" or "LONG" or "RAW")
            return baseType;

        if (baseType == "NUMBER" && precision.HasValue)
            return scale.HasValue && scale > 0
                ? $"{baseType}({precision},{scale})"
                : $"{baseType}({precision})";

        if (baseType is "VARCHAR2" or "NVARCHAR2" or "CHAR" or "NCHAR")
        {
            if (dataLength.HasValue)
                return dataLength == -1 ? $"{baseType}(MAX)" : $"{baseType}({dataLength})";
            return $"{baseType}(1)";
        }

        return baseType;
    }

    private static async Task<QueryResult> ReadResultAsync(OracleDataReader reader, CancellationToken cancellationToken)
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

    private static string ApplyLimitIfMissing(string sql, int maxRows)
    {
        if (s_fetchFirstRegex.IsMatch(sql))
            return sql;

        var trimmed = sql.TrimEnd();
        if (trimmed.EndsWith(';'))
            trimmed = trimmed[..^1];

        return trimmed + $" FETCH FIRST {maxRows} ROWS ONLY";
    }

    private static readonly Regex s_fetchFirstRegex = new(@"\bFETCH\s+FIRST\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
}