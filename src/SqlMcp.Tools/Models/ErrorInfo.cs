namespace SqlMcp.Tools.Models;

internal sealed record ErrorInfo(
    string Message,
    IReadOnlyDictionary<string, string>? Details = null);
