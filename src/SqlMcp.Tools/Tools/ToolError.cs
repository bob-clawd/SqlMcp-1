using SqlMcp.Tools.Models;

namespace SqlMcp.Tools.Tools;

public sealed record ToolError(
    string Message,
    IReadOnlyDictionary<string, string>? Details = null)
{
    internal static ToolError From(ErrorInfo error)
        => new(error.Message, error.Details);
}
