namespace Lash.Compiler.CodeGen;

using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;
using Lash.Compiler.Ast.Types;

internal sealed partial class StatementGenerator
{
    private string RenderCommandStatement(CommandStatement commandStatement)
    {
        if (commandStatement.IsRawLiteral)
            return commandStatement.Script;

        var script = commandStatement.Script;
        if (!script.Contains("$\"", StringComparison.Ordinal))
            return script;

        var output = new System.Text.StringBuilder(script.Length);

        for (int i = 0; i < script.Length;)
        {
            if (script[i] == '$' && i + 1 < script.Length && script[i + 1] == '"')
            {
                var cursor = i + 2;
                while (cursor < script.Length)
                {
                    if (script[cursor] == '"' && !IsEscapedQuote(script, cursor))
                        break;
                    cursor++;
                }

                if (cursor >= script.Length)
                {
                    output.Append(script[i..]);
                    break;
                }

                var template = script[(i + 2)..cursor];
                output.Append(BashGenerator.GenerateInterpolatedStringLiteral(template));
                i = cursor + 1;
                continue;
            }

            output.Append(script[i]);
            i++;
        }

        return output.ToString();
    }

    private static bool IsEscapedQuote(string text, int index)
    {
        var slashCount = 0;
        for (var i = index - 1; i >= 0 && text[i] == '\\'; i--)
            slashCount++;
        return (slashCount % 2) != 0;
    }

    private void GenerateExpressionStatement(Expression expression)
    {
        switch (expression)
        {
            case BinaryExpression binaryExpression when IsComparisonRedirect(binaryExpression):
                owner.Emit(GenerateBinaryRedirectStatement(binaryExpression));
                return;

            case FunctionCallExpression call:
                owner.Emit(GenerateFunctionCallStatement(call));
                return;

            case PipeExpression pipeExpression:
                owner.Emit(GeneratePipeStatement(pipeExpression));
                return;

            case RedirectExpression redirectExpression:
                if (string.Equals(redirectExpression.Operator, "<<", StringComparison.Ordinal))
                {
                    GenerateHeredocStatement(redirectExpression);
                }
                else
                {
                    owner.Emit(GenerateRedirectStatement(redirectExpression));
                }
                return;
        }

        owner.Emit(owner.GenerateExpression(expression));
    }

    private static bool IsComparisonRedirect(BinaryExpression binaryExpression)
    {
        return binaryExpression.Operator is ">" or "<";
    }

    private string GenerateBinaryRedirectStatement(BinaryExpression binaryExpression)
    {
        var redirect = new RedirectExpression
        {
            Line = binaryExpression.Line,
            Column = binaryExpression.Column,
            Left = binaryExpression.Left,
            Operator = binaryExpression.Operator,
            Right = binaryExpression.Right,
            Type = binaryExpression.Type
        };

        return GenerateRedirectStatement(redirect);
    }

    private void GenerateShellStatement(ShellStatement shellStatement)
    {
        if (!owner.TryGenerateShellPayload(shellStatement.Command, out var payload))
        {
            owner.EmitComment("Unsupported 'sh' command payload.");
            owner.ReportUnsupported("sh command payload");
            return;
        }

        owner.Emit(payload);
    }

    private void GenerateTestStatement(TestStatement testStatement)
    {
        if (!owner.TryGenerateShellPayload(testStatement.Condition, out var payload))
        {
            owner.EmitComment("Unsupported 'test' condition payload.");
            owner.ReportUnsupported("test condition payload");
            return;
        }

        owner.Emit($"[[ {payload} ]]");
    }

    private string GenerateFunctionCallStatement(FunctionCallExpression call)
    {
        var args = string.Join(" ", call.Arguments.Select(GenerateSingleShellArg));
        return args.Length > 0 ? $"{call.FunctionName} {args}" : call.FunctionName;
    }

    private string GenerateSingleShellArg(Expression expression)
    {
        if (expression is IdentifierExpression identifier &&
            string.Equals(identifier.Name, "argv", StringComparison.Ordinal))
        {
            return $"\"${{{BashGenerator.ArgvName}[@]}}\"";
        }

        var rendered = owner.GenerateExpression(expression);
        if (IsAlreadyQuotedArg(rendered))
            return rendered;
        return $"\"{rendered}\"";
    }

    private static bool IsAlreadyQuotedArg(string rendered)
    {
        if (rendered.Length < 2)
            return false;

        return (rendered[0] == '"' && rendered[^1] == '"')
               || (rendered[0] == '\'' && rendered[^1] == '\'');
    }

    private string GeneratePipeStatement(PipeExpression expr)
    {
        if (TryGetPipeAssignment(expr, out var target, out var valueExpression))
        {
            var value = owner.GenerateExpression(valueExpression);
            return $"{target}={value}";
        }

        return ":";
    }

    private string GenerateRedirectStatement(RedirectExpression redirect)
    {
        var op = redirect.Operator;
        if (IsFdDupOperator(op))
        {
            return redirect.Left switch
            {
                FunctionCallExpression call => $"{GenerateFunctionCallStatement(call)} {op}",
                PipeExpression pipe => $"{GeneratePipeStatement(pipe)} {op}",
                _ => $"echo {GenerateSingleShellArg(redirect.Left)} {op}"
            };
        }

        var fileTarget = GenerateSingleShellArg(redirect.Right);

        return redirect.Left switch
        {
            FunctionCallExpression call => $"{GenerateFunctionCallStatement(call)} {op} {fileTarget}",
            PipeExpression pipe => $"{GeneratePipeStatement(pipe)} {op} {fileTarget}",
            _ => $"echo {GenerateSingleShellArg(redirect.Left)} {op} {fileTarget}"
        };
    }

    private void GenerateHeredocStatement(RedirectExpression redirect)
    {
        if (!TryGetHeredocPayload(redirect.Right, out var payload))
        {
            owner.EmitComment("Unsupported heredoc payload; expected non-interpolated string literal.");
            owner.ReportUnsupported("heredoc payload");
            return;
        }

        var command = redirect.Left switch
        {
            FunctionCallExpression call => GenerateFunctionCallStatement(call),
            PipeExpression pipe => GeneratePipeStatement(pipe),
            _ => $"echo {GenerateSingleShellArg(redirect.Left)}"
        };

        var delimiter = ChooseHeredocDelimiter(payload);
        owner.Emit($"{command} <<'{delimiter}'");
        owner.EmitLine();

        var lines = payload.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        foreach (var line in lines)
        {
            owner.Emit(line);
            owner.EmitLine();
        }

        owner.Emit(delimiter);
    }

    private static bool TryGetHeredocPayload(Expression expression, out string payload)
    {
        payload = string.Empty;
        if (expression is not LiteralExpression literal ||
            literal.LiteralType is not PrimitiveType { PrimitiveKind: PrimitiveType.Kind.String } ||
            literal.IsInterpolated)
        {
            return false;
        }

        payload = literal.Value?.ToString() ?? string.Empty;
        return true;
    }

    private static string ChooseHeredocDelimiter(string payload)
    {
        var normalized = payload.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var delimiter = "LASH_HEREDOC";
        var suffix = 0;

        while (lines.Any(line => string.Equals(line, delimiter, StringComparison.Ordinal)))
        {
            suffix++;
            delimiter = $"LASH_HEREDOC_{suffix}";
        }

        return delimiter;
    }

    private static bool IsFdDupOperator(string op)
    {
        if (op.Length < 4)
            return false;

        int i = 0;
        while (i < op.Length && char.IsDigit(op[i]))
            i++;

        if (i == 0 || i + 2 > op.Length || op[i] != '>' || op[i + 1] != '&')
            return false;

        var target = op[(i + 2)..];
        if (target == "-")
            return true;

        return target.Length > 0 && target.All(char.IsDigit);
    }

    private static bool TryGetPipeAssignment(PipeExpression expr, out string target, out Expression valueExpression)
    {
        target = string.Empty;
        valueExpression = expr;

        if (expr.Right is IdentifierExpression identifier)
        {
            target = identifier.Name;
            valueExpression = expr.Left;
            return true;
        }

        return false;
    }
}
