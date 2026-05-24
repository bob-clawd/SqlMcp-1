using System.Text.RegularExpressions;
using SqlMcp.Tools.Models;

namespace SqlMcp.Tools.Security;

public static partial class SqlLimiter
{
    [GeneratedRegex(@"\bLIMIT\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LimitRegex();

    public static string ApplyLimitIfMissing(string sql, int maxRows, DbDialect dialect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        var capped = Math.Clamp(maxRows, 1, 10_000);

        if (LimitRegex().IsMatch(sql))
            return sql;

        var trimmed = sql.TrimEnd();
        if (trimmed.EndsWith(';'))
            trimmed = trimmed[..^1];

        return trimmed + $" LIMIT {capped}";
    }
}
