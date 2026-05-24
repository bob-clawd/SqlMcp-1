namespace SqlMcp.Tools.Security;

public static class SqlPermissionChecker
{
    public static (bool Allowed, string? Reason) Check(SqlStatementType type, SqlPermissionOptions permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        if (type is SqlStatementType.Select or SqlStatementType.Show or SqlStatementType.Describe or SqlStatementType.Explain)
            return (true, null);

        if (type == SqlStatementType.Unknown)
        {
            return (false,
                "Unrecognized or disallowed SQL statement. Only SELECT/SHOW/DESCRIBE/EXPLAIN are allowed by default. Write/DDL operations require explicit opt-in via flags.");
        }

        if (type == SqlStatementType.DropDatabase)
        {
            return permissions.AllowDropDatabase
                ? (true, null)
                : (false, "DROP DATABASE is prohibited. Use --allow-drop-database to explicitly enable it.");
        }

        if (type is SqlStatementType.Insert or SqlStatementType.Update)
        {
            return permissions.AllowWrite
                ? (true, null)
                : (false, "Write operations are disabled. Use --allow-write to enable INSERT and UPDATE.");
        }

        if (type == SqlStatementType.Delete)
        {
            return permissions.AllowDelete
                ? (true, null)
                : (false, "DELETE is disabled. Use --allow-delete to enable it.");
        }

        if (type is SqlStatementType.Alter or SqlStatementType.Create or SqlStatementType.Drop or SqlStatementType.Truncate)
        {
            return permissions.AllowDdl
                ? (true, null)
                : (false, "DDL operations are disabled. Use --allow-ddl to enable ALTER/CREATE/DROP/TRUNCATE.");
        }

        return (false, "Statement type not permitted.");
    }
}
