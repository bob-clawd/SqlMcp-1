namespace SqlMcp.Tools.Security;

public sealed class SqlStatementClassifier
{
    public SqlStatementType Classify(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        var tokens = SqlTokenizer.TokenizeTopLevel(sql);
        if (tokens.Count == 0) return SqlStatementType.Unknown;

        var first = tokens[0].ToUpperInvariant();

        if (first == "WITH")
        {
            // Best-effort: detect the first real statement keyword after the CTE.
            for (var i = 1; i < tokens.Count; i++)
            {
                var t = tokens[i].ToUpperInvariant();
                if (t is "SELECT" or "INSERT" or "UPDATE" or "DELETE")
                    return MapSimple(t, tokens, i);
            }
            return SqlStatementType.Unknown;
        }

        return MapSimple(first, tokens, 0);
    }

    public bool HasMultipleStatements(string sql) => SqlTokenizer.HasMultipleStatements(sql);

    private static SqlStatementType MapSimple(string first, IReadOnlyList<string> tokens, int index)
    {
        if (first == "DROP")
            return SqlStatementType.Drop;

        return first switch
        {
            "SELECT" => SqlStatementType.Select,
            "SHOW" => SqlStatementType.Show,
            "DESCRIBE" => SqlStatementType.Describe,
            "DESC" => SqlStatementType.Describe,
            "EXPLAIN" => SqlStatementType.Explain,
            "INSERT" => SqlStatementType.Insert,
            "UPDATE" => SqlStatementType.Update,
            "DELETE" => SqlStatementType.Delete,
            "ALTER" => SqlStatementType.Alter,
            "CREATE" => SqlStatementType.Create,
            "TRUNCATE" => SqlStatementType.Truncate,
            _ => SqlStatementType.Unknown
        };
    }
}