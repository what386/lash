namespace Lash.Compiler.CodeGen;

using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Types;

internal sealed partial class ExpressionGenerator
{
    private string GenerateIdentifierExpression(IdentifierExpression ident)
    {
        if (string.Equals(ident.Name, "argv", StringComparison.Ordinal))
            return HandleUnsupportedExpression(ident, "bare argv expression");

        return $"${{{ident.Name}}}";
    }

    private string GenerateLiteral(LiteralExpression lit)
    {
        if (lit.LiteralType is PrimitiveType prim)
        {
            return prim.PrimitiveKind switch
            {
                PrimitiveType.Kind.String => lit.IsInterpolated
                    ? GenerateInterpolatedStringLiteral(lit.Value?.ToString() ?? string.Empty)
                    : $"\"{owner.EscapeString(lit.Value?.ToString() ?? string.Empty, preserveLineBreaks: lit.IsMultiline)}\"",
                PrimitiveType.Kind.Int => lit.Value?.ToString() ?? "0",
                PrimitiveType.Kind.Bool => lit.Value?.ToString()?.ToLowerInvariant() == "true" ? "1" : "0",
                _ => "\"\""
            };
        }

        return "\"\"";
    }

    private static int FindNextUnescaped(string text, char needle, int start)
    {
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == needle && (i == 0 || text[i - 1] != '\\'))
                return i;
        }

        return -1;
    }

    private static string EscapeForDoubleQuotes(string input)
    {
        return input
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal);
    }

    private static bool TryGetIdentifierPath(string input, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var parts = input.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        foreach (var part in parts)
        {
            if (!IsIdentifier(part))
                return false;
        }

        path = string.Join("_", parts);
        return true;
    }

    private static bool IsIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;
        if (!IsIdentifierStart(value[0]))
            return false;

        for (int i = 1; i < value.Length; i++)
        {
            if (!IsIdentifierPart(value[i]))
                return false;
        }

        return true;
    }

    private static bool IsIdentifierStart(char c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
    }

    private static bool IsIdentifierPart(char c)
    {
        return IsIdentifierStart(c) || (c >= '0' && c <= '9');
    }
}
