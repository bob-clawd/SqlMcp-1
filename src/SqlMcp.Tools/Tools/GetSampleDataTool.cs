using System.ComponentModel;
using ModelContextProtocol.Server;
using SqlMcp.Tools.Drivers;
using SqlMcp.Tools.Models;

namespace SqlMcp.Tools.Tools;

public sealed record GetSampleDataResponse(
    DbDialect Dialect,
    string TableName,
    int RowCount,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);

[McpServerToolType]
public sealed class GetSampleDataTool(IDatabaseDriver db)
{
    [McpServerTool(Name = "get_sample_data", Title = "Get Sample Data")]
    [Description("Get a small sample of rows from a table. Always read-only. Useful for understanding data shape and values.")]
    public async Task<GetSampleDataResponse> ExecuteAsync(
        [Description("Table to sample from")] string table_name,
        [Description("Number of rows to return (default: 5, max: 100)")] int? limit = null,
        [Description("Optional ORDER BY column name")] string? order_by_column = null,
        [Description("If true, sorts descending")] bool order_desc = false,
        CancellationToken cancellationToken = default)
    {
        var lim = limit ?? 5;
        var result = await db.GetSampleDataAsync(table_name, lim, order_by_column, order_desc, cancellationToken).ConfigureAwait(false);

        return new GetSampleDataResponse(
            db.Dialect,
            table_name,
            result.Rows.Count,
            result.Columns,
            result.Rows);
    }
}
