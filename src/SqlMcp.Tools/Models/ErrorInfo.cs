namespace SqlMcp.Tools.Models;

public sealed record ErrorInfo(
    string Message,
    IReadOnlyDictionary<string, string>? Details = null);
