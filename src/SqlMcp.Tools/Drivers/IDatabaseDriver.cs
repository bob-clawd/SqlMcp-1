using SqlMcp.Tools.Models;

namespace SqlMcp.Tools.Drivers;

public interface IDatabaseDriver : IAsyncDisposable
{
    DbDialect Dialect { get; }

    Task TestConnectionAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TableInfo>> ListTablesAsync(CancellationToken cancellationToken = default);
    Task<TableDescription> DescribeTableAsync(string tableName, CancellationToken cancellationToken = default);

    Task<QueryResult> QueryAsync(string sql, int limit, CancellationToken cancellationToken = default);
    Task<ExecutionResult> ExecuteAsync(string sql, CancellationToken cancellationToken = default);

    Task<AnalyzeResult> AnalyzeQueryAsync(string sql, bool execute, TimeSpan timeout, CancellationToken cancellationToken = default);
}
