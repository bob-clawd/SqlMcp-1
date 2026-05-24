namespace SqlMcp.Tools.Security;

public sealed class SqlStatementClassifier : ISqlStatementClassifier
{
    public SqlStatementType Classify(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        var tokens = SqlTokenizer.TokenizeTopLevel(sql);
        if (tokens.Count == 0) return SqlStatementType.Unknown;

        static string Up(string s) => s.ToUpperInvariant();

        var first = Up(tokens[0]);

        if (first == "WITH")
        {
            // Best-effort: detect the first real statement keyword after the CTE.
            for (var i = 1; i < tokens.Count; i++)
            {
                var t = Up(tokens[i]);
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
        {
            var second = tokens.Count > index + 1 ? tokens[index + 1].ToUpperInvariant() : string.Empty;
            if (second is "DATABASE" or "SCHEMA")
                return SqlStatementType.DropDatabase;

            return SqlStatementType.Drop;
        }

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
