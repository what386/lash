namespace Lash.Compiler.Diagnostics;

using System.Text.RegularExpressions;
using Antlr4.Runtime;

internal static class SyntaxErrorFormatter {
  public static string FormatParserError(IToken? offendingSymbol,
                                         string rawMessage) {
    return FormatParserError(offendingSymbol, rawMessage, null);
  }

  public static string FormatParserError(IToken? offendingSymbol,
                                         string rawMessage,
                                         UnclosedBlockHint? unclosedBlockHint) {
    var offendingText = NormalizeToken(offendingSymbol?.Text);
    if (string.Equals(offendingText, "<EOF>", StringComparison.Ordinal))
      return FormatUnexpectedEndOfFile(rawMessage, unclosedBlockHint);

    if (IsPreprocessorLike(offendingText) ||
        rawMessage.Contains("'#", StringComparison.Ordinal))
      return DiagnosticMessage.WithTip(
          $"Unrecognized symbol '{offendingText}'",
          "Use '@' for preprocessor directives and '#' only for length (for " +
              "example '#items').");

    if (rawMessage.StartsWith("extraneous input", StringComparison.Ordinal))
      return DiagnosticMessage.WithTip(
          $"Unexpected token '{offendingText}'",
          "Check for a missing operator, delimiter, or block terminator.");

    if (rawMessage.StartsWith("mismatched input", StringComparison.Ordinal)) {
      if (LooksLikeIdentifier(offendingText))
        return DiagnosticMessage.WithTip(
            $"Unexpected token '{offendingText}'",
            $"Use '${offendingText}' for variable references.");

      return DiagnosticMessage.WithTip(
          $"Unexpected token '{offendingText}'",
          "Check surrounding syntax and matching block keywords.");
    }

    if (rawMessage.StartsWith("no viable alternative",
                              StringComparison.Ordinal)) {
      if (LooksLikeIdentifier(offendingText))
        return DiagnosticMessage.WithTip(
            $"Invalid syntax near '{offendingText}'",
            $"Use '${offendingText}' for variable references.");

      return DiagnosticMessage.WithTip(
          $"Invalid syntax near '{offendingText}'",
          "Check expression structure and quoting.");
    }

    return $"Syntax error: {rawMessage}";
  }

  public static string FormatLexerError(string rawMessage) {
    // Example: token recognition error at: '#'
    var m = Regex.Match(rawMessage, @"token recognition error at: '(.+)'");
    if (m.Success)
      return DiagnosticMessage.WithTip(
          $"Unrecognized symbol '{m.Groups[1].Value}'",
          "Check for unsupported punctuation or an unclosed string.");

    return $"Lexer error: {rawMessage}";
  }

  private static bool IsPreprocessorLike(string? text) {
    if (string.IsNullOrWhiteSpace(text))
      return false;
    if (string.Equals(text, "<EOF>", StringComparison.Ordinal))
      return false;

    return text.StartsWith("#", StringComparison.Ordinal);
  }

  private static string NormalizeToken(string? tokenText) {
    if (string.IsNullOrWhiteSpace(tokenText))
      return "<unknown>";

    return tokenText.Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);
  }

  private static bool LooksLikeIdentifier(string text) {
    if (string.IsNullOrWhiteSpace(text))
      return false;
    if (text[0] is not(>= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_'))
      return false;

    for (var i = 1; i < text.Length; i++)
      if (text[i] is not(>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <=
                         '9' or '_'))
        return false;

    return true;
  }

  public static bool IsMissingEndAtEof(IToken? offendingSymbol,
                                       string rawMessage) {
    if (!string.Equals(NormalizeToken(offendingSymbol?.Text), "<EOF>",
                       StringComparison.Ordinal))
      return false;

    var expected = ExtractExpectedTokens(rawMessage);
    return expected != null &&
           expected.Contains("'end'", StringComparison.Ordinal);
  }

  private static string
  FormatUnexpectedEndOfFile(string rawMessage,
                            UnclosedBlockHint? unclosedBlockHint) {
    var expected = ExtractExpectedTokens(rawMessage);
    if (expected is null)
      return DiagnosticMessage.WithTip(
          "Unexpected end of file.",
          "Check for an unclosed block, string, or directive.");

    if (expected.Contains("'end'", StringComparison.Ordinal) &&
        unclosedBlockHint is UnclosedBlockHint hint)
      return DiagnosticMessage.WithTip(
          "Unexpected end of file: missing 'end' to close an open block.",
          $"Add 'end' to close '{hint.Keyword}' opened at line {hint.Line}.");

    if (expected.Contains("'end'", StringComparison.Ordinal))
      return DiagnosticMessage.WithTip(
          "Unexpected end of file: missing 'end' to close an open block.",
          "Add an 'end' to each opened block.");

    return DiagnosticMessage.WithTip(
        $"Unexpected end of file: expected {expected}.",
        "Finish the statement or close the open construct before end of file.");
  }

  private static string? ExtractExpectedTokens(string rawMessage) {
    var marker = "expecting ";
    var index = rawMessage.IndexOf(marker, StringComparison.Ordinal);
    if (index < 0)
      return null;

    var expected = rawMessage[(index + marker.Length)..].Trim();
    if (expected.Length == 0)
      return null;

    return expected;
  }
}

internal readonly record struct UnclosedBlockHint(string Keyword, int Line,
                                                  int Column);
