namespace Lash.Formatter.Rules;

internal static class LayoutRules
{
    public static List<string> NormalizeTopLevelLayout(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
            return new List<string>();

        var trimmed = TrimEdgeBlankLines(lines);
        var result = new List<string>(trimmed.Count);
        int? previousNonBlank = null;

        for (int i = 0; i < trimmed.Count; i++)
        {
            var line = trimmed[i];
            var currentTrimmed = line.Trim();

            if (currentTrimmed.Length == 0)
            {
                if (result.Count > 0 && result[^1].Length != 0)
                    result.Add(string.Empty);
                continue;
            }

            if (previousNonBlank.HasValue)
            {
                var previousRaw = trimmed[previousNonBlank.Value];
                var previousLine = previousRaw.Trim();
                bool hadBlankBetween = HasBlankBetween(trimmed, previousNonBlank.Value, i);
                if (NeedsTopLevelSeparator(previousRaw, previousLine, line, currentTrimmed) && !hadBlankBetween)
                    result.Add(string.Empty);
            }

            result.Add(line);
            previousNonBlank = i;
        }

        return result;
    }

    private static bool NeedsTopLevelSeparator(
        string previousRaw,
        string previous,
        string currentRaw,
        string current)
    {
        if (!IsTopLevelLine(previousRaw) || !IsTopLevelLine(currentRaw))
            return false;

        if (!IsTopLevelDeclarationStart(current))
            return false;

        if (IsTopLevelDeclarationStart(previous) || IsTopLevelDeclarationStart(current))
            return true;

        return false;
    }

    private static bool IsTopLevelLine(string line)
    {
        return line.Length > 0 && !char.IsWhiteSpace(line[0]);
    }

    private static bool IsTopLevelDeclarationStart(string line)
    {
        return IsTopLevelMajorDeclaration(line);
    }

    private static bool IsTopLevelMajorDeclaration(string line)
    {
        return StartsWithKeyword(line, "fn")
            || StartsWithKeyword(line, "enum");
    }

    private static bool StartsWithKeyword(string line, string keyword)
    {
        if (!line.StartsWith(keyword, StringComparison.Ordinal))
            return false;

        if (line.Length == keyword.Length)
            return true;

        return char.IsWhiteSpace(line[keyword.Length]);
    }

    private static bool HasBlankBetween(IReadOnlyList<string> lines, int previousNonBlank, int current)
    {
        for (int i = previousNonBlank + 1; i < current; i++)
        {
            if (lines[i].Trim().Length == 0)
                return true;
        }

        return false;
    }

    private static List<string> TrimEdgeBlankLines(IReadOnlyList<string> lines)
    {
        int start = 0;
        while (start < lines.Count && lines[start].Trim().Length == 0)
            start++;

        int end = lines.Count - 1;
        while (end >= start && lines[end].Trim().Length == 0)
            end--;

        var result = new List<string>(Math.Max(0, end - start + 1));
        for (int i = start; i <= end; i++)
            result.Add(lines[i]);

        return result;
    }
}
