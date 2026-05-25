namespace SqlMcp.Tools.Models;

public enum DbDialect
{
    MySql,
    Postgres,
    Sqlite,
    Mssql,
    Oracle
}

public enum DbTableType
{
    Table,
    View
}

public sealed record TableInfo(string Name, DbTableType Type);

public sealed record ColumnInfo(
    string Name,
    string DataType,
    bool Nullable,
    string? DefaultValue,
    bool IsPrimaryKey,
    bool IsUnique,
    string? Extra);

public sealed record IndexInfo(
    string Name,
    bool Unique,
    IReadOnlyList<string> Columns);

public sealed record ForeignKeyInfo(
    string ConstraintName,
    string Column,
    string ReferencedTable,
    string ReferencedColumn);

public sealed record TableDescription(
    string Name,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<IndexInfo> Indexes,
    IReadOnlyList<ForeignKeyInfo> ForeignKeys);

public sealed record QueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows);

public sealed record ExecutionResult(
    int? AffectedRows,
    string? InsertId);

public sealed record AnalyzeResult(
    string Raw,
    bool Executed,
    bool TimedOut = false);
