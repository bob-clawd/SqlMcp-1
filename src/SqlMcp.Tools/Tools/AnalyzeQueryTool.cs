using System.ComponentModel;
using ModelContextProtocol.Server;
using SqlMcp.Tools.Drivers;
using SqlMcp.Tools.Models;
using SqlMcp.Tools.Security;

namespace SqlMcp.Tools.Tools;

public sealed record AnalyzeQueryResponse(
    DbDialect Dialect,
    bool Executed,
    bool TimedOut,
    string Raw);

[McpServerToolType]
public sealed class AnalyzeQueryTool(
    IDatabaseDriver db,
    SqlStatementClassifier classifier)
{
    [McpServerTool(Name = "analyze_query", Title = "Analyze SQL Query")]
    [Description("Analyze a SQL query for performance. Shows raw EXPLAIN output. Defaults to plan-only (execute=false). Set execute=true to capture actual timing for SELECT.")]
    public async Task<AnalyzeQueryResponse> ExecuteAsync(
        [Description("SQL statement to analyze")] string sql,
        [Description("If true, executes the query to collect actual timing (SELECT only). Default: false.")] bool execute = false,
        [Description("Hard timeout in milliseconds for analyze. Default: 5000ms.")] int timeout_ms = 5000,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new ArgumentException("sql must not be empty.", nameof(sql));

        if (classifier.HasMultipleStatements(sql))
            throw new ArgumentException("Multi-statement queries are not allowed. Analyze one statement at a time.", nameof(sql));

        var stmtType = classifier.Classify(sql);
        if (execute && stmtType != SqlStatementType.Select)
            throw new InvalidOperationException($"execute=true is only allowed for SELECT statements (got {stmtType}). Use execute=false for plan-only analysis.");

        var timeout = TimeSpan.FromMilliseconds(Math.Clamp(timeout_ms, 500, 60_000));
        var result = await db.AnalyzeQueryAsync(sql, execute, timeout, cancellationToken).ConfigureAwait(false);

        return new AnalyzeQueryResponse(
            Dialect: db.Dialect,
            Executed: result.Executed,
            TimedOut: result.TimedOut,
            Raw: result.Raw);
    }
}
