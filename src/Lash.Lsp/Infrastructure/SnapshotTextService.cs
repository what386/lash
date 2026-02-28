namespace Lash.Lsp.Infrastructure;

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

internal sealed record SnapshotToken(string Text, Range Range);

internal sealed class SnapshotTextService
{
    private static readonly string[] OperatorsByLength =
    [
        "n>&-", "n>&m", "2>>", "&>>", "<<<", "<<", "2>", "&>", ">>", "<>", "::", "&&", "||", "==", "!=", "<=", ">=", "+=", "..", "|", "#", ">", "<"
    ];

    public bool TryGetLine(DocumentSnapshot snapshot, int zeroBasedLine, out string lineText)
    {
        var lines = NormalizeLines(snapshot.Text);
        if (zeroBasedLine < 0 || zeroBasedLine >= lines.Length)
        {
            lineText = string.Empty;
            return false;
        }

        lineText = lines[zeroBasedLine];
        return true;
    }

    public bool TryGetTokenAt(DocumentSnapshot snapshot, Position position, out SnapshotToken token)
    {
        token = new SnapshotToken(string.Empty, new Range(position, position));

        if (!TryGetLine(snapshot, (int)position.Line, out var lineText))
            return false;

        var column = Math.Clamp((int)position.Character, 0, lineText.Length);

        if (TryGetIdentifierLikeToken(lineText, column, out var identifierToken, out var start, out var end))
        {
            token = new SnapshotToken(identifierToken, new Range(new Position(position.Line, start), new Position(position.Line, end)));
            return true;
        }

        if (TryGetOperatorToken(lineText, column, out var operatorToken, out start, out end))
        {
            token = new SnapshotToken(operatorToken, new Range(new Position(position.Line, start), new Position(position.Line, end)));
            return true;
        }

        return false;
    }

    public bool TryGetCompletionPrefix(DocumentSnapshot snapshot, Position position, out string prefix, out bool directiveContext, out bool dollarPrefix)
    {
        prefix = string.Empty;
        directiveContext = false;
        dollarPrefix = false;

        if (!TryGetLine(snapshot, (int)position.Line, out var lineText))
            return false;

        var column = Math.Clamp((int)position.Character, 0, lineText.Length);
        var i = column - 1;

        while (i >= 0 && IsIdentifierPart(lineText[i]))
            i--;

        var start = i + 1;
        prefix = start < lineText.Length && start <= column
            ? lineText[start..column]
            : string.Empty;

        if (i >= 0 && lineText[i] == '@')
        {
            directiveContext = true;
            prefix = "@" + prefix;
            return true;
        }

        if (i >= 0 && lineText[i] == '$')
            dollarPrefix = true;

        var nonWhitespace = 0;
        while (nonWhitespace < lineText.Length && char.IsWhiteSpace(lineText[nonWhitespace]))
            nonWhitespace++;

        directiveContext = nonWhitespace < lineText.Length && lineText[nonWhitespace] == '@';
        return true;
    }

    public bool IsSpanBackedBySnapshotText(DocumentSnapshot snapshot, int oneBasedLine, int startColumn, int endColumn, string expected)
    {
        if (!TryGetLine(snapshot, oneBasedLine - 1, out var lineText))
            return false;

        var start = Math.Clamp(startColumn, 0, lineText.Length);
        var end = Math.Clamp(endColumn, 0, lineText.Length);

        if (end <= start)
            return false;

        var candidate = lineText[start..end];
        return string.Equals(candidate, expected, StringComparison.Ordinal);
    }

    public bool TryFindIdentifierInLine(DocumentSnapshot snapshot, int oneBasedLine, string identifier, out int startColumn)
    {
        startColumn = -1;
        if (string.IsNullOrEmpty(identifier))
            return false;

        if (!TryGetLine(snapshot, oneBasedLine - 1, out var lineText))
            return false;

        for (var i = 0; i <= lineText.Length - identifier.Length; i++)
        {
            if (!lineText.AsSpan(i, identifier.Length).SequenceEqual(identifier.AsSpan()))
                continue;

            var leftOk = i == 0 || !IsIdentifierPart(lineText[i - 1]);
            var rightIndex = i + identifier.Length;
            var rightOk = rightIndex >= lineText.Length || !IsIdentifierPart(lineText[rightIndex]);
            if (!leftOk || !rightOk)
                continue;

            startColumn = i;
            return true;
        }

        return false;
    }

    private static string[] NormalizeLines(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static bool TryGetIdentifierLikeToken(string lineText, int column, out string token, out int start, out int end)
    {
        token = string.Empty;
        start = 0;
        end = 0;

        if (lineText.Length == 0)
            return false;

        var pivot = Math.Min(Math.Max(column, 0), lineText.Length - 1);
        if (column == lineText.Length && column > 0)
            pivot = column - 1;

        if (!IsIdentifierPart(lineText[pivot]) && lineText[pivot] != '@')
            return false;

        start = pivot;
        while (start > 0 && IsIdentifierPart(lineText[start - 1]))
            start--;

        if (start > 0 && lineText[start - 1] == '@')
            start--;

        end = pivot + 1;
        while (end < lineText.Length && IsIdentifierPart(lineText[end]))
            end++;

        token = lineText[start..end];

        if (token == "@")
            return false;

        if (token[0] == '@' && (token.Length == 1 || !IdentifierRules.IsValidIdentifier(token[1..])))
            return false;

        if (token[0] != '@' && !IdentifierRules.IsValidIdentifier(token))
            return false;

        return true;
    }

    private static bool TryGetOperatorToken(string lineText, int column, out string token, out int start, out int end)
    {
        token = string.Empty;
        start = 0;
        end = 0;

        if (lineText.Length == 0)
            return false;

        var searchStart = Math.Max(0, column - 4);
        var searchEnd = Math.Min(lineText.Length - 1, column + 1);

        for (var i = searchStart; i <= searchEnd; i++)
        {
            foreach (var op in OperatorsByLength)
            {
                if (i + op.Length > lineText.Length)
                    continue;

                if (!lineText.AsSpan(i, op.Length).SequenceEqual(op.AsSpan()))
                    continue;

                var include = column >= i && column <= i + op.Length;
                if (!include)
                    continue;

                token = op;
                start = i;
                end = i + op.Length;
                return true;
            }

            if (char.IsDigit(lineText[i]))
            {
                var j = i;
                while (j < lineText.Length && char.IsDigit(lineText[j]))
                    j++;

                if (j + 1 < lineText.Length && lineText[j] == '>' && lineText[j + 1] == '&')
                {
                    var k = j + 2;
                    if (k < lineText.Length && lineText[k] == '-')
                    {
                        token = "n>&-";
                        start = i;
                        end = k + 1;
                        if (column >= start && column <= end)
                            return true;
                    }
                    else
                    {
                        while (k < lineText.Length && char.IsDigit(lineText[k]))
                            k++;

                        if (k > j + 2)
                        {
                            token = "n>&m";
                            start = i;
                            end = k;
                            if (column >= start && column <= end)
                                return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private static bool IsIdentifierPart(char c)
    {
        return (c >= 'a' && c <= 'z')
            || (c >= 'A' && c <= 'Z')
            || (c >= '0' && c <= '9')
            || c == '_';
    }
}
