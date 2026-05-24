namespace SqlMcp.Tools.Security;

public enum SqlStatementType
{
    Unknown = 0,
    Select,
    Show,
    Describe,
    Explain,
    Insert,
    Update,
    Delete,
    Alter,
    Create,
    Drop,
    Truncate,
    DropDatabase
}
