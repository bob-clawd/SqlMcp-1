using System.ComponentModel;
using ModelContextProtocol.Server;
using SqlMcp.Tools.Drivers;
using SqlMcp.Tools.Models;

namespace SqlMcp.Tools.Tools;

public sealed record DescribeTableResponse(
    TableDescription Table);

[McpServerToolType]
public sealed class DescribeTableTool(IDatabaseDriver db)
{
    [McpServerTool(Name = "describe_table", Title = "Describe Table")]
    [Description("Get full schema for one table: columns, indexes, and foreign keys.")]
    public async Task<DescribeTableResponse> ExecuteAsync(
        [Description("Table name")] string table_name,
        CancellationToken cancellationToken = default)
    {
        var table = await db.DescribeTableAsync(table_name, cancellationToken).ConfigureAwait(false);
        return new DescribeTableResponse(table);
    }
}
