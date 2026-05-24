using System.Text.RegularExpressions;

namespace SqlMcp.Tools.Security;

public static partial class SqlIdentifier
{
    [GeneratedRegex("^[a-zA-Z0-9_]+$", RegexOptions.Compiled)]
    private static partial Regex IdentifierRegex();

    public static bool IsValid(string name) =>
        !string.IsNullOrWhiteSpace(name) && IdentifierRegex().IsMatch(name);
}
