namespace Lash.Compiler.Preprocessor;

using Lash.Compiler.Diagnostics;

internal sealed class Preprocessor
{
    public string Process(string source, DiagnosticBag diagnostics)
    {
        _ = diagnostics;
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var withoutShebang = StripLeadingShebang(normalized);
        return StripComments(withoutShebang);
    }

    private static string StripLeadingShebang(string source)
    {
        if (!source.StartsWith("#!", StringComparison.Ordinal))
            return source;

        var newlineIndex = source.IndexOf('\n');
        if (newlineIndex < 0)
            return string.Empty;

        // Keep the newline so diagnostics preserve original line numbers.
        return source[newlineIndex..];
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

    private static bool IsEscaped(string source, int index)
    {
        var backslashes = 0;
        for (int i = index - 1; i >= 0 && source[i] == '\\'; i--)
            backslashes++;
        return (backslashes % 2) != 0;
    }
}
