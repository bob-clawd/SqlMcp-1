namespace SqlMcp.Tools.Drivers.Postgres;

internal static class PlanInsights
{
    public static IReadOnlyList<string> FromText(string plan)
    {
        if (string.IsNullOrWhiteSpace(plan))
            return Array.Empty<string>();

        var insights = new List<string>();
        foreach (var line in plan.Split('\n'))
        {
            var m = System.Text.RegularExpressions.Regex.Match(line, @"Seq Scan on (\w+)");
            if (m.Success)
                insights.Add($"⚠ Sequential scan on `{m.Groups[1].Value}`");

            if (line.Contains("external merge", StringComparison.OrdinalIgnoreCase))
                insights.Add("⚠ Sort spilled to disk — consider increasing work_mem");
        }

        return insights.Distinct().ToArray();
    }
}