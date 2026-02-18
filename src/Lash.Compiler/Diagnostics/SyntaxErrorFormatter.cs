namespace Lash.Compiler.Diagnostics;

using System.Text.RegularExpressions;
using Antlr4.Runtime;

internal static class SyntaxErrorFormatter
{
    public static string FormatParserError(IToken? offendingSymbol, string rawMessage)
    {
        var offendingText = offendingSymbol?.Text;

        if (IsPreprocessorLike(offendingText) || rawMessage.Contains("'#", StringComparison.Ordinal))
            return $"Unrecognized symbol '{NormalizeToken(offendingText)}'";

        if (rawMessage.StartsWith("extraneous input", StringComparison.Ordinal))
            return $"Unexpected token '{NormalizeToken(offendingText)}'";

        if (rawMessage.StartsWith("mismatched input", StringComparison.Ordinal))
            return $"Unexpected token '{NormalizeToken(offendingText)}'";

        if (rawMessage.StartsWith("no viable alternative", StringComparison.Ordinal))
            return $"Invalid syntax near '{NormalizeToken(offendingText)}'";

        return $"Syntax error: {rawMessage}";
    }

    public static string FormatLexerError(string rawMessage)
    {
        // Example: token recognition error at: '#'
        var m = Regex.Match(rawMessage, @"token recognition error at: '(.+)'");
        if (m.Success)
            return $"Unrecognized symbol '{m.Groups[1].Value}'";

        return $"Lexer error: {rawMessage}";
    }

    private static bool IsPreprocessorLike(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.StartsWith("#", StringComparison.Ordinal);
    }

    private static string NormalizeToken(string? tokenText)
    {
        if (string.IsNullOrWhiteSpace(tokenText))
            return "<unknown>";

        return tokenText
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}

