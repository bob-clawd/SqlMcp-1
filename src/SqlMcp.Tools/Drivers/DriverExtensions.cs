using System.Data.Common;
using System.Text.RegularExpressions;
using SqlMcp.Tools.Models;

namespace SqlMcp.Tools.Drivers;

internal static partial class DriverExtensions
{
    [GeneratedRegex("^[a-zA-Z0-9_]+$", RegexOptions.Compiled)]
    private static partial Regex IdentifierRegex();

    public static void GuardIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !IdentifierRegex().IsMatch(name))
            throw new ArgumentException($"Invalid identifier '{name}'.", nameof(name));
    }

    public static async Task<QueryResult> ReadResultAsync(this DbDataReader reader, int maxRows, CancellationToken cancellationToken = default)
    {
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var rows = new List<IReadOnlyList<object?>>(capacity: Math.Min(maxRows, 1000));

        while (rows.Count < maxRows && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new List<object?>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var val = await reader.IsDBNullAsync(i, cancellationToken).ConfigureAwait(false) ? null : reader.GetValue(i);
                row.Add(val);
            }
            rows.Add(row);
        }

        return new QueryResult(columns, rows);
    }
}
