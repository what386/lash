namespace Lash.Formatter.Rules;

internal static class WrappingRules
{
    public static IReadOnlyList<string> WrapArgumentsIfNeeded(string line, string prefix, FormatterOptions options)
    {
        var rendered = prefix + line;
        if (rendered.Length <= options.MaxLineLength)
            return new[] { rendered };

        if (!TryFindCallBounds(line, out var openIndex, out var closeIndex))
            return new[] { rendered };

        var before = line[..openIndex].TrimEnd();
        var inside = line[(openIndex + 1)..closeIndex];
        var after = line[(closeIndex + 1)..].Trim();
        var args = SplitArguments(inside);
        if (args.Count <= 1)
            return new[] { rendered };

        var lines = new List<string>(args.Count + 2)
        {
            $"{prefix}{before}("
        };

        var argPrefix = prefix + new string(' ', options.WrappedArgumentIndent);
        for (int i = 0; i < args.Count; i++)
        {
            var suffix = i == args.Count - 1 ? string.Empty : ",";
            lines.Add($"{argPrefix}{args[i].Trim()}{suffix}");
        }

        lines.Add(string.IsNullOrEmpty(after)
            ? $"{prefix})"
            : $"{prefix}) {after}");

        return lines;
    }

    private static bool TryFindCallBounds(string line, out int openIndex, out int closeIndex)
    {
        openIndex = -1;
        closeIndex = -1;
        bool inString = false;
        bool escaped = false;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"' && !escaped)
                inString = !inString;

            if (inString)
            {
                escaped = ch == '\\' && !escaped;
                continue;
            }

            escaped = false;

            if (ch == '(')
            {
                openIndex = i;
                break;
            }
        }

        if (openIndex < 0)
            return false;

        int depth = 0;
        inString = false;
        escaped = false;
        for (int i = openIndex; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"' && !escaped)
                inString = !inString;

            if (inString)
            {
                escaped = ch == '\\' && !escaped;
                continue;
            }

            escaped = false;

            if (ch == '(') depth++;
            if (ch == ')') depth--;
            if (depth == 0)
            {
                closeIndex = i;
                break;
            }
        }

        return closeIndex > openIndex;
    }

    private static List<string> SplitArguments(string args)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(args))
            return result;

        int start = 0;
        int parenDepth = 0;
        int braceDepth = 0;
        int bracketDepth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = 0; i < args.Length; i++)
        {
            char ch = args[i];

            if (ch == '"' && !escaped)
                inString = !inString;

            if (inString)
            {
                escaped = ch == '\\' && !escaped;
                continue;
            }

            escaped = false;

            switch (ch)
            {
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    parenDepth--;
                    break;
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    braceDepth--;
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    bracketDepth--;
                    break;
                case ',' when parenDepth == 0 && braceDepth == 0 && bracketDepth == 0:
                    result.Add(args[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        if (start < args.Length)
            result.Add(args[start..].Trim());

        return result;
    }
}
