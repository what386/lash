namespace Lash.Formatter.Rules;

internal static class SignatureRules
{
    public static string NormalizeFunctionDeclarationSpacing(string line)
    {
        if (!IsFunctionDeclarationStart(line))
            return line;

        int openParen = line.IndexOf('(');
        if (openParen < 0)
            return line;

        int closeParen = FindMatchingParen(line, openParen);
        if (closeParen < 0)
            return line;

        var before = line[..openParen].TrimEnd();
        var inside = line[(openParen + 1)..closeParen].Trim();
        var after = line[(closeParen + 1)..].TrimStart();

        var normalizedInside = NormalizeParameterList(inside);
        var rebuilt = $"{before}({normalizedInside})";
        if (after.Length > 0)
        {
            if (after.StartsWith(":", StringComparison.Ordinal))
                rebuilt += after;
            else
                rebuilt += " " + after;
        }

        return rebuilt;
    }

    private static bool IsFunctionDeclarationStart(string line)
    {
        return line.StartsWith("fn ", StringComparison.Ordinal);
    }

    private static int FindMatchingParen(string line, int openParen)
    {
        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = openParen; i < line.Length; i++)
        {
            char ch = line[i];

            if (ch == '"' && !escaped)
            {
                inString = !inString;
            }
            else if (!inString)
            {
                if (ch == '(')
                    depth++;
                else if (ch == ')')
                    depth--;

                if (depth == 0)
                    return i;
            }

            escaped = ch == '\\' && !escaped;
        }

        return -1;
    }

    private static string NormalizeParameterList(string inside)
    {
        if (inside.Length == 0)
            return string.Empty;

        var parts = inside.Split(',', StringSplitOptions.TrimEntries);
        return string.Join(", ", parts);
    }
}
