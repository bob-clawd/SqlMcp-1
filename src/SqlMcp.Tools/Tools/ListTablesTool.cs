using System.ComponentModel;
using ModelContextProtocol.Server;
using SqlMcp.Tools.Drivers;
using SqlMcp.Tools.Models;

namespace SqlMcp.Tools.Tools;

public sealed record ListTablesResponse(
    DbDialect Dialect,
    IReadOnlyList<TableInfo> Tables);

[McpServerToolType]
public sealed class ListTablesTool(IDatabaseDriver db)
{
    [McpServerTool(Name = "list_tables", Title = "List Tables")]
    [Description("List all tables and views in the database.")]
    public async Task<ListTablesResponse> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var tables = await db.ListTablesAsync(cancellationToken).ConfigureAwait(false);
        return new ListTablesResponse(db.Dialect, tables);
    }
}
