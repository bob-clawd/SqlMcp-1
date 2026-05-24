namespace SqlMcp.Tools.Drivers.Sqlite;

internal static class PlanInsights
{
    public static IReadOnlyList<string> FromText(string plan)
    {
        if (string.IsNullOrWhiteSpace(plan))
            return [];

        var insights = new List<string>();

        foreach (var line in plan.Split('\n'))
        {
            var scanMatch = System.Text.RegularExpressions.Regex.Match(line, @"^SCAN (\w+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (scanMatch.Success)
                insights.Add($"⚠ Full table scan on `{scanMatch.Groups[1].Value}`");

            if (line.Contains("USE TEMP B-TREE FOR ORDER BY", StringComparison.OrdinalIgnoreCase))
                insights.Add("⚠ ORDER BY uses temporary B-tree — consider an index on the sort columns");
        }

        return insights.Distinct().ToArray();
    }
}