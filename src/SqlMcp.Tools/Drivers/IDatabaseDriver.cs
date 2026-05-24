using SqlMcp.Tools.Models;

namespace SqlMcp.Tools.Drivers;

public interface IDatabaseDriver : IAsyncDisposable
{
    DbDialect Dialect { get; }

    Task TestConnectionAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TableInfo>> ListTablesAsync(CancellationToken cancellationToken = default);
    Task<TableDescription> DescribeTableAsync(string tableName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TableDescription>> GetSchemaAsync(CancellationToken cancellationToken = default);
    Task<QueryResult> GetSampleDataAsync(string tableName, int limit, string? orderByColumn, bool orderDescending,
        CancellationToken cancellationToken = default);

    Task<QueryResult> ExecuteQueryAsync(string sql, bool isReadOnly, int maxRows, CancellationToken cancellationToken = default);

    Task<AnalyzeResult> AnalyzeQueryAsync(string sql, bool execute, TimeSpan timeout, CancellationToken cancellationToken = default);
}
