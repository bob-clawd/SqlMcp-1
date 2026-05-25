using SqlMcp.Tools.Drivers.Dialects;

namespace SqlMcp.Tools.Drivers;

public static class DatabaseDriverFactory
{
    public static IDatabaseDriver Create(string connectionUri, bool ssl)
    {
        if (string.IsNullOrWhiteSpace(connectionUri))
            throw new ArgumentException("Connection URI must not be empty.", nameof(connectionUri));

        if (connectionUri.StartsWith("mysql://", StringComparison.OrdinalIgnoreCase) ||
            connectionUri.StartsWith("mysql2://", StringComparison.OrdinalIgnoreCase))
            return MySqlDatabaseDriver.Create(connectionUri, ssl);

        if (connectionUri.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            connectionUri.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return PostgresDatabaseDriver.Create(connectionUri, ssl);

        if (connectionUri.StartsWith("sqlite:", StringComparison.OrdinalIgnoreCase) ||
            connectionUri.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
            HasSqliteExtension(connectionUri))
            return SqliteDatabaseDriver.Create(connectionUri);

        if (connectionUri.StartsWith("mssql://", StringComparison.OrdinalIgnoreCase) ||
            connectionUri.StartsWith("sqlserver://", StringComparison.OrdinalIgnoreCase))
            return MsSqlDatabaseDriver.Create(connectionUri, ssl);

        if (connectionUri.StartsWith("oracle://", StringComparison.OrdinalIgnoreCase))
            return OracleDatabaseDriver.Create(connectionUri, ssl);

        throw new ArgumentException(
            "Unsupported database URI. Supported schemes: mysql://, postgres:// (postgresql://), mssql:// (sqlserver://), oracle://, sqlite: (file:) or a *.db/*.sqlite/*.sqlite3 path.",
            nameof(connectionUri));
    }

    private static bool HasSqliteExtension(string value) =>
        value.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase);
}
