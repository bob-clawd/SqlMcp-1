using System.Text;

namespace SqlMcp.Tools.Security;

internal static class SqlTokenizer
{
    public static IReadOnlyList<string> TokenizeTopLevel(string sql)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();

        var state = new ScanState();
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (state.InLineComment)
            {
                if (c == '\n') state.InLineComment = false;
                continue;
            }

            if (state.InBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    state.InBlockComment = false;
                    i++;
                }
                continue;
            }

            if (state.InDollarQuote)
            {
                if (c == '$' && state.DollarTag.Length > 0)
                {
                    // Try to match $tag$
                    if (IsAt(sql, i, state.DollarTag))
                    {
                        i += state.DollarTag.Length - 1;
                        state.InDollarQuote = false;
                        state.DollarTag = string.Empty;
                    }
                }
                continue;
            }

            if (state.InSingleQuote)
            {
                if (c == '\'' && next == '\'')
                {
                    i++; // escaped quote
                    continue;
                }
                if (c == '\'') state.InSingleQuote = false;
                continue;
            }

            if (state.InDoubleQuote)
            {
                if (c == '"') state.InDoubleQuote = false;
                continue;
            }

            if (state.InBacktick)
            {
                if (c == '`') state.InBacktick = false;
                continue;
            }

            // comments
            if (c == '-' && next == '-')
            {
                state.FlushToken(sb, tokens);
                state.InLineComment = true;
                i++;
                continue;
            }
            if (c == '#')
            {
                state.FlushToken(sb, tokens);
                state.InLineComment = true;
                continue;
            }
            if (c == '/' && next == '*')
            {
                state.FlushToken(sb, tokens);
                state.InBlockComment = true;
                i++;
                continue;
            }

            // strings / identifiers
            if (c == '\'')
            {
                state.FlushToken(sb, tokens);
                state.InSingleQuote = true;
                continue;
            }
            if (c == '"')
            {
                state.FlushToken(sb, tokens);
                state.InDoubleQuote = true;
                continue;
            }
            if (c == '`')
            {
                state.FlushToken(sb, tokens);
                state.InBacktick = true;
                continue;
            }

            // postgres dollar-quote: $tag$...$tag$
            if (c == '$')
            {
                var tag = TryReadDollarTag(sql, i);
                if (tag is not null)
                {
                    state.FlushToken(sb, tokens);
                    state.InDollarQuote = true;
                    state.DollarTag = tag;
                    i += tag.Length - 1;
                    continue;
                }
            }

            if (char.IsWhiteSpace(c) || c is ';' or '(' or ')' or ',')
            {
                state.FlushToken(sb, tokens);
                continue;
            }

            sb.Append(c);
        }

        state.FlushToken(sb, tokens);
        return tokens;
    }

    public static bool HasMultipleStatements(string sql)
    {
        // Detect a ';' outside comments/strings that is not trailing.
        var state = new ScanState();
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (state.ConsumeInside(ref i, sql, c, next))
                continue;

            if (c == ';')
            {
                // if anything non-whitespace remains after this semicolon => multiple statements
                for (var j = i + 1; j < sql.Length; j++)
                {
                    if (!char.IsWhiteSpace(sql[j]))
                        return true;
                }
                return false;
            }
        }

        return false;
    }

    private static string? TryReadDollarTag(string sql, int start)
    {
        // $tag$ where tag is [A-Za-z0-9_]* (empty allowed as $$)
        if (sql[start] != '$') return null;
        var end = start + 1;
        while (end < sql.Length)
        {
            var c = sql[end];
            if (c == '$')
            {
                var tag = sql[start..(end + 1)];
                return tag;
            }
            if (!(char.IsLetterOrDigit(c) || c == '_')) return null;
            end++;
        }
        return null;
    }

    private static bool IsAt(string sql, int index, string needle)
    {
        if (index + needle.Length > sql.Length) return false;
        for (var i = 0; i < needle.Length; i++)
            if (sql[index + i] != needle[i]) return false;
        return true;
    }

    private sealed class ScanState
    {
        public bool InLineComment;
        public bool InBlockComment;
        public bool InSingleQuote;
        public bool InDoubleQuote;
        public bool InBacktick;
        public bool InDollarQuote;
        public string DollarTag = string.Empty;

        public void FlushToken(StringBuilder sb, List<string> tokens)
        {
            if (sb.Length == 0) return;
            tokens.Add(sb.ToString());
            sb.Clear();
        }

        public bool ConsumeInside(ref int index, string sql, char c, char next)
        {
            if (InLineComment)
            {
                if (c == '\n') InLineComment = false;
                return true;
            }

            if (InBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    InBlockComment = false;
                    index++;
                }
                return true;
            }

            if (InDollarQuote)
            {
                if (c == '$' && DollarTag.Length > 0 && IsAt(sql, index, DollarTag))
                {
                    index += DollarTag.Length - 1;
                    InDollarQuote = false;
                    DollarTag = string.Empty;
                    return true;
                }
                return true;
            }

            if (InSingleQuote)
            {
                if (c == '\'' && next == '\'')
                {
                    index++;
                    return true;
                }
                if (c == '\'') InSingleQuote = false;
                return true;
            }

            if (InDoubleQuote)
            {
                if (c == '"') InDoubleQuote = false;
                return true;
            }

            if (InBacktick)
            {
                if (c == '`') InBacktick = false;
                return true;
            }

            // entering states
            if (c == '-' && next == '-') { InLineComment = true; index++; return true; }
            if (c == '#') { InLineComment = true; return true; }
            if (c == '/' && next == '*') { InBlockComment = true; index++; return true; }
            if (c == '\'') { InSingleQuote = true; return true; }
            if (c == '"') { InDoubleQuote = true; return true; }
            if (c == '`') { InBacktick = true; return true; }
            if (c == '$')
            {
                var tag = TryReadDollarTag(sql, index);
                if (tag is not null)
                {
                    InDollarQuote = true;
                    DollarTag = tag;
                    index += tag.Length - 1;
                    return true;
                }
            }

            return false;
        }
    }
}
