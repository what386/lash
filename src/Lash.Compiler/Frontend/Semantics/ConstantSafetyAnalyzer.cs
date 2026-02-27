namespace Lash.Compiler.Frontend.Semantics;

using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;
using Lash.Compiler.Ast.Types;
using Lash.Compiler.Diagnostics;

public sealed class ConstantSafetyAnalyzer
{
    private readonly DiagnosticBag diagnostics;
    private readonly Stack<Dictionary<string, int>> scopes = new();

    public ConstantSafetyAnalyzer(DiagnosticBag diagnostics)
    {
        this.diagnostics = diagnostics;
        scopes.Push(new Dictionary<string, int>(StringComparer.Ordinal));
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
                CheckExpression(variable.Value);
                if (variable.Kind == VariableDeclaration.VarKind.Const && TryEvaluateInt(variable.Value, out var value))
                    DeclareConstInt(variable.Name, value, variable.IsGlobal);
                else
                    RemoveConstInt(variable.Name, variable.IsGlobal);
                break;

            case Assignment assignment:
                CheckExpression(assignment.Value);
                if (assignment.Target is IdentifierExpression identifier)
                    RemoveConstInt(identifier.Name, assignment.IsGlobal);
                break;

            case FunctionDeclaration function:
                PushScope();
                foreach (var parameter in function.Parameters)
                {
                    if (parameter.DefaultValue != null)
                        CheckExpression(parameter.DefaultValue);
                }
                foreach (var nested in function.Body)
                    CheckStatement(nested);
                PopScope();
                break;

            case IfStatement ifStatement:
                CheckExpression(ifStatement.Condition);
                AnalyzeIsolated(ifStatement.ThenBlock);
                foreach (var clause in ifStatement.ElifClauses)
                {
                    CheckExpression(clause.Condition);
                    AnalyzeIsolated(clause.Body);
                }
                AnalyzeIsolated(ifStatement.ElseBlock);
                break;

            case SwitchStatement switchStatement:
                CheckExpression(switchStatement.Value);
                foreach (var clause in switchStatement.Cases)
                {
                    CheckExpression(clause.Pattern);
                    AnalyzeIsolated(clause.Body);
                }
                break;

            case ForLoop forLoop:
                if (forLoop.Range != null)
                    CheckExpression(forLoop.Range);
                if (forLoop.Step != null)
                {
                    CheckExpression(forLoop.Step);
                    if (TryEvaluateInt(forLoop.Step, out var stepValue) && stepValue == 0)
                    {
                        diagnostics.AddError(
                            DiagnosticMessage.WithTip(
                                "For-loop step cannot be 0.",
                                "Use a positive or negative non-zero step value."),
                            forLoop.Step.Line,
                            forLoop.Step.Column,
                            DiagnosticCodes.InvalidForStep);
                    }
                }
                AnalyzeIsolated(forLoop.Body);
                break;

            case WhileLoop whileLoop:
                CheckExpression(whileLoop.Condition);
                AnalyzeIsolated(whileLoop.Body);
                break;

            case ReturnStatement returnStatement when returnStatement.Value != null:
                CheckExpression(returnStatement.Value);
                break;

            case ShiftStatement shiftStatement when shiftStatement.Amount != null:
                CheckExpression(shiftStatement.Amount);
                if (TryEvaluateInt(shiftStatement.Amount, out var shiftAmount) && shiftAmount < 0)
                {
                    diagnostics.AddError(
                        DiagnosticMessage.WithTip(
                            "Shift amount cannot be negative.",
                            "Use shift 0 or a positive integer."),
                        shiftStatement.Amount.Line,
                        shiftStatement.Amount.Column,
                        DiagnosticCodes.InvalidShiftAmount);
                }
                break;

            case SubshellStatement subshellStatement:
                AnalyzeIsolated(subshellStatement.Body);
                break;

            case WaitStatement waitStatement when waitStatement.TargetKind == WaitTargetKind.Target && waitStatement.Target != null:
                CheckExpression(waitStatement.Target);
                break;

            case ShellStatement shellStatement:
                CheckExpression(shellStatement.Command);
                break;

            case ExpressionStatement expressionStatement:
                CheckExpression(expressionStatement.Expression);
                break;
        }
    }

    private void CheckExpression(Expression expression)
    {
        switch (expression)
        {
            case BinaryExpression binary:
                CheckExpression(binary.Left);
                CheckExpression(binary.Right);

                if (binary.Operator is "/" or "%" &&
                    TryEvaluateInt(binary.Right, out var right) &&
                    right == 0)
                {
                    diagnostics.AddError(
                        DiagnosticMessage.WithTip(
                            $"Operator '{binary.Operator}' cannot use a right-hand operand of 0.",
                            "Ensure the divisor/modulo operand is non-zero."),
                        binary.Right.Line,
                        binary.Right.Column,
                        DiagnosticCodes.DivisionOrModuloByZero);
                }
                break;

            case UnaryExpression unary:
                CheckExpression(unary.Operand);
                break;

            case FunctionCallExpression functionCall:
                foreach (var argument in functionCall.Arguments)
                    CheckExpression(argument);
                break;

            case ShellCaptureExpression shellCapture:
                CheckExpression(shellCapture.Command);
                break;

            case PipeExpression pipe:
                CheckExpression(pipe.Left);
                CheckExpression(pipe.Right);
                break;

            case RedirectExpression redirect:
                CheckExpression(redirect.Left);
                CheckExpression(redirect.Right);
                break;

            case IndexAccessExpression indexAccess:
                CheckExpression(indexAccess.Array);
                CheckExpression(indexAccess.Index);
                break;

            case ArrayLiteral arrayLiteral:
                foreach (var element in arrayLiteral.Elements)
                    CheckExpression(element);
                break;
        }
    }

    private bool TryEvaluateInt(Expression expression, out int value)
    {
        switch (expression)
        {
            case LiteralExpression literal when literal.LiteralType is PrimitiveType { PrimitiveKind: PrimitiveType.Kind.Int }:
                if (literal.Value is int i)
                {
                    value = i;
                    return true;
                }
                break;

            case IdentifierExpression identifier:
                if (TryResolveConstInt(identifier.Name, out value))
                    return true;
                break;

            case UnaryExpression unary:
                if (TryEvaluateInt(unary.Operand, out var operand))
                {
                    switch (unary.Operator)
                    {
                        case "+":
                            value = operand;
                            return true;
                        case "-":
                            value = -operand;
                            return true;
                    }
                }
                break;

            case BinaryExpression binary:
                if (!TryEvaluateInt(binary.Left, out var left) || !TryEvaluateInt(binary.Right, out var right))
                    break;

                switch (binary.Operator)
                {
                    case "+":
                        value = left + right;
                        return true;
                    case "-":
                        value = left - right;
                        return true;
                    case "*":
                        value = left * right;
                        return true;
                    case "/":
                        if (right == 0)
                            break;
                        value = left / right;
                        return true;
                    case "%":
                        if (right == 0)
                            break;
                        value = left % right;
                        return true;
                }
                break;
        }

        value = 0;
        return false;
    }

    private void AnalyzeIsolated(IEnumerable<Statement> statements)
    {
        PushScope();
        foreach (var statement in statements)
            CheckStatement(statement);
        PopScope();
    }

    private void PushScope()
    {
        scopes.Push(new Dictionary<string, int>(scopes.Peek(), StringComparer.Ordinal));
    }

    private void PopScope()
    {
        scopes.Pop();
    }

    private void DeclareConstInt(string name, int value, bool isGlobal)
    {
        if (isGlobal)
        {
            var global = scopes.Last();
            global[name] = value;
            return;
        }

        scopes.Peek()[name] = value;
    }

    private void RemoveConstInt(string name, bool isGlobal)
    {
        if (isGlobal)
        {
            scopes.Last().Remove(name);
            return;
        }

        scopes.Peek().Remove(name);
    }

    private bool TryResolveConstInt(string name, out int value)
    {
        foreach (var scope in scopes)
        {
            if (scope.TryGetValue(name, out value))
                return true;
        }

        value = 0;
        return false;
    }
}
