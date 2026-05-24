namespace SqlMcp.Tools.Drivers.MySql;

internal static class PlanInsights
{
    public static IReadOnlyList<string> FromText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var insights = new List<string>();

        if (raw.Contains("Using filesort", StringComparison.OrdinalIgnoreCase))
            insights.Add("⚠ Filesort detected — consider an index on ORDER BY columns");

        if (raw.Contains("Using temporary", StringComparison.OrdinalIgnoreCase))
            insights.Add("⚠ Temporary table used — often GROUP BY/DISTINCT without suitable index");

        if (raw.Contains("Table scan on", StringComparison.OrdinalIgnoreCase) || raw.Contains("type\tALL", StringComparison.OrdinalIgnoreCase))
            insights.Add("⚠ Full table scan detected");

        return insights;
    }
}