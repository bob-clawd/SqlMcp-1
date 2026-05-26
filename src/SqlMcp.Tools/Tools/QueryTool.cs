using System.ComponentModel;
using ModelContextProtocol.Server;
using SqlMcp.Tools.Drivers;
using SqlMcp.Tools.Models;

namespace SqlMcp.Tools.Tools;

public sealed record QueryResponse(
    IReadOnlyList<string>? Columns = null,
    IReadOnlyList<IReadOnlyList<object?>>? Rows = null,
    ErrorInfo? Error = null)
{
    public static QueryResponse AsError(ErrorInfo error) => new(Error: error);
}

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
            return QueryResponse.AsError(new ErrorInfo("sql must not be empty."));

        try
        {
            var cappedLimit = Math.Clamp(limit, 1, 10_000);
            var result = await db.QueryAsync(sql, cappedLimit, cancellationToken).ConfigureAwait(false);
            return new QueryResponse(result.Columns, result.Rows);
        }
        catch (Exception ex)
        {
            return QueryResponse.AsError(new ErrorInfo(ex.Message,
                new Dictionary<string, string> { ["sql"] = sql }));
        }
    }
}
