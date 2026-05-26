using System.ComponentModel;
using ModelContextProtocol.Server;
using SqlMcp.Tools.Drivers;
using SqlMcp.Tools.Models;

namespace SqlMcp.Tools.Tools;

public sealed record ExecuteResponse(
    int? AffectedRows = null,
    ErrorInfo? Error = null)
{
    public static ExecuteResponse AsError(ErrorInfo error) => new(Error: error);
}

[McpServerToolType]
public sealed class ExecuteTool(IDatabaseDriver db)
{
    [McpServerTool(Name = "execute", Title = "Run a modifying SQL statement")]
    [Description("INSERT, UPDATE, DELETE, ALTER, CREATE, DROP, TRUNCATE.")]
    public async Task<ExecuteResponse> ExecuteAsync(
        [Description("SQL to execute")] string sql,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return ExecuteResponse.AsError(new ErrorInfo("sql must not be empty."));

        return await ToolHelper.RunAsync(
            async () =>
            {
                var result = await db.ExecuteAsync(sql, cancellationToken).ConfigureAwait(false);
                return new ExecuteResponse(result.AffectedRows);
            },
            error => ExecuteResponse.AsError(error),
            new Dictionary<string, string> { ["sql"] = sql });
    }
}
