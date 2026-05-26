using System.ComponentModel;
using ModelContextProtocol.Server;
using SqlMcp.Tools.Drivers;
using SqlMcp.Tools.Models;

namespace SqlMcp.Tools.Tools;

public sealed record AnalyzeQueryResponse(
    bool Executed = false,
    bool TimedOut = false,
    string? Raw = null,
    ErrorInfo? Error = null)
{
    public static AnalyzeQueryResponse AsError(ErrorInfo error) => new(Error: error);
}

[McpServerToolType]
public sealed class AnalyzeQueryTool(IDatabaseDriver db)
{
    [McpServerTool(Name = "analyze_query", Title = "Analyze SQL Query")]
    [Description("EXPLAIN query plan. execute=true for actual timings (SELECT only).")]
    public async Task<AnalyzeQueryResponse> ExecuteAsync(
        [Description("SQL to analyze")] string sql,
        [Description("Execute for actual timings (SELECT only)")] bool execute = false,
        [Description("Timeout in milliseconds")] int timeout_ms = 5000,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return AnalyzeQueryResponse.AsError(new ErrorInfo("sql must not be empty."));

        try
        {
            var timeout = TimeSpan.FromMilliseconds(Math.Clamp(timeout_ms, 500, 60_000));
            var result = await db.AnalyzeQueryAsync(sql, execute, timeout, cancellationToken).ConfigureAwait(false);

            return new AnalyzeQueryResponse(
                Executed: result.Executed,
                TimedOut: result.TimedOut,
                Raw: result.Raw);
        }
        catch (Exception ex)
        {
            return AnalyzeQueryResponse.AsError(new ErrorInfo(ex.Message,
                new Dictionary<string, string> { ["sql"] = sql }));
        }
    }

}
