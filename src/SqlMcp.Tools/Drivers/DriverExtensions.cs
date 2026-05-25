using System.Data.Common;
using SqlMcp.Tools.Models;

namespace SqlMcp.Tools.Drivers;

internal static class DriverExtensions
{
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
