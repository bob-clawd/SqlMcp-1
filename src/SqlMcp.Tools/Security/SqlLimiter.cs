using System.Text.RegularExpressions;
using SqlMcp.Tools.Models;

namespace SqlMcp.Tools.Security;

public static partial class SqlLimiter
{
    [GeneratedRegex(@"\bLIMIT\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LimitRegex();

    [GeneratedRegex(@"\bTOP\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TopClauseRegex();

    [GeneratedRegex(@"\bFETCH\s+FIRST\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex FetchFirstRegex();

    public static string ApplyLimitIfMissing(string sql, int maxRows, DbDialect dialect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        var capped = Math.Clamp(maxRows, 1, 10_000);

        if (dialect == DbDialect.Mssql)
            return ApplyTopLimit(sql, capped);

        if (dialect == DbDialect.Oracle)
            return ApplyFetchFirstLimit(sql, capped);

        if (LimitRegex().IsMatch(sql))
            return sql;

        var trimmed = sql.TrimEnd();
        if (trimmed.EndsWith(';'))
            trimmed = trimmed[..^1];

        return trimmed + $" LIMIT {capped}";
    }

    private static string ApplyTopLimit(string sql, int maxRows)
    {
        if (TopClauseRegex().IsMatch(sql))
            return sql;

        var selectIndex = sql.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase);
        if (selectIndex < 0)
            return sql;

        return sql.Insert(selectIndex + 6, $" TOP({maxRows})");
    }

    private static string ApplyFetchFirstLimit(string sql, int maxRows)
    {
        if (FetchFirstRegex().IsMatch(sql))
            return sql;

        var trimmed = sql.TrimEnd();
        if (trimmed.EndsWith(';'))
            trimmed = trimmed[..^1];

        return trimmed + $" FETCH FIRST {maxRows} ROWS ONLY";
    }
}
