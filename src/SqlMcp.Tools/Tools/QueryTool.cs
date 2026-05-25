using System.ComponentModel;
using ModelContextProtocol.Server;
using SqlMcp.Tools.Drivers;
using SqlMcp.Tools.Models;
using SqlMcp.Tools.Security;

namespace SqlMcp.Tools.Tools;

public sealed record QueryResponse(
    IReadOnlyList<string>? Columns = null,
    IReadOnlyList<IReadOnlyList<object?>>? Rows = null,
    ToolError? Error = null);

[McpServerToolType]
public sealed class QueryTool(IDatabaseDriver db)
{
    [McpServerTool(Name = "query", Title = "Run a read-only SQL query")]
    [Description("SELECT, SHOW, DESCRIBE, EXPLAIN only.")]
    public async Task<QueryResponse> ExecuteAsync(
        [Description("SQL to execute")] string sql,
        [Description("Max rows")] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return new QueryResponse(Error: new ToolError("sql must not be empty."));

        if (SqlTokenizer.HasMultipleStatements(sql))
            return new QueryResponse(Error: new ToolError("Multi-statement queries are not allowed. Execute one statement at a time."));

        var cappedLimit = Math.Clamp(limit, 1, 10_000);
        var result = await db.QueryAsync(sql, cappedLimit, cancellationToken).ConfigureAwait(false);

        return new QueryResponse(result.Columns, result.Rows);
    }
}
