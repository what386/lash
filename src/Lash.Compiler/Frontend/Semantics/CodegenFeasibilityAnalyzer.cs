namespace Lash.Compiler.Frontend.Semantics;

using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;
using Lash.Compiler.Ast.Types;
using Lash.Compiler.Diagnostics;

public sealed class CodegenFeasibilityAnalyzer
{
    private readonly DiagnosticBag diagnostics;

    public CodegenFeasibilityAnalyzer(DiagnosticBag diagnostics)
    {
        this.diagnostics = diagnostics;
    }

    public void Analyze(ProgramNode program)
    {
        foreach (var statement in program.Statements)
            CheckStatement(statement);
    }

    private void CheckStatement(Statement statement)
    {
        switch (statement)
        {
            case VariableDeclaration variable:
                ValidateValueExpression(variable.Value, ValueContext.VariableInitializer);
                break;

            case Assignment assignment:
                ValidateAssignmentTarget(assignment);
                if (assignment.Operator == "+=")
                {
                    ValidateAppendValueExpression(assignment.Value);
                }
                else
                {
                    ValidateValueExpression(assignment.Value, ValueContext.AssignmentRhs);
                }
                break;

            case FunctionDeclaration function:
                foreach (var parameter in function.Parameters)
                {
                    if (parameter.DefaultValue != null)
                        ValidateValueExpression(parameter.DefaultValue, ValueContext.GeneralValue);
                }
                foreach (var nested in function.Body)
                    CheckStatement(nested);
                break;

            case IfStatement ifStatement:
                ValidateValueExpression(ifStatement.Condition, ValueContext.Condition);
                foreach (var nested in ifStatement.ThenBlock)
                    CheckStatement(nested);
                foreach (var clause in ifStatement.ElifClauses)
                {
                    ValidateValueExpression(clause.Condition, ValueContext.Condition);
                    foreach (var nested in clause.Body)
                        CheckStatement(nested);
                }
                foreach (var nested in ifStatement.ElseBlock)
                    CheckStatement(nested);
                break;

            case SwitchStatement switchStatement:
                ValidateValueExpression(switchStatement.Value, ValueContext.GeneralValue);
                foreach (var clause in switchStatement.Cases)
                {
                    ValidateValueExpression(clause.Pattern, ValueContext.GeneralValue);
                    foreach (var nested in clause.Body)
                        CheckStatement(nested);
                }
                break;

            case ForLoop forLoop:
                if (forLoop.Range != null)
                    ValidateValueExpression(forLoop.Range, ValueContext.ForRange);
                if (forLoop.Step != null)
                    ValidateValueExpression(forLoop.Step, ValueContext.GeneralValue);
                foreach (var nested in forLoop.Body)
                    CheckStatement(nested);
                break;
            case SelectLoop selectLoop:
                if (selectLoop.Options != null)
                    ValidateValueExpression(selectLoop.Options, ValueContext.ForRange);
                foreach (var nested in selectLoop.Body)
                    CheckStatement(nested);
                break;

            case WhileLoop whileLoop:
                ValidateValueExpression(whileLoop.Condition, ValueContext.Condition);
                foreach (var nested in whileLoop.Body)
                    CheckStatement(nested);
                break;

            case UntilLoop untilLoop:
                ValidateValueExpression(untilLoop.Condition, ValueContext.Condition);
                foreach (var nested in untilLoop.Body)
                    CheckStatement(nested);
                break;

            case ReturnStatement returnStatement when returnStatement.Value != null:
                ValidateValueExpression(returnStatement.Value, ValueContext.GeneralValue);
                break;

            case ShiftStatement shiftStatement when shiftStatement.Amount != null:
                ValidateValueExpression(shiftStatement.Amount, ValueContext.GeneralValue);
                break;

            case SubshellStatement subshellStatement:
                foreach (var nested in subshellStatement.Body)
                    CheckStatement(nested);
                break;
            case CoprocStatement coprocStatement:
                foreach (var nested in coprocStatement.Body)
                    CheckStatement(nested);
                break;

            case WaitStatement waitStatement when waitStatement.TargetKind == WaitTargetKind.Target && waitStatement.Target != null:
                ValidateValueExpression(waitStatement.Target, ValueContext.GeneralValue);
                break;

            case ShellStatement shellStatement:
                ValidateShellPayload(shellStatement.Command, shellStatement.Line, shellStatement.Column);
                break;

            case ExpressionStatement expressionStatement:
                ValidateExpressionStatement(expressionStatement.Expression);
                break;
        }
    }

    private void ValidateExpressionStatement(Expression expression)
    {
        if (expression is BinaryExpression binary && binary.Operator is ">" or "<")
        {
            ValidateValueExpression(binary.Left, ValueContext.GeneralValue);
            ValidateValueExpression(binary.Right, ValueContext.GeneralValue);
            return;
        }

        switch (expression)
        {
            case FunctionCallExpression call:
                foreach (var argument in call.Arguments)
                    ValidateValueExpression(argument, ValueContext.FunctionArgument);
                return;

            case PipeExpression pipe:
                if (pipe.Right is not IdentifierExpression)
                {
                    Report(
                        pipe.Right,
                        "Pipe statement right-hand stage must be a variable assignment sink.",
                        DiagnosticCodes.UnsupportedStatementForCodegen);
                    return;
                }

                ValidateValueExpression(pipe.Left, ValueContext.GeneralValue);
                return;

            case RedirectExpression redirect:
                ValidateValueExpression(redirect.Left, ValueContext.GeneralValue);
                if (redirect.Right is not NullLiteral)
                    ValidateValueExpression(redirect.Right, ValueContext.GeneralValue);

                if (string.Equals(redirect.Operator, "<<", StringComparison.Ordinal) &&
                    !IsSupportedHeredocPayload(redirect.Right))
                {
                    Report(
                        redirect.Right,
                        "Heredoc redirection ('<<') requires a non-interpolated string literal payload.",
                        DiagnosticCodes.UnsupportedStatementForCodegen);
                }

                return;
        }

        ValidateValueExpression(expression, ValueContext.GeneralValue);
    }

    private void ValidateAssignmentTarget(Assignment assignment)
    {
        switch (assignment.Target)
        {
            case IdentifierExpression identifier when string.Equals(identifier.Name, "argv", StringComparison.Ordinal):
                Report(
                    identifier,
                    "Assignment target 'argv' is not supported by Bash code generation.",
                    DiagnosticCodes.UnsupportedStatementForCodegen);
                break;

            case IndexAccessExpression indexAccess when indexAccess.Array is not IdentifierExpression:
                Report(
                    indexAccess.Array,
                    "Index assignment target must be a named variable.",
                    DiagnosticCodes.UnsupportedStatementForCodegen);
                break;

            case IdentifierExpression:
            case IndexAccessExpression:
                break;

            default:
                Report(
                    assignment.Target,
                    $"Assignment target '{assignment.Target.GetType().Name}' is not supported.",
                    DiagnosticCodes.UnsupportedStatementForCodegen);
                break;
        }
    }

    private void ValidateAppendValueExpression(Expression expression)
    {
        if (expression is ArrayLiteral)
        {
            ValidateValueExpression(expression, ValueContext.AppendValue);
            return;
        }

        if (expression is IdentifierExpression identifier &&
            string.Equals(identifier.Name, "argv", StringComparison.Ordinal))
        {
            return;
        }

        if (expression is IdentifierExpression)
        {
            return;
        }

        ValidateValueExpression(expression, ValueContext.AppendValue);
        Report(
            expression,
            "Operator '+=' only supports array literals or array variables in Bash code generation.",
            DiagnosticCodes.UnsupportedStatementForCodegen);
    }

    private void ValidateValueExpression(Expression expression, ValueContext context)
    {
        switch (expression)
        {
            case NullLiteral:
                return;

            case LiteralExpression:
            case EnumAccessExpression:
                return;

            case IdentifierExpression identifier:
                ValidateIdentifierForContext(identifier, context);
                return;

            case ShellCaptureExpression shellCapture:
                ValidateShellPayload(shellCapture.Command, shellCapture.Line, shellCapture.Column);
                return;

            case RangeExpression:
                if (context != ValueContext.ForRange)
                {
                    Report(
                        expression,
                        "Range expressions are only supported as the top-level iterable in 'for ... in'.",
                        DiagnosticCodes.UnsupportedExpressionForCodegen);
                }
                return;

            case RedirectExpression:
                Report(
                    expression,
                    "Redirect expressions are only supported as standalone expression statements.",
                    DiagnosticCodes.UnsupportedExpressionForCodegen);
                return;

            case PipeExpression pipe:
                ValidateValueExpression(pipe.Left, ValueContext.GeneralValue);
                if (pipe.Right is not FunctionCallExpression rightCall)
                {
                    Report(
                        pipe.Right,
                        "Pipe value expressions require a function-call right-hand stage.",
                        DiagnosticCodes.UnsupportedExpressionForCodegen);
                    return;
                }

                foreach (var argument in rightCall.Arguments)
                    ValidateValueExpression(argument, ValueContext.FunctionArgument);
                return;

            case IndexAccessExpression indexAccess:
                if (indexAccess.Array is not IdentifierExpression)
                {
                    Report(
                        indexAccess.Array,
                        "Index access receivers must be named variables for Bash code generation.",
                        DiagnosticCodes.UnsupportedExpressionForCodegen);
                }
                ValidateValueExpression(indexAccess.Index, ValueContext.GeneralValue);
                return;

            case UnaryExpression unary:
                if (unary.Operator == "#" && !IsSupportedLengthOperand(unary.Operand))
                {
                    Report(
                        unary.Operand,
                        "Operator '#' is only supported for identifiers, index access, arrays, and string literals in Bash code generation.",
                        DiagnosticCodes.UnsupportedExpressionForCodegen);
                }
                ValidateValueExpression(unary.Operand, unary.Operator == "#" ? ValueContext.LengthOperand : ValueContext.GeneralValue);
                return;

            case BinaryExpression binary:
                ValidateValueExpression(binary.Left, ValueContext.GeneralValue);
                ValidateValueExpression(binary.Right, ValueContext.GeneralValue);
                return;

            case FunctionCallExpression functionCall:
                foreach (var argument in functionCall.Arguments)
                    ValidateValueExpression(argument, ValueContext.FunctionArgument);
                return;

            case ArrayLiteral arrayLiteral:
                foreach (var element in arrayLiteral.Elements)
                    ValidateValueExpression(element, ValueContext.GeneralValue);
                return;

            default:
                Report(
                    expression,
                    $"Expression '{expression.GetType().Name}' is not supported by Bash code generation.",
                    DiagnosticCodes.UnsupportedExpressionForCodegen);
                return;
        }
    }

    private void ValidateIdentifierForContext(IdentifierExpression identifier, ValueContext context)
    {
        if (!string.Equals(identifier.Name, "argv", StringComparison.Ordinal))
            return;

        if (context is ValueContext.VariableInitializer or
            ValueContext.AssignmentRhs or
            ValueContext.FunctionArgument or
            ValueContext.ForRange or
            ValueContext.AppendValue or
            ValueContext.LengthOperand)
        {
            return;
        }

        Report(
            identifier,
            "Bare 'argv' is not supported in this expression context for Bash code generation.",
            DiagnosticCodes.UnsupportedExpressionForCodegen);
    }

    private static bool IsSupportedLengthOperand(Expression operand)
    {
        return operand switch
        {
            IdentifierExpression => true,
            IndexAccessExpression indexAccess when indexAccess.Array is IdentifierExpression => true,
            LiteralExpression literal when literal.LiteralType is PrimitiveType { PrimitiveKind: PrimitiveType.Kind.String } => true,
            ArrayLiteral => true,
            _ => false
        };
    }

    private void ValidateShellPayload(Expression command, int line, int column)
    {
        if (command is LiteralExpression literal &&
            literal.LiteralType is PrimitiveType { PrimitiveKind: PrimitiveType.Kind.String })
        {
            return;
        }

        diagnostics.AddError(
            DiagnosticMessage.WithTip(
                "Shell command payload must be a string literal for Bash code generation.",
                "Use a string literal/interpolated string in-place, or move dynamic construction into a plain shell command statement."),
            line,
            column,
            DiagnosticCodes.UnsupportedExpressionForCodegen);
    }

    private void Report(AstNode node, string message, string code)
    {
        var tip = code switch
        {
            DiagnosticCodes.UnsupportedExpressionForCodegen => "Rewrite this expression using a supported Bash-lowerable form.",
            DiagnosticCodes.UnsupportedStatementForCodegen => "Rewrite this statement to a supported construct or plain shell command.",
            _ => null
        };

        diagnostics.AddError(DiagnosticMessage.WithTip(message, tip), node.Line, node.Column, code);
    }

    private static bool IsSupportedHeredocPayload(Expression expression)
    {
        if (expression is not LiteralExpression literal ||
            literal.LiteralType is not PrimitiveType { PrimitiveKind: PrimitiveType.Kind.String })
        {
            return false;
        }

        return !literal.IsInterpolated;
    }

    private enum ValueContext
    {
        GeneralValue,
        VariableInitializer,
        AssignmentRhs,
        FunctionArgument,
        ForRange,
        AppendValue,
        LengthOperand,
        Condition
    }
}
