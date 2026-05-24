namespace SqlMcp.Tools.Security;

public interface ISqlStatementClassifier
{
    SqlStatementType Classify(string sql);
    bool HasMultipleStatements(string sql);
}
