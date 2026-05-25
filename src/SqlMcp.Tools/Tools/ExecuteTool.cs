using System.ComponentModel;
using ModelContextProtocol.Server;
using SqlMcp.Tools.Drivers;
using SqlMcp.Tools.Models;
using SqlMcp.Tools.Security;

namespace SqlMcp.Tools.Tools;

public sealed record ExecuteResponse(
    int? AffectedRows = null,
    string? InsertId = null,
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
        [Description("Confirm write operation")] bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return ExecuteResponse.AsError(new ErrorInfo("sql must not be empty."));

        if (SqlTokenizer.HasMultipleStatements(sql))
            return ExecuteResponse.AsError(new ErrorInfo("Multi-statement queries are not allowed. Execute one statement at a time.",
                new Dictionary<string, string> { ["sql"] = sql }));

        if (!confirm)
            return ExecuteResponse.AsError(new ErrorInfo("Set confirm=true to execute write statements."));

        var result = await db.ExecuteAsync(sql, cancellationToken).ConfigureAwait(false);

        return new ExecuteResponse(result.AffectedRows, result.InsertId);
    }
}
