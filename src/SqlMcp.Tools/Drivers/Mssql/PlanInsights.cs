namespace SqlMcp.Tools.Drivers.Mssql;

internal static class PlanInsights
{
    public static IReadOnlyList<string> FromText(string plan)
    {
        if (string.IsNullOrWhiteSpace(plan))
            return [];

        var insights = new List<string>();

        foreach (var line in plan.Split('\n'))
        {
            if (line.Contains("Table Scan", StringComparison.OrdinalIgnoreCase))
                insights.Add("⚠ Full table scan detected — consider adding indexes");

            if (line.Contains("Clustered Index Scan", StringComparison.OrdinalIgnoreCase))
                insights.Add("⚠ Clustered index scan — may be expensive on large tables");

            if (line.Contains("RID Lookup", StringComparison.OrdinalIgnoreCase))
                insights.Add("⚠ RID lookup on heap — consider adding a clustered index");

            if (line.Contains("Key Lookup", StringComparison.OrdinalIgnoreCase))
                insights.Add("⚠ Key lookup — consider a covering index");

            if (line.Contains("Sort", StringComparison.OrdinalIgnoreCase) &&
                !line.Contains("Sort Order", StringComparison.OrdinalIgnoreCase))
                insights.Add("⚠ Sort operator — consider an index on ORDER BY columns");
        }

        return insights.Distinct().ToArray();
    }
}
