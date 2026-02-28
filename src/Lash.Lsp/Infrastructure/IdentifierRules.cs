namespace Lash.Lsp.Infrastructure;

internal static class IdentifierRules
{
    private static readonly HashSet<string> ReservedWords = new(StringComparer.Ordinal)
    {
        "if", "elif", "else", "end",
        "fn", "for", "in", "step", "while",
        "switch", "case",
        "let", "const", "global",
        "return", "break", "continue",
        "shift", "subshell", "wait", "into", "jobs",
        "sh", "test", "trap", "untrap", "enum",
        "true", "false"
    };

    public static bool IsValidIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!IsIdentifierStart(value[0]))
            return false;

        for (var i = 1; i < value.Length; i++)
        {
            if (!IsIdentifierPart(value[i]))
                return false;
        }

        return true;
    }

    public static bool IsReservedKeyword(string value)
    {
        return ReservedWords.Contains(value);
    }

    private static bool IsIdentifierStart(char c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
    }

    private static bool IsIdentifierPart(char c)
    {
        return IsIdentifierStart(c) || (c >= '0' && c <= '9');
    }
}
