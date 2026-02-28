namespace Lash.Compiler.CodeGen;

using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;
using Lash.Compiler.Ast.Types;

public partial class BashGenerator
{
    private void GenerateStatement(Statement stmt)
    {
        currentContext = stmt.GetType().Name;

        switch (stmt)
        {
            case VariableDeclaration varDecl:
                GenerateVariableDeclaration(varDecl);
                break;

            case Assignment assignment:
                GenerateAssignment(assignment);
                break;

            case FunctionDeclaration funcDecl:
                GenerateFunctionDeclaration(funcDecl);
                break;

            case EnumDeclaration:
                break;

            case IfStatement ifStmt:
                GenerateIfStatement(ifStmt);
                break;

            case SwitchStatement switchStatement:
                GenerateSwitchStatement(switchStatement);
                break;

            case ForLoop forLoop:
                GenerateForLoop(forLoop);
                break;

            case WhileLoop whileLoop:
                GenerateWhileLoop(whileLoop);
                break;

            case UntilLoop untilLoop:
                GenerateUntilLoop(untilLoop);
                break;

            case ReturnStatement returnStmt:
                GenerateReturnStatement(returnStmt);
                break;

            case ShiftStatement shiftStatement:
                GenerateShiftStatement(shiftStatement);
                break;

            case SubshellStatement subshellStatement:
                GenerateSubshellStatement(subshellStatement);
                break;

            case WaitStatement waitStatement:
                GenerateWaitStatement(waitStatement);
                break;

            case BreakStatement:
                Emit("break");
                break;

            case ContinueStatement:
                Emit("continue");
                break;

            case ExpressionStatement exprStmt:
                GenerateExpressionStatement(exprStmt.Expression);
                break;

            case ShellStatement shellStmt:
                GenerateShellStatement(shellStmt);
                break;

            case CommandStatement commandStmt:
                Emit(RenderCommandStatement(commandStmt));
                break;

            default:
                EmitComment($"Unsupported statement '{stmt.GetType().Name}'.");
                ReportUnsupported($"statement '{stmt.GetType().Name}'");
                break;
        }
    }

    private void GenerateVariableDeclaration(VariableDeclaration varDecl)
    {
        var isAssociative = IsAssociativeVariable(varDecl.Name, varDecl.IsGlobal);
        var isFunctionLocal = currentFunctionName != null && !varDecl.IsGlobal;
        var value = GenerateVariableDeclarationValue(varDecl.Value);

        if (isAssociative)
        {
            if (isFunctionLocal)
            {
                Emit($"local -A {varDecl.Name}={value}");
                if (varDecl.Kind == VariableDeclaration.VarKind.Const)
                {
                    EmitLine();
                    Emit($"readonly {varDecl.Name}");
                }
                return;
            }

            Emit($"declare -A {varDecl.Name}={value}");
            if (varDecl.Kind == VariableDeclaration.VarKind.Const)
            {
                EmitLine();
                Emit($"readonly {varDecl.Name}");
            }
            return;
        }

        if (varDecl.Kind == VariableDeclaration.VarKind.Const)
        {
            if (isFunctionLocal)
            {
                Emit($"local -r {varDecl.Name}={value}");
                return;
            }

            Emit($"readonly {varDecl.Name}={value}");
            return;
        }

        if (isFunctionLocal)
        {
            Emit($"local {varDecl.Name}={value}");
            return;
        }

        Emit($"{varDecl.Name}={value}");
    }

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
                output.Append(GenerateInterpolatedStringLiteral(template));
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

    private void GenerateAssignment(Assignment assignment)
    {
        if (assignment.Operator == "+=")
        {
            GenerateAppendAssignment(assignment);
            return;
        }

        if (assignment.Target is IndexAccessExpression indexTarget &&
            indexTarget.Array is IdentifierExpression identifierTarget)
        {
            if (string.Equals(identifierTarget.Name, "argv", StringComparison.Ordinal))
            {
                EmitComment("Unsupported assignment target 'argv'.");
                ReportUnsupported("assignment target 'argv'");
                return;
            }

            var key = GenerateCollectionIndex(indexTarget.Index, IsCurrentScopeAssociative(identifierTarget.Name));
            var assignedValue = GenerateExpression(assignment.Value);
            Emit($"{identifierTarget.Name}[{key}]={assignedValue}");
            return;
        }

        if (assignment.Target is IdentifierExpression ident)
        {
            var value = GenerateAssignmentValue(assignment.Value);
            Emit($"{ident.Name}={value}");
            return;
        }

        EmitComment($"Unsupported assignment target '{assignment.Target.GetType().Name}'.");
        ReportUnsupported($"assignment target '{assignment.Target.GetType().Name}'");
    }

    private void GenerateFunctionDeclaration(FunctionDeclaration func)
    {
        var previousFunctionName = currentFunctionName;
        currentFunctionName = func.Name;

        EmitLine($"{func.Name}() {{");
        indentLevel++;
        EmitLine($"local -a {ArgvRuntimeName}=(\"$@\")");

        for (int i = 0; i < func.Parameters.Count; i++)
        {
            var param = func.Parameters[i];
            if (param.DefaultValue == null)
            {
                EmitLine($"local {param.Name}=\"${i + 1}\"");
                continue;
            }

            EmitLine($"local {param.Name}=\"${{{i + 1}-}}\"");
            if (param.DefaultValue != null)
            {
                var defaultValue = GenerateExpression(param.DefaultValue);
                EmitLine($"if (( $# < {i + 1} )); then {param.Name}={defaultValue}; fi");
            }
        }

        if (func.Parameters.Count > 0)
            EmitLine();

        foreach (var stmt in func.Body)
        {
            GenerateStatement(stmt);
            EmitLine();
        }

        indentLevel--;
        Emit("}");

        currentFunctionName = previousFunctionName;
    }

    private void GenerateIfStatement(IfStatement ifStmt)
    {
        var condition = GenerateCondition(ifStmt.Condition);
        Emit($"if {condition}; then");
        indentLevel++;

        foreach (var stmt in ifStmt.ThenBlock)
        {
            EmitLine();
            GenerateStatement(stmt);
        }

        indentLevel--;

        foreach (var elifClause in ifStmt.ElifClauses)
        {
            EmitLine();
            var elifCondition = GenerateCondition(elifClause.Condition);
            Emit($"elif {elifCondition}; then");
            indentLevel++;

            foreach (var stmt in elifClause.Body)
            {
                EmitLine();
                GenerateStatement(stmt);
            }

            indentLevel--;
        }

        if (ifStmt.ElseBlock.Count > 0)
        {
            EmitLine();
            Emit("else");
            indentLevel++;

            foreach (var stmt in ifStmt.ElseBlock)
            {
                EmitLine();
                GenerateStatement(stmt);
            }

            indentLevel--;
        }

        EmitLine();
        Emit("fi");
    }

    private void GenerateForLoop(ForLoop forLoop)
    {
        string rangeExpr;
        if (!string.IsNullOrEmpty(forLoop.GlobPattern))
        {
            rangeExpr = forLoop.GlobPattern!;
        }
        else if (forLoop.Range is RangeExpression range)
        {
            var start = GenerateExpression(range.Start);
            var end = GenerateExpression(range.End);

            if (forLoop.Step != null)
            {
                var step = GenerateExpression(forLoop.Step);
                rangeExpr = $"$(seq {start} {step} {end})";
            }
            else
            {
                rangeExpr = $"$(seq {start} {end})";
            }
        }
        else
        {
            if (forLoop.Range is null)
                throw new InvalidOperationException("Non-glob for-loop is missing its iterable expression.");

            rangeExpr = forLoop.Range is IdentifierExpression ident
                ? string.Equals(ident.Name, "argv", StringComparison.Ordinal)
                    ? $"\"${{{ArgvRuntimeName}[@]}}\""
                    : $"\"${{{ident.Name}[@]}}\""
                : GenerateExpression(forLoop.Range);
        }

        Emit($"for {forLoop.Variable} in {rangeExpr}; do");
        indentLevel++;

        foreach (var stmt in forLoop.Body)
        {
            EmitLine();
            GenerateStatement(stmt);
        }

        indentLevel--;
        EmitLine();
        Emit("done");
    }

    private void GenerateSwitchStatement(SwitchStatement switchStatement)
    {
        var switchValue = GenerateExpression(switchStatement.Value);
        Emit($"case {switchValue} in");
        indentLevel++;

        foreach (var caseClause in switchStatement.Cases)
        {
            EmitLine();
            Emit($"{GenerateSwitchPattern(caseClause.Pattern)})");
            indentLevel++;

            foreach (var statement in caseClause.Body)
            {
                EmitLine();
                GenerateStatement(statement);
            }

            EmitLine();
            Emit(";;");
            indentLevel--;
        }

        indentLevel--;
        EmitLine();
        Emit("esac");
    }

    private string GenerateSwitchPattern(Expression pattern)
    {
        if (pattern is LiteralExpression literal
            && literal.LiteralType is PrimitiveType { PrimitiveKind: PrimitiveType.Kind.String })
        {
            return EscapeCasePattern(literal.Value?.ToString() ?? string.Empty);
        }

        return GenerateExpression(pattern);
    }

    private void GenerateWhileLoop(WhileLoop whileLoop)
    {
        var condition = GenerateCondition(whileLoop.Condition);
        Emit($"while {condition}; do");
        indentLevel++;

        foreach (var stmt in whileLoop.Body)
        {
            EmitLine();
            GenerateStatement(stmt);
        }

        indentLevel--;
        EmitLine();
        Emit("done");
    }

    private void GenerateUntilLoop(UntilLoop untilLoop)
    {
        var condition = GenerateCondition(untilLoop.Condition);
        Emit($"until {condition}; do");
        indentLevel++;

        foreach (var stmt in untilLoop.Body)
        {
            EmitLine();
            GenerateStatement(stmt);
        }

        indentLevel--;
        EmitLine();
        Emit("done");
    }

    private void GenerateReturnStatement(ReturnStatement returnStmt)
    {
        if (returnStmt.Value != null)
        {
            var value = GenerateExpression(returnStmt.Value);
            Emit($"echo {value}");
            EmitLine();
            Emit("return 0");
        }
        else
        {
            Emit("return 0");
        }
    }

    private void GenerateExpressionStatement(Expression expression)
    {
        switch (expression)
        {
            case BinaryExpression binaryExpression when IsComparisonRedirect(binaryExpression):
                Emit(GenerateBinaryRedirectStatement(binaryExpression));
                return;

            case FunctionCallExpression call:
                Emit(GenerateFunctionCallStatement(call));
                return;

            case PipeExpression pipeExpression:
                Emit(GeneratePipeStatement(pipeExpression));
                return;

            case RedirectExpression redirectExpression:
                if (string.Equals(redirectExpression.Operator, "<<", StringComparison.Ordinal))
                {
                    GenerateHeredocStatement(redirectExpression);
                }
                else
                {
                    Emit(GenerateRedirectStatement(redirectExpression));
                }
                return;
        }

        Emit(GenerateExpression(expression));
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
        if (!TryGenerateShellPayload(shellStatement.Command, out var payload))
        {
            EmitComment("Unsupported 'sh' command payload.");
            ReportUnsupported("sh command payload");
            return;
        }

        Emit(payload);
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
            return $"\"${{{ArgvRuntimeName}[@]}}\"";
        }

        var rendered = GenerateExpression(expression);
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
            var value = GenerateExpression(valueExpression);
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
            EmitComment("Unsupported heredoc payload; expected non-interpolated string literal.");
            ReportUnsupported("heredoc payload");
            return;
        }

        var command = redirect.Left switch
        {
            FunctionCallExpression call => GenerateFunctionCallStatement(call),
            PipeExpression pipe => GeneratePipeStatement(pipe),
            _ => $"echo {GenerateSingleShellArg(redirect.Left)}"
        };

        var delimiter = ChooseHeredocDelimiter(payload);
        Emit($"{command} <<'{delimiter}'");
        EmitLine();

        var lines = payload.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        foreach (var line in lines)
        {
            Emit(line);
            EmitLine();
        }

        Emit(delimiter);
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

    private string GenerateCondition(Expression condition)
    {
        return condition switch
        {
            BinaryExpression bin when IsComparisonOperator(bin.Operator) => GenerateComparisonCondition(bin),
            BinaryExpression bin when bin.Operator == "&&" =>
                $"{GenerateCondition(bin.Left)} && {GenerateCondition(bin.Right)}",
            BinaryExpression bin when bin.Operator == "||" =>
                $"{GenerateCondition(bin.Left)} || {GenerateCondition(bin.Right)}",
            UnaryExpression { Operator: "!" } unary => $"! {GenerateCondition(unary.Operand)}",
            _ => GenerateNumericTruthinessCondition(condition)
        };
    }

    private static bool IsComparisonOperator(string op)
    {
        return op is "==" or "!=" or "<" or ">" or "<=" or ">=";
    }

    private string GenerateComparisonCondition(BinaryExpression comparison)
    {
        if (comparison.Operator is "==" or "!=")
        {
            var leftString = GenerateExpression(comparison.Left);
            var rightString = GenerateExpression(comparison.Right);
            return $"[[ {leftString} {comparison.Operator} {rightString} ]]";
        }

        var left = GenerateArithmeticExpression(comparison.Left);
        var right = GenerateArithmeticExpression(comparison.Right);

        return $"(( {left} {comparison.Operator} {right} ))";
    }

    private string GenerateNumericTruthinessCondition(Expression condition)
    {
        if (condition is IdentifierExpression identifier)
        {
            if (string.Equals(identifier.Name, "argv", StringComparison.Ordinal))
                return $"(( ${{#{ArgvRuntimeName}[@]}} != 0 ))";

            return $"(( {identifier.Name} != 0 ))";
        }

        var expr = GenerateExpression(condition);
        if (expr.StartsWith("$((", StringComparison.Ordinal) &&
            expr.EndsWith("))", StringComparison.Ordinal))
        {
            var inner = expr[3..^2].Trim();
            return $"(( {inner} ))";
        }

        if (int.TryParse(expr, out _))
            return $"(( {expr} != 0 ))";

        return $"[ {expr} -ne 0 ]";
    }

    private string GenerateVariableDeclarationValue(Expression value)
    {
        if (value is IdentifierExpression identifier &&
            string.Equals(identifier.Name, "argv", StringComparison.Ordinal))
        {
            return $"(\"${{{ArgvRuntimeName}[@]}}\")";
        }

        return GenerateExpression(value);
    }

    private string GenerateAssignmentValue(Expression value)
    {
        if (value is IdentifierExpression identifier &&
            string.Equals(identifier.Name, "argv", StringComparison.Ordinal))
        {
            return $"(\"${{{ArgvRuntimeName}[@]}}\")";
        }

        return GenerateExpression(value);
    }

    private void GenerateAppendAssignment(Assignment assignment)
    {
        if (assignment.Target is not IdentifierExpression identifier)
        {
            EmitComment("Unsupported assignment target for '+='.");
            ReportUnsupported("assignment target for '+='");
            return;
        }

        string appendValue = assignment.Value switch
        {
            ArrayLiteral array => GenerateArrayLiteral(array),
            IdentifierExpression rhsIdentifier when string.Equals(rhsIdentifier.Name, "argv", StringComparison.Ordinal) =>
                $"(\"${{{ArgvRuntimeName}[@]}}\")",
            IdentifierExpression rhsIdentifier => $"(\"${{{rhsIdentifier.Name}[@]}}\")",
            _ => HandleUnsupportedExpression(assignment.Value, "array append value")
        };

        Emit($"{identifier.Name}+={appendValue}");
    }

    private void GenerateShiftStatement(ShiftStatement shiftStatement)
    {
        var amount = shiftStatement.Amount == null
            ? "1"
            : GenerateArithmeticExpression(shiftStatement.Amount);

        Emit($"__lash_shift_n=$(( {amount} ))");
        EmitLine();
        Emit($"if (( __lash_shift_n > 0 )); then {ArgvRuntimeName}=(\"${{{ArgvRuntimeName}[@]:__lash_shift_n}}\"); fi");
    }

    private void GenerateSubshellStatement(SubshellStatement subshellStatement)
    {
        Emit("(");
        indentLevel++;

        foreach (var stmt in subshellStatement.Body)
        {
            EmitLine();
            GenerateStatement(stmt);
        }

        indentLevel--;
        EmitLine();
        Emit(")");

        if (subshellStatement.RunInBackground)
        {
            Emit(" &");

            if (!string.IsNullOrEmpty(subshellStatement.IntoVariable))
            {
                EmitLine();
                EmitIntoCaptureAssignment(
                    subshellStatement.IntoVariable!,
                    "$!",
                    subshellStatement.IntoCreatesVariable,
                    subshellStatement.IntoCreatesConst);
            }

            if (needsTrackedJobs)
            {
                EmitLine();
                Emit($"{TrackedJobsRuntimeName}+=(\"$!\")");
            }

            return;
        }

        if (!string.IsNullOrEmpty(subshellStatement.IntoVariable))
        {
            EmitLine();
            EmitIntoCaptureAssignment(
                subshellStatement.IntoVariable!,
                "$?",
                subshellStatement.IntoCreatesVariable,
                subshellStatement.IntoCreatesConst);
        }
    }

    private void GenerateWaitStatement(WaitStatement waitStatement)
    {
        switch (waitStatement.TargetKind)
        {
            case WaitTargetKind.Jobs:
                GenerateWaitJobsStatement(waitStatement);
                return;

            case WaitTargetKind.Target:
                if (waitStatement.Target != null)
                {
                    var target = GenerateSingleShellArg(waitStatement.Target);
                    Emit($"wait {target}");
                }
                else
                {
                    Emit("wait");
                }
                break;

            default:
                Emit("wait");
                break;
        }

        if (!string.IsNullOrEmpty(waitStatement.IntoVariable))
        {
            EmitLine();
            EmitIntoCaptureAssignment(
                waitStatement.IntoVariable!,
                "$?",
                waitStatement.IntoCreatesVariable,
                waitStatement.IntoCreatesConst);
        }
    }

    private void GenerateWaitJobsStatement(WaitStatement waitStatement)
    {
        if (!string.IsNullOrEmpty(waitStatement.IntoVariable))
        {
            EmitIntoCaptureAssignment(
                waitStatement.IntoVariable!,
                "0",
                waitStatement.IntoCreatesVariable,
                waitStatement.IntoCreatesConst);
            EmitLine();
        }

        Emit($"for {WaitPidRuntimeName} in \"${{{TrackedJobsRuntimeName}[@]}}\"; do");
        indentLevel++;
        EmitLine();
        Emit($"wait \"${{{WaitPidRuntimeName}}}\"");

        if (!string.IsNullOrEmpty(waitStatement.IntoVariable))
        {
            EmitLine();
            Emit($"{waitStatement.IntoVariable}=$?");
        }

        indentLevel--;
        EmitLine();
        Emit("done");
        EmitLine();
        Emit($"{TrackedJobsRuntimeName}=()");
    }

    private void EmitIntoCaptureAssignment(string name, string value, bool createsVariable, bool createsConst)
    {
        if (createsVariable)
        {
            if (createsConst)
            {
                if (currentFunctionName != null)
                {
                    Emit($"local -r {name}={value}");
                    return;
                }

                Emit($"readonly {name}={value}");
                return;
            }

            if (currentFunctionName != null)
            {
                Emit($"local {name}={value}");
                return;
            }
        }

        Emit($"{name}={value}");
    }

    private static string EscapeCasePattern(string pattern)
    {
        if (pattern.Length == 0)
            return "\"\"";

        if (pattern.IndexOfAny(['"', '`', '$', '\\']) >= 0)
            return $"\"{pattern.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("$", "\\$", StringComparison.Ordinal).Replace("`", "\\`", StringComparison.Ordinal)}\"";

        return pattern;
    }
}
