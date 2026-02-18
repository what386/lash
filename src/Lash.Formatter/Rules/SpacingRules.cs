namespace Lash.Formatter.Rules;

using System.Text;

internal static class SpacingRules
{
    public static string Normalize(string line)
    {
        if (line.StartsWith("//", StringComparison.Ordinal))
            return line;

        // Raw command statements should preserve Bash syntax exactly.
        if (IsRawCommandPassthrough(line))
            return line;

        var sb = new StringBuilder(line.Length + 8);
        bool inString = false;
        bool escaped = false;

        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (inString)
            {
                sb.Append(ch);
                if (escaped)
                {
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (ch == '"')
            {
                inString = true;
                sb.Append(ch);
                continue;
            }

            if (ch == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                if (sb.Length > 0 && sb[^1] != ' ')
                    sb.Append(' ');
                sb.Append(line.AsSpan(i));
                break;
            }

            if (ch == ':' && i + 1 < line.Length && line[i + 1] == ':')
            {
                TrimTrailingSpaces(sb);
                sb.Append("::");
                i++;
                SkipFollowingSpaces(line, ref i);
                continue;
            }

            if (ch == ',' || ch == ':')
            {
                TrimTrailingSpaces(sb);
                sb.Append(ch);
                sb.Append(' ');
                SkipFollowingSpaces(line, ref i);
                continue;
            }

            if (ch == '{')
            {
                EnsureSpaceBeforeBrace(sb);
                sb.Append('{');
                if (i + 1 < line.Length && line[i + 1] != '}')
                    sb.Append(' ');
                SkipFollowingSpaces(line, ref i);
                continue;
            }

            if (ch == '}')
            {
                TrimTrailingSpaces(sb);
                if (sb.Length > 0 && sb[^1] != '{' && sb[^1] != ' ')
                    sb.Append(' ');
                sb.Append('}');
                continue;
            }

            if (TryReadOperator(line, i, out var op, out var consume))
            {
                if (IsTightOperator(op))
                {
                    TrimTrailingSpaces(sb);
                    sb.Append(op);
                }
                else
                {
                    TrimTrailingSpaces(sb);
                    sb.Append(' ');
                    sb.Append(op);
                    sb.Append(' ');
                }

                i += consume - 1;
                SkipFollowingSpaces(line, ref i);
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (sb.Length == 0 || sb[^1] != ' ')
                    sb.Append(' ');
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static bool IsRawCommandPassthrough(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0)
            return false;

        if (IsKnownLashPrefix(trimmed))
            return false;

        if (LooksLikeLashAssignment(trimmed))
            return false;

        if (LooksLikeFunctionCallExpression(trimmed))
            return false;

        if (LooksLikeLashPipeExpression(trimmed))
            return false;

        return true;
    }

    private static bool IsKnownLashPrefix(string line)
    {
        return line.StartsWith("fn ", StringComparison.Ordinal)
               || line == "end"
               || line.StartsWith("if ", StringComparison.Ordinal)
               || line.StartsWith("elif ", StringComparison.Ordinal)
               || line == "else"
               || line.StartsWith("for ", StringComparison.Ordinal)
               || line.StartsWith("while ", StringComparison.Ordinal)
               || line.StartsWith("switch ", StringComparison.Ordinal)
               || line.StartsWith("case ", StringComparison.Ordinal)
               || line.StartsWith("let ", StringComparison.Ordinal)
               || line.StartsWith("const ", StringComparison.Ordinal)
               || line.StartsWith("enum ", StringComparison.Ordinal)
               || line.StartsWith("global ", StringComparison.Ordinal)
               || line.StartsWith("return", StringComparison.Ordinal)
               || line.StartsWith("sh ", StringComparison.Ordinal)
               || line.StartsWith("shift", StringComparison.Ordinal)
               || line == "break"
               || line == "continue";
    }

    private static bool LooksLikeFunctionCallExpression(string line)
    {
        if (line.Length == 0)
            return false;
        if (!(char.IsLetter(line[0]) || line[0] == '_'))
            return false;

        int i = 1;
        while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_'))
            i++;

        while (i < line.Length && char.IsWhiteSpace(line[i]))
            i++;

        return i < line.Length && line[i] == '(';
    }

    private static bool LooksLikeLashPipeExpression(string line)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escaped = false;
        var parenDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;
        var stageStart = 0;
        var sawPipe = false;

        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (inDoubleQuote)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                    inDoubleQuote = false;

                continue;
            }

            if (inSingleQuote)
            {
                if (ch == '\'')
                    inSingleQuote = false;
                continue;
            }

            if (ch == '"')
            {
                inDoubleQuote = true;
                continue;
            }

            if (ch == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (ch == '(')
            {
                parenDepth++;
                continue;
            }

            if (ch == ')')
            {
                if (parenDepth > 0)
                    parenDepth--;
                continue;
            }

            if (ch == '[')
            {
                bracketDepth++;
                continue;
            }

            if (ch == ']')
            {
                if (bracketDepth > 0)
                    bracketDepth--;
                continue;
            }

            if (ch == '{')
            {
                braceDepth++;
                continue;
            }

            if (ch == '}')
            {
                if (braceDepth > 0)
                    braceDepth--;
                continue;
            }

            if (ch != '|')
                continue;

            var isLogicalOr = (i > 0 && line[i - 1] == '|') || (i + 1 < line.Length && line[i + 1] == '|');
            if (isLogicalOr)
                continue;

            if (parenDepth > 0 || bracketDepth > 0 || braceDepth > 0)
                continue;

            sawPipe = true;
            var stage = line[stageStart..i].Trim();
            if (LooksLikeFunctionCallExpression(stage))
                return true;

            stageStart = i + 1;
        }

        if (!sawPipe)
            return false;

        var lastStage = line[stageStart..].Trim();
        return LooksLikeFunctionCallExpression(lastStage);
    }

    private static bool LooksLikeLashAssignment(string line)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escaped = false;

        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (inDoubleQuote)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                    inDoubleQuote = false;

                continue;
            }

            if (inSingleQuote)
            {
                if (ch == '\'')
                    inSingleQuote = false;
                continue;
            }

            if (ch == '"')
            {
                inDoubleQuote = true;
                continue;
            }

            if (ch == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (ch != '=')
                continue;

            if (i + 1 < line.Length && line[i + 1] == '=')
                continue;

            if (i > 0 && (line[i - 1] == '=' || line[i - 1] == '!' || line[i - 1] == '<' || line[i - 1] == '>'))
                continue;

            var left = line[..i].TrimEnd();
            if (left.EndsWith("+", StringComparison.Ordinal))
                left = left[..^1].TrimEnd();

            if (IsIdentifier(left))
                return true;

            if (LooksLikeIndexTarget(left))
                return true;

            return false;
        }

        return false;
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0)
            return false;
        if (!(char.IsLetter(value[0]) || value[0] == '_'))
            return false;

        for (int i = 1; i < value.Length; i++)
        {
            var ch = value[i];
            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
                return false;
        }

        return true;
    }

    private static bool LooksLikeIndexTarget(string value)
    {
        if (!value.EndsWith("]", StringComparison.Ordinal))
            return false;

        var bracket = value.IndexOf('[');
        if (bracket <= 0 || bracket >= value.Length - 1)
            return false;

        var baseName = value[..bracket].Trim();
        return IsIdentifier(baseName);
    }

    private static bool TryReadOperator(string line, int index, out string op, out int consume)
    {
        if (TryReadFdDupOperator(line, index, out op, out consume))
            return true;

        var threeChar = index + 2 < line.Length ? line.Substring(index, 3) : string.Empty;
        if (threeChar is "&>>" or "2>>" or "<<<")
        {
            op = threeChar;
            consume = 3;
            return true;
        }

        var twoChar = index + 1 < line.Length ? line.Substring(index, 2) : string.Empty;
        if (twoChar is ">>" or "2>" or "&>" or "<>" or "::")
        {
            op = twoChar;
            consume = 2;
            return true;
        }

        if (twoChar is "==" or "!=" or "<=" or ">=" or "&&" or "||" or "..")
        {
            op = twoChar;
            consume = 2;
            return true;
        }

        var single = line[index];
        if (single is '=' or '+' or '-' or '*' or '/' or '%' or '<' or '>' or '|')
        {
            op = single.ToString();
            consume = 1;
            return true;
        }

        op = string.Empty;
        consume = 0;
        return false;
    }

    private static bool TryReadFdDupOperator(string line, int index, out string op, out int consume)
    {
        op = string.Empty;
        consume = 0;

        if (index >= line.Length || !char.IsDigit(line[index]))
            return false;

        int cursor = index;
        while (cursor < line.Length && char.IsDigit(line[cursor]))
            cursor++;

        if (cursor + 1 >= line.Length || line[cursor] != '>' || line[cursor + 1] != '&')
            return false;

        int targetStart = cursor + 2;
        if (targetStart >= line.Length)
            return false;

        if (line[targetStart] == '-')
        {
            consume = targetStart - index + 1;
            op = line.Substring(index, consume);
            return true;
        }

        int targetEnd = targetStart;
        while (targetEnd < line.Length && char.IsDigit(line[targetEnd]))
            targetEnd++;

        if (targetEnd == targetStart)
            return false;

        consume = targetEnd - index;
        op = line.Substring(index, consume);
        return true;
    }

    private static bool IsTightOperator(string op)
    {
        return op == "::";
    }

    private static void TrimTrailingSpaces(StringBuilder sb)
    {
        while (sb.Length > 0 && sb[^1] == ' ')
            sb.Length--;
    }

    private static void SkipFollowingSpaces(string line, ref int index)
    {
        while (index + 1 < line.Length && char.IsWhiteSpace(line[index + 1]))
            index++;
    }

    private static void EnsureSpaceBeforeBrace(StringBuilder sb)
    {
        if (sb.Length == 0)
            return;

        var prev = sb[^1];
        if (prev == ' ')
            return;

        if (char.IsLetterOrDigit(prev) || prev == '_' || prev == ')' || prev == ']')
            sb.Append(' ');
    }
}
