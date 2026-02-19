namespace Lash.Compiler.Preprocessing;

internal static class Normalizer
{
    public static string Normalize(string source)
    {
        source = NormalizeLineEndings(source);
        source = StripShebang(source);
        source = StripComments(source);

        return source;
    }

    private static string NormalizeLineEndings(string source)
    {
        return source.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    public static string StripTrailingLineComment(string text)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escaped = false;

        for (int i = 0; i < text.Length - 1; i++)
        {
            var c = text[i];
            var next = text[i + 1];

            if (c == '"' && !inSingleQuote && !escaped)
                inDoubleQuote = !inDoubleQuote;
            else if (c == '\'' && !inDoubleQuote && !escaped)
                inSingleQuote = !inSingleQuote;

            if (!inSingleQuote && !inDoubleQuote && c == '/' && next == '/')
                return text[..i];

            escaped = !escaped && c == '\\';
            if (c != '\\')
                escaped = false;
        }

        return text;
    }

    private static string StripComments(string source)
    {
        var output = new System.Text.StringBuilder(source.Length);
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (int i = 0; i < source.Length; i++)
        {
            var c = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\n')
                {
                    inLineComment = false;
                    output.Append(c);
                }

                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                    continue;
                }

                if (c == '\n')
                    output.Append(c);

                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && c == '/' && next == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && c == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (c == '"' && !inSingleQuote && !IsEscaped(source, i))
                inDoubleQuote = !inDoubleQuote;
            else if (c == '\'' && !inDoubleQuote && !IsEscaped(source, i))
                inSingleQuote = !inSingleQuote;

            output.Append(c);
        }

        return output.ToString();
    }

    private static string StripShebang(string source)
    {
        if (!source.StartsWith("#!", StringComparison.Ordinal))
            return source;

        var newlineIndex = source.IndexOf('\n');
        if (newlineIndex < 0)
            return string.Empty;

        // Keep the newline so diagnostics preserve original line numbers.
        return source[newlineIndex..];
    }

    private static bool IsEscaped(string source, int index)
    {
        var backslashes = 0;
        for (int i = index - 1; i >= 0 && source[i] == '\\'; i--)
            backslashes++;
        return (backslashes % 2) != 0;
    }
}
