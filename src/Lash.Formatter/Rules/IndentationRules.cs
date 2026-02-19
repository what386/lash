namespace Lash.Formatter.Rules;

internal static class IndentationRules
{
    public static string? GetLeadingKeyword(string line)
    {
        if (StartsWithKeyword(line, "fn")) return "fn";
        if (StartsWithKeyword(line, "if")) return "if";
        if (StartsWithKeyword(line, "elif")) return "elif";
        if (StartsWithKeyword(line, "else")) return "else";
        if (StartsWithKeyword(line, "for")) return "for";
        if (StartsWithKeyword(line, "while")) return "while";
        if (StartsWithKeyword(line, "switch")) return "switch";
        if (StartsWithKeyword(line, "case")) return "case";
        if (StartsWithKeyword(line, "enum")) return "enum";
        if (StartsWithKeyword(line, "subshell")) return "subshell";
        if (StartsWithKeyword(line, "end")) return "end";
        return null;
    }

    public static bool IsIndentOpeningKeyword(string keyword)
    {
        return keyword is "fn" or "if" or "for" or "while" or "switch" or "enum" or "subshell";
    }

    private static bool StartsWithKeyword(string line, string keyword)
    {
        if (!line.StartsWith(keyword, StringComparison.Ordinal))
            return false;

        if (line.Length == keyword.Length)
            return true;

        return char.IsWhiteSpace(line[keyword.Length]);
    }
}
