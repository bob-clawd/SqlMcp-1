using System.ComponentModel;
using ModelContextProtocol.Server;
using SqlMcp.Tools.Drivers;
using SqlMcp.Tools.Models;

namespace SqlMcp.Tools.Tools;

public sealed record ListTablesResponse(
    DbDialect Dialect,
    IReadOnlyList<string> Tables,
    IReadOnlyList<string> Views,
    ToolError? Error = null);

[McpServerToolType]
public sealed class ListTablesTool(IDatabaseDriver db)
{
    [McpServerTool(Name = "list_tables", Title = "List Tables")]
    [Description("All tables and views in the database.")]
    public async Task<ListTablesResponse> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var all = await db.ListTablesAsync(cancellationToken).ConfigureAwait(false);
        var tables = all.Where(t => t.Type == DbTableType.Table).Select(t => t.Name).ToArray();
        var views = all.Where(t => t.Type == DbTableType.View).Select(t => t.Name).ToArray();
        return new ListTablesResponse(db.Dialect, tables, views);
    }
}
