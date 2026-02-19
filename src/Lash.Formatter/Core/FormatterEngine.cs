namespace Lash.Formatter.Core;

internal static class FormatterEngine
{
    public static string Format(string source, FormatterOptions options)
    {
        var normalized = source.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var output = new List<string>(lines.Length);

        var blockStack = new List<string>();
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

            if (keyword == "case" && blockStack.Count > 0 && blockStack[^1] == "case")
                blockStack.RemoveAt(blockStack.Count - 1);

            if (keyword == "end")
            {
                if (blockStack.Count > 0 && blockStack[^1] == "case")
                    blockStack.RemoveAt(blockStack.Count - 1);
                if (blockStack.Count > 0)
                    blockStack.RemoveAt(blockStack.Count - 1);
            }

            var indentLevel = blockStack.Count;
            if (keyword is "elif" or "else")
                indentLevel = Math.Max(0, indentLevel - 1);

            var prefix = new string(' ', indentLevel * options.SpacesPerIndent);
            var wrapped = WrappingRules.WrapArgumentsIfNeeded(line, prefix, options);
            output.AddRange(wrapped);
            previousBlank = false;

            if (keyword is not null)
            {
                if (IndentationRules.IsIndentOpeningKeyword(keyword))
                    blockStack.Add(keyword);
                else if (keyword == "case")
                    blockStack.Add("case");
            }
        }

        var normalizedLayout = LayoutRules.NormalizeTopLevelLayout(output);
        var result = string.Join('\n', normalizedLayout);
        return options.EnsureTrailingNewline ? result + '\n' : result;
    }
}
