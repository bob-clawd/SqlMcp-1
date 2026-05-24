namespace SqlMcp.Tools.Drivers.Oracle;

internal static class PlanInsights
{
    public static IReadOnlyList<string> FromText(string plan)
    {
        if (string.IsNullOrWhiteSpace(plan))
            return [];

        var insights = new List<string>();

        foreach (var line in plan.Split('\n'))
        {
            if (line.Contains("TABLE ACCESS FULL", StringComparison.OrdinalIgnoreCase))
                insights.Add("⚠ Full table scan detected — consider adding indexes");

            if (line.Contains("SORT (ORDER BY)", StringComparison.OrdinalIgnoreCase))
                insights.Add("⚠ Sort operation — consider an index on ORDER BY columns");

            if (line.Contains("HASH JOIN", StringComparison.OrdinalIgnoreCase))
                insights.Add("⚠ Hash join detected — may be expensive on large tables");
        }

        return insights.Distinct().ToArray();
    }
}
