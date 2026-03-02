namespace Lash.Formatter.Core;

internal static class FormatterEngine
{
    public static string Format(string source, FormatterOptions options)
    {
        var normalized = source.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var output = new List<string>(lines.Length);

        var keywordStack = new List<string>();
        int bracketDepth = 0;
        bool previousBlank = false;

        foreach (var rawLine in lines)
        {
            var trimmedRight = rawLine.TrimEnd();
            var trimmed = trimmedRight.Trim();

            if (string.IsNullOrEmpty(trimmed))
            {
                if (!options.CollapseConsecutiveBlankLines)
                {
                    output.Add(string.Empty);
                    previousBlank = true;
                    continue;
                }

                if (!previousBlank && output.Count > 0)
                {
                    output.Add(string.Empty);
                    previousBlank = true;
                }
                continue;
            }

            var line = SpacingRules.Normalize(trimmed);
            line = SignatureRules.NormalizeFunctionDeclarationSpacing(line);
            var keyword = IndentationRules.GetLeadingKeyword(line);

            if (keyword == "case" && keywordStack.Count > 0 && keywordStack[^1] == "case")
                keywordStack.RemoveAt(keywordStack.Count - 1);

            if (keyword == "end")
            {
                if (keywordStack.Count > 0 && keywordStack[^1] == "case")
                    keywordStack.RemoveAt(keywordStack.Count - 1);
                if (keywordStack.Count > 0)
                    keywordStack.RemoveAt(keywordStack.Count - 1);
            }

            var leadingBracketClosers = CountLeadingBracketClosers(line);
            var effectiveBracketDepth = Math.Max(0, bracketDepth - leadingBracketClosers);
            var indentLevel = keywordStack.Count + effectiveBracketDepth;
            if (keyword is "elif" or "else")
                indentLevel = Math.Max(0, indentLevel - 1);

            var prefix = new string(' ', indentLevel * options.SpacesPerIndent);
            var wrapped = WrappingRules.WrapArgumentsIfNeeded(line, prefix, options);
            output.AddRange(wrapped);
            previousBlank = false;

            if (keyword is not null)
            {
                if (IndentationRules.IsIndentOpeningKeyword(keyword))
                    keywordStack.Add(keyword);
                else if (keyword == "case")
                    keywordStack.Add("case");
            }

            bracketDepth = Math.Max(0, bracketDepth + CountBracketDelta(line));
        }

        var normalizedLayout = LayoutRules.NormalizeTopLevelLayout(output);
        var result = string.Join('\n', normalizedLayout);
        return options.EnsureTrailingNewline ? result + '\n' : result;
    }

    private static int CountLeadingBracketClosers(string line)
    {
        var trimmedStart = line.TrimStart();
        if (trimmedStart.StartsWith("]]", StringComparison.Ordinal))
            return 0;

        int closers = 0;
        for (int i = 0; i < trimmedStart.Length; i++)
        {
            if (trimmedStart[i] != ']')
                break;
            closers++;
        }

        return closers;
    }

    private static int CountBracketDelta(string line)
    {
        int delta = 0;
        bool inSingleString = false;
        bool inDoubleString = false;
        bool escaped = false;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            char? next = i + 1 < line.Length ? line[i + 1] : null;

            if (!inSingleString && !inDoubleString && ch == '/' && next == '/')
                break;

            if (!inSingleString && ch == '"' && !escaped)
            {
                inDoubleString = !inDoubleString;
                escaped = false;
                continue;
            }

            if (!inDoubleString && ch == '\'')
            {
                inSingleString = !inSingleString;
                escaped = false;
                continue;
            }

            if (inDoubleString)
            {
                escaped = ch == '\\' && !escaped;
                continue;
            }

            escaped = false;

            if (inSingleString)
                continue;

            if (ch == '[')
            {
                if (next == '[')
                {
                    i++;
                    continue;
                }

                delta++;
                continue;
            }

            if (ch == ']')
            {
                if (next == ']')
                {
                    i++;
                    continue;
                }

                delta--;
            }
        }

        return delta;
    }
}
