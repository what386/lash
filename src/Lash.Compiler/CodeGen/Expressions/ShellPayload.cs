namespace Lash.Compiler.CodeGen;

using System.Text;

internal sealed partial class ExpressionGenerator
{
    private static string RenderInterpolatedShellPayload(string template)
    {
        var builder = new StringBuilder();
        var quoteState = new ShellQuoteState();
        int cursor = 0;

        while (cursor < template.Length)
        {
            var openBrace = FindNextUnescaped(template, '{', cursor);
            if (openBrace < 0)
            {
                var tail = template[cursor..];
                builder.Append(UnescapeShellPayloadText(tail));
                break;
            }

            var literalSegment = template[cursor..openBrace];
            builder.Append(UnescapeShellPayloadText(literalSegment));
            UpdateShellQuoteState(literalSegment, ref quoteState);

            var closeBrace = FindNextUnescaped(template, '}', openBrace + 1);
            if (closeBrace < 0)
            {
                var rawRemainder = template[openBrace..];
                builder.Append(UnescapeShellPayloadText(rawRemainder));
                break;
            }

            var placeholder = template[(openBrace + 1)..closeBrace].Trim();
            if (TryGetIdentifierPath(placeholder, out var path))
            {
                builder.Append(quoteState.InSingleQuote
                    ? "'\"${" + path + "}\"'"
                    : "${" + path + "}");
            }
            else
            {
                var rawPlaceholder = template[openBrace..(closeBrace + 1)];
                builder.Append(UnescapeShellPayloadText(rawPlaceholder));
                UpdateShellQuoteState(rawPlaceholder, ref quoteState);
            }

            cursor = closeBrace + 1;
        }

        return builder.ToString();
    }

    private struct ShellQuoteState
    {
        public bool InSingleQuote;
        public bool InDoubleQuote;
        public bool EscapeNext;
    }

    private static void UpdateShellQuoteState(string text, ref ShellQuoteState state)
    {
        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (state.InSingleQuote)
            {
                if (ch == '\'')
                    state.InSingleQuote = false;
                continue;
            }

            if (state.EscapeNext)
            {
                state.EscapeNext = false;
                continue;
            }

            if (ch == '\\')
            {
                state.EscapeNext = true;
                continue;
            }

            if (state.InDoubleQuote)
            {
                if (ch == '"')
                    state.InDoubleQuote = false;
                continue;
            }

            if (ch == '\'')
            {
                state.InSingleQuote = true;
                continue;
            }

            if (ch == '"')
                state.InDoubleQuote = true;
        }
    }

    private static string UnescapeShellPayloadText(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
            return value;

        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch != '\\' || i + 1 >= value.Length)
            {
                builder.Append(ch);
                continue;
            }

            var next = value[i + 1];
            i++;

            builder.Append(next switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '"' => '"',
                '\\' => '\\',
                '$' => '$',
                '`' => '`',
                _ => next
            });
        }

        return builder.ToString();
    }
}
