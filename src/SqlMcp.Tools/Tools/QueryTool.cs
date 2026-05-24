using System.ComponentModel;
using ModelContextProtocol.Server;
using SqlMcp.Tools.Drivers;
using SqlMcp.Tools.Models;
using SqlMcp.Tools.Security;

namespace SqlMcp.Tools.Tools;

public sealed record QueryResponse(
    DbDialect Dialect,
    SqlStatementType StatementType,
    int? AffectedRows,
    string? InsertId,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);

[McpServerToolType]
public sealed class QueryTool(
    IDatabaseDriver db,
    ISqlStatementClassifier classifier,
    SqlPermissionOptions permissions)
{
    private const int DefaultMaxRows = 100;

    [McpServerTool(Name = "query", Title = "Execute SQL Query")]
    [Description("Execute a SQL statement against the connected database. Read operations (SELECT/SHOW/DESCRIBE/EXPLAIN) are always allowed. Write/DDL require explicit opt-in via startup flags.")]
    public async Task<QueryResponse> ExecuteAsync(
        [Description("SQL statement to execute")] string sql,
        [Description("Maximum rows to return for read queries (default: 100, max: 10000)")] int? max_rows = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new ArgumentException("sql must not be empty.", nameof(sql));

        if (classifier.HasMultipleStatements(sql))
            throw new ArgumentException("Multi-statement queries are not allowed. Execute one statement at a time.", nameof(sql));

        var stmtType = classifier.Classify(sql);
        var (allowed, reason) = SqlPermissionChecker.Check(stmtType, permissions);
        if (!allowed)
            throw new InvalidOperationException($"Permission denied: {reason}");

        var cappedMaxRows = Math.Clamp(max_rows ?? DefaultMaxRows, 1, 10_000);
        var isReadOnly = stmtType is SqlStatementType.Select or SqlStatementType.Show or SqlStatementType.Describe or SqlStatementType.Explain;

        var result = await db.ExecuteQueryAsync(sql, isReadOnly, cappedMaxRows, cancellationToken).ConfigureAwait(false);

        return new QueryResponse(
            Dialect: db.Dialect,
            StatementType: stmtType,
            AffectedRows: result.AffectedRows,
            InsertId: result.InsertId,
            Columns: result.Columns,
            Rows: result.Rows);
    }
}
