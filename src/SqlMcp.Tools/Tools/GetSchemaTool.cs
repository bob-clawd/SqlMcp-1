using System.ComponentModel;
using ModelContextProtocol.Server;
using SqlMcp.Tools.Drivers;
using SqlMcp.Tools.Models;

namespace SqlMcp.Tools.Tools;

public sealed record GetSchemaResponse(
    DbDialect Dialect,
    IReadOnlyList<TableDescription> Schema);

[McpServerToolType]
public sealed class GetSchemaTool(IDatabaseDriver db)
{
    [McpServerTool(Name = "get_schema", Title = "Get Database Schema")]
    [Description("Get a schema dump of the database: tables + columns (+ foreign keys). Indexes may be omitted for large schemas.")]
    public async Task<GetSchemaResponse> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var schema = await db.GetSchemaAsync(cancellationToken).ConfigureAwait(false);
        return new GetSchemaResponse(db.Dialect, schema);
    }
}
