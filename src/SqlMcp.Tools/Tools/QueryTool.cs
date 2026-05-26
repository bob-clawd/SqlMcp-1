using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using SqlMcp.Tools.Drivers;
using SqlMcp.Tools.Models;

namespace SqlMcp.Tools.Tools;

public sealed record QueryResponse(
    IReadOnlyList<string>? Columns = null,
    IReadOnlyList<IReadOnlyList<object?>>? Rows = null,
    int? RowCount = null,
    string? FilePath = null,
    ErrorInfo? Error = null)
{
    public static QueryResponse AsError(ErrorInfo error) => new(Error: error);
}

[McpServerToolType]
public sealed class QueryTool(IDatabaseDriver db)
{
    private const int AutoExportThreshold = 100;
    private const string ExportFileName = "query_result.csv";

    [McpServerTool(Name = "query", Title = "Run a read-only SQL query")]
    [Description("SELECT, SHOW, DESCRIBE, EXPLAIN only.")]
    public async Task<QueryResponse> ExecuteAsync(
        [Description("SQL to execute")] string sql,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return QueryResponse.AsError(new ErrorInfo("sql must not be empty."));

        return await ToolHelper.RunAsync(
            async () =>
            {
                var result = await db.QueryAsync(sql, int.MaxValue, cancellationToken).ConfigureAwait(false);
                var count = result.Rows.Count;

                if (count <= AutoExportThreshold)
                {
                    return new QueryResponse(
                        Columns: result.Columns,
                        Rows: result.Rows,
                        RowCount: count);
                }

                // Too large for inline — export to CSV file instead.
                var csv = ToCsv(result.Columns, result.Rows);
                await File.WriteAllTextAsync(ExportFileName, csv, cancellationToken).ConfigureAwait(false);

                return new QueryResponse(
                    Columns: result.Columns,
                    RowCount: count,
                    FilePath: ExportFileName);
            },
            error => QueryResponse.AsError(error),
            new Dictionary<string, string> { ["sql"] = sql });
    }

    private static string ToCsv(IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(";", columns));

        foreach (var row in rows)
        {
            for (var i = 0; i < row.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(EscapeCsvField(row[i]));
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string EscapeCsvField(object? value)
    {
        if (value is null)
            return string.Empty;

        var s = value.ToString() ?? string.Empty;

        if (s.Contains(';') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";

        return s;
    }
}
