using System.ComponentModel;
using ModelContextProtocol.Server;
using SqlMcp.Tools.Drivers;
using SqlMcp.Tools.Models;
using SqlMcp.Tools.Security;

namespace SqlMcp.Tools.Tools;

public sealed record ExecuteResponse(
    int? AffectedRows = null,
    string? InsertId = null,
    ToolError? Error = null);

[McpServerToolType]
public sealed class ExecuteTool(IDatabaseDriver db)
{
    [McpServerTool(Name = "execute", Title = "Run a modifying SQL statement")]
    [Description("INSERT, UPDATE, DELETE, ALTER, CREATE, DROP, TRUNCATE.")]
    public async Task<ExecuteResponse> ExecuteAsync(
        [Description("SQL to execute")] string sql,
        [Description("Confirm write operation")] bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return new ExecuteResponse(Error: new ToolError("sql must not be empty."));

        if (SqlTokenizer.HasMultipleStatements(sql))
            return new ExecuteResponse(Error: new ToolError("Multi-statement queries are not allowed. Execute one statement at a time."));

        if (!confirm)
            return new ExecuteResponse(Error: new ToolError("Set confirm=true to execute write statements."));

        var result = await db.ExecuteAsync(sql, cancellationToken).ConfigureAwait(false);

        return new ExecuteResponse(result.AffectedRows, result.InsertId);
    }
}
