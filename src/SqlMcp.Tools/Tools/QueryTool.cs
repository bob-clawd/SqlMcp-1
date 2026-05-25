using System.ComponentModel;
using ModelContextProtocol.Server;
using SqlMcp.Tools.Drivers;
using SqlMcp.Tools.Models;
using SqlMcp.Tools.Security;

namespace SqlMcp.Tools.Tools;

public sealed record QueryResponse(
    int? AffectedRows,
    string? InsertId,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows);

[McpServerToolType]
public sealed class QueryTool(
    IDatabaseDriver db,
    SqlStatementClassifier classifier,
    SqlPermissionOptions permissions)
{
    [McpServerTool(Name = "execute_query", Title = "Execute SQL Query")]
    [Description("Execute a SQL statement against the connected database. Read operations (SELECT/SHOW/DESCRIBE/EXPLAIN) are always allowed. Write/DDL require explicit opt-in via startup flags.")]
    public async Task<QueryResponse> ExecuteAsync(
        [Description("SQL statement to execute")] string sql,
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

        var isReadOnly = stmtType is SqlStatementType.Select or SqlStatementType.Show or SqlStatementType.Describe or SqlStatementType.Explain;

        var result = await db.ExecuteQueryAsync(sql, isReadOnly, 100, cancellationToken).ConfigureAwait(false);

        return new QueryResponse(
            AffectedRows: result.AffectedRows,
            InsertId: result.InsertId,
            Columns: result.Columns,
            Rows: result.Rows);
    }
}
