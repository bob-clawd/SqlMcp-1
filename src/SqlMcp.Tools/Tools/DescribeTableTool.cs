using System.ComponentModel;
using ModelContextProtocol.Server;
using SqlMcp.Tools.Drivers;
using SqlMcp.Tools.Models;

namespace SqlMcp.Tools.Tools;

public sealed record DescribeTableResponse(
    TableDescription? Table = null,
    ErrorInfo? Error = null)
{
    public static DescribeTableResponse AsError(ErrorInfo error) => new(Error: error);
}

[McpServerToolType]
public sealed class DescribeTableTool(IDatabaseDriver db)
{
    [McpServerTool(Name = "describe_table", Title = "Describe Table")]
    [Description("Full schema: columns, indexes, foreign keys.")]
    public async Task<DescribeTableResponse> ExecuteAsync(
        [Description("Table name")] string table_name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(table_name))
            return DescribeTableResponse.AsError(new ErrorInfo("table_name must not be empty.",
                new Dictionary<string, string> { ["table_name"] = table_name ?? "" }));

        if (!table_name.All(c => char.IsLetterOrDigit(c) || c == '_'))
            return DescribeTableResponse.AsError(new ErrorInfo($"Invalid table name '{table_name}'.",
                new Dictionary<string, string> { ["table_name"] = table_name }));

        var table = await db.DescribeTableAsync(table_name, cancellationToken).ConfigureAwait(false);
        return new DescribeTableResponse(table);
    }
}
