namespace Lash.Compiler.CodeGen;

using System.Globalization;
using System.Text;
using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Types;

public partial class BashGenerator
{
    private string GenerateExpression(Expression expr)
    {
        return expr switch
        {
            LiteralExpression lit => GenerateLiteral(lit),
            IdentifierExpression ident => GenerateIdentifierExpression(ident),
            BinaryExpression bin => GenerateBinaryExpression(bin),
            UnaryExpression unary => GenerateUnaryExpression(unary),
            FunctionCallExpression call => GenerateFunctionCall(call),
            ShellCaptureExpression shellCapture => GenerateShellCaptureExpression(shellCapture),
            EnumAccessExpression enumAccess => $"\"{EscapeString(enumAccess.EnumName + enumAccess.MemberName)}\"",
            IndexAccessExpression index => GenerateIndexAccess(index),
            ArrayLiteral array => GenerateArrayLiteral(array),
            PipeExpression pipe => GeneratePipeExpression(pipe),
            RedirectExpression => HandleUnsupportedExpression(expr, "redirect as value"),
            RangeExpression => HandleUnsupportedExpression(expr, "range as value"),
            NullLiteral => "\"\"",
            _ => UnsupportedExpression(expr)
        };
    }

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
                    : $"\"{EscapeString(lit.Value?.ToString() ?? string.Empty, preserveLineBreaks: lit.IsMultiline)}\"",
                PrimitiveType.Kind.Int => lit.Value?.ToString() ?? "0",
                PrimitiveType.Kind.Bool => lit.Value?.ToString()?.ToLowerInvariant() == "true" ? "1" : "0",
                _ => "\"\""
            };
        }

        return "\"\"";
    }

    private static string GenerateInterpolatedStringLiteral(string template)
    {
        var builder = new StringBuilder();
        builder.Append('"');

        int cursor = 0;
        while (cursor < template.Length)
        {
            var openBrace = FindNextUnescaped(template, '{', cursor);
            if (openBrace < 0)
            {
                builder.Append(EscapeForDoubleQuotes(template[cursor..]));
                break;
            }

            builder.Append(EscapeForDoubleQuotes(template[cursor..openBrace]));

            var closeBrace = FindNextUnescaped(template, '}', openBrace + 1);
            if (closeBrace < 0)
            {
                builder.Append(EscapeForDoubleQuotes(template[openBrace..]));
                break;
            }

            var placeholder = template[(openBrace + 1)..closeBrace].Trim();
            if (TryGetIdentifierPath(placeholder, out var path))
                builder.Append("${" + path + "}");
            else
                builder.Append(EscapeForDoubleQuotes(template[openBrace..(closeBrace + 1)]));

            cursor = closeBrace + 1;
        }

        builder.Append('"');
        return builder.ToString();
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

    private string GenerateBinaryExpression(BinaryExpression bin)
    {
        var left = GenerateExpression(bin.Left);
        var right = GenerateExpression(bin.Right);
        var leftArithmetic = GenerateArithmeticExpression(bin.Left);
        var rightArithmetic = GenerateArithmeticExpression(bin.Right);

        return bin.Operator switch
        {
            "+" => GenerateAddition(bin.Left, bin.Right, left, right),
            "-" => $"$(({leftArithmetic} - {rightArithmetic}))",
            "*" => $"$(({leftArithmetic} * {rightArithmetic}))",
            "/" => $"$(({leftArithmetic} / {rightArithmetic}))",
            "%" => $"$(({leftArithmetic} % {rightArithmetic}))",
            "==" => $"$(( {leftArithmetic} == {rightArithmetic} ))",
            "!=" => $"$(( {leftArithmetic} != {rightArithmetic} ))",
            "<" => $"$(( {leftArithmetic} < {rightArithmetic} ))",
            ">" => $"$(( {leftArithmetic} > {rightArithmetic} ))",
            "<=" => $"$(( {leftArithmetic} <= {rightArithmetic} ))",
            ">=" => $"$(( {leftArithmetic} >= {rightArithmetic} ))",
            "&&" => $"$(( ({leftArithmetic} != 0) && ({rightArithmetic} != 0) ))",
            "||" => $"$(( ({leftArithmetic} != 0) || ({rightArithmetic} != 0) ))",
            _ => $"{left} {bin.Operator} {right}"
        };
    }

    private string GenerateUnaryExpression(UnaryExpression unary)
    {
        return unary.Operator switch
        {
            "-" => $"$((-{GenerateArithmeticExpression(unary.Operand)}))",
            "+" => $"$((+{GenerateArithmeticExpression(unary.Operand)}))",
            "!" => $"$((!{GenerateArithmeticExpression(unary.Operand)}))",
            "#" => GenerateLengthExpression(unary.Operand),
            _ => GenerateArithmeticExpression(unary.Operand)
        };
    }

    private string GenerateFunctionCall(FunctionCallExpression call)
    {
        var args = string.Join(" ", call.Arguments.Select(GenerateFunctionCallArg));
        return args.Length > 0 ? $"$({call.FunctionName} {args})" : $"$({call.FunctionName})";
    }

    private string GenerateShellCaptureExpression(ShellCaptureExpression shellCapture)
    {
        if (!TryGenerateShellPayload(shellCapture.Command, out var payload))
            return HandleUnsupportedExpression(shellCapture, "shell capture payload");

        return $"$({payload})";
    }

    private string GenerateFunctionCallArg(Expression expression)
    {
        if (expression is IdentifierExpression identifier &&
            string.Equals(identifier.Name, "argv", StringComparison.Ordinal))
        {
            return $"\"${{{ArgvRuntimeName}[@]}}\"";
        }

        var rendered = GenerateExpression(expression);
        if (IsAlreadyQuoted(rendered))
            return rendered;
        return $"\"{rendered}\"";
    }

    private static bool IsAlreadyQuoted(string rendered)
    {
        return rendered.Length >= 2 &&
               ((rendered[0] == '"' && rendered[^1] == '"') || (rendered[0] == '\'' && rendered[^1] == '\''));
    }

    private string GenerateArrayLiteral(ArrayLiteral array)
    {
        var elements = string.Join(" ", array.Elements.Select(GenerateExpression));
        return $"({elements})";
    }

    private string GeneratePipeExpression(PipeExpression expr)
    {
        return GenerateValuePipeExpression(expr);
    }

    private string GenerateValuePipeExpression(PipeExpression expr)
    {
        var input = GenerateExpression(expr.Left);
        return expr.Right switch
        {
            FunctionCallExpression call => GenerateFunctionPipeInvocation(input, call),
            _ => HandleUnsupportedExpression(expr, "value pipe right stage")
        };
    }

    private string GenerateFunctionPipeInvocation(string pipedInput, FunctionCallExpression call)
    {
        var args = new List<string> { QuoteRenderedArg(pipedInput) };
        args.AddRange(call.Arguments.Select(GenerateFunctionCallArg));
        return $"$({call.FunctionName} {string.Join(" ", args)})";
    }

    private static string QuoteRenderedArg(string rendered)
    {
        if (IsAlreadyQuoted(rendered))
            return rendered;
        return $"\"{rendered}\"";
    }

    private string GenerateIndexAccess(IndexAccessExpression index)
    {
        if (index.Array is IdentifierExpression ident)
        {
            if (string.Equals(ident.Name, "argv", StringComparison.Ordinal))
            {
                var argvKey = GenerateNumericArrayIndex(index.Index);
                return $"${{{ArgvRuntimeName}[{argvKey}]}}";
            }

            var key = GenerateCollectionIndex(index.Index, IsCurrentScopeAssociative(ident.Name));
            return $"${{{ident.Name}[{key}]}}";
        }

        return HandleUnsupportedExpression(index, "index access receiver");
    }

    private string GenerateCollectionIndex(Expression index, bool preferString)
    {
        if (index is LiteralExpression literal &&
            literal.LiteralType is PrimitiveType primitive &&
            primitive.PrimitiveKind == PrimitiveType.Kind.String)
        {
            return $"\"{EscapeString(literal.Value?.ToString() ?? string.Empty)}\"";
        }

        if (preferString || IsStringTyped(index))
        {
            var rendered = GenerateExpression(index);
            if (IsAlreadyQuoted(rendered))
                return rendered;
            return $"\"{rendered}\"";
        }

        return GenerateNumericArrayIndex(index);
    }

    private string GenerateNumericArrayIndex(Expression index)
    {
        if (index is LiteralExpression { LiteralType: PrimitiveType { PrimitiveKind: PrimitiveType.Kind.Int }, Value: not null } literal)
            return Convert.ToString(literal.Value, CultureInfo.InvariantCulture) ?? "0";

        return $"$(( {GenerateArithmeticExpression(index)} ))";
    }

    private string GenerateAddition(Expression leftExpr, Expression rightExpr, string left, string right)
    {
        if (IsStringLike(leftExpr) || IsStringLike(rightExpr))
        {
            var leftArg = GenerateStringConcatSegment(leftExpr, left);
            var rightArg = GenerateStringConcatSegment(rightExpr, right);
            return $"\"{leftArg}{rightArg}\"";
        }

        return $"$(( {left} + {right} ))";
    }

    private string GenerateStringConcatSegment(Expression expression, string renderedExpression)
    {
        return expression switch
        {
            IdentifierExpression ident => "${" + ident.Name + "}",
            LiteralExpression lit when lit.LiteralType is PrimitiveType { PrimitiveKind: PrimitiveType.Kind.String } => StripOuterQuotes(renderedExpression),
            BinaryExpression nested when nested.Operator == "+" && IsStringLike(nested) => StripOuterQuotes(renderedExpression),
            _ => StripOuterQuotes(renderedExpression)
        };
    }

    private static string StripOuterQuotes(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    private string GenerateArithmeticExpression(Expression expr)
    {
        return expr switch
        {
            IdentifierExpression ident when string.Equals(ident.Name, "argv", StringComparison.Ordinal) =>
                HandleUnsupportedExpression(ident, "argv in arithmetic"),
            IdentifierExpression ident => ident.Name,
            LiteralExpression lit when lit.LiteralType is PrimitiveType { PrimitiveKind: PrimitiveType.Kind.Int } =>
                lit.Value?.ToString() ?? "0",
            LiteralExpression lit when lit.LiteralType is PrimitiveType { PrimitiveKind: PrimitiveType.Kind.Bool } =>
                lit.Value?.ToString()?.ToLowerInvariant() == "true" ? "1" : "0",
            UnaryExpression unary =>
                unary.Operator switch
                {
                    "-" => $"-{GenerateArithmeticExpression(unary.Operand)}",
                    "+" => $"+{GenerateArithmeticExpression(unary.Operand)}",
                    "!" => $"!{GenerateArithmeticExpression(unary.Operand)}",
                    "#" => GenerateLengthExpression(unary.Operand),
                    _ => GenerateExpression(unary)
                },
            BinaryExpression bin when bin.Operator is "+" or "-" or "*" or "/" or "%" or "==" or "!=" or "<" or ">" or "<=" or ">=" or "&&" or "||" =>
                $"{GenerateArithmeticExpression(bin.Left)} {bin.Operator} {GenerateArithmeticExpression(bin.Right)}",
            _ => GenerateExpression(expr)
        };
    }

    private static bool IsStringLike(Expression expr)
    {
        return expr switch
        {
            LiteralExpression lit when lit.LiteralType is PrimitiveType { PrimitiveKind: PrimitiveType.Kind.String } => true,
            BinaryExpression bin when bin.Operator == "+" && (IsStringLike(bin.Left) || IsStringLike(bin.Right)) => true,
            _ when IsStringTyped(expr) => true,
            _ => false
        };
    }

    private static bool IsStringTyped(Expression expr) => expr.Type is StringType;

    private bool TryGenerateShellPayload(Expression expression, out string payload)
    {
        if (expression is LiteralExpression literal &&
            literal.LiteralType is PrimitiveType { PrimitiveKind: PrimitiveType.Kind.String })
        {
            var value = literal.Value?.ToString() ?? string.Empty;
            payload = literal.IsInterpolated
                ? RenderInterpolatedShellPayload(value)
                : UnescapeShellPayloadText(value);
            return true;
        }

        payload = string.Empty;
        return false;
    }

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

    private string HandleUnsupportedExpression(Expression expr, string feature)
    {
        ReportUnsupported(feature);
        return UnsupportedExpression(expr);
    }

    private string GenerateLengthExpression(Expression operand)
    {
        if (operand is IdentifierExpression ident)
        {
            if (string.Equals(ident.Name, "argv", StringComparison.Ordinal))
                return $"${{#{ArgvRuntimeName}[@]}}";

            return $"${{#{ident.Name}[@]}}";
        }

        if (operand is IndexAccessExpression index &&
            index.Array is IdentifierExpression arrayIdent)
        {
            if (string.Equals(arrayIdent.Name, "argv", StringComparison.Ordinal))
            {
                var argvKey = GenerateNumericArrayIndex(index.Index);
                return $"${{#{ArgvRuntimeName}[{argvKey}]}}";
            }

            var key = GenerateCollectionIndex(index.Index, IsCurrentScopeAssociative(arrayIdent.Name));
            return $"${{#{arrayIdent.Name}[{key}]}}";
        }

        if (operand is LiteralExpression literal &&
            literal.LiteralType is PrimitiveType { PrimitiveKind: PrimitiveType.Kind.String })
        {
            return (literal.Value?.ToString() ?? string.Empty).Length.ToString(CultureInfo.InvariantCulture);
        }

        if (operand is ArrayLiteral arrayLiteral)
            return arrayLiteral.Elements.Count.ToString(CultureInfo.InvariantCulture);

        return HandleUnsupportedExpression(operand, "length operand");
    }
}
