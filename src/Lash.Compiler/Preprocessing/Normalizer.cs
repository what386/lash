namespace Lash.Compiler.Preprocessing;

internal static class Normalizer
{
    public static string Normalize(string source)
    {
        source = NormalizeLineEndings(source);
        source = StripShebang(source);

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

}
