namespace Lash.Compiler.Frontend.Semantics;

using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;
using Lash.Compiler.Diagnostics;

public sealed class DefiniteAssignmentAnalyzer
{
    private readonly DiagnosticBag diagnostics;
    private readonly Stack<Dictionary<string, bool>> scopes = new();

    public DefiniteAssignmentAnalyzer(DiagnosticBag diagnostics)
    {
        this.diagnostics = diagnostics;
        scopes.Push(new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["argv"] = true
        });
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
                Declare(variable.Name, IsInitialized(variable.Value), variable.IsGlobal);
                break;

            case Assignment assignment:
                CheckExpression(assignment.Value);
                if (assignment.Target is IdentifierExpression target)
                {
                    SetAssigned(target.Name, assignment.IsGlobal);
                }
                else if (assignment.Target is IndexAccessExpression indexAccess)
                {
                    CheckExpression(indexAccess.Array);
                    CheckExpression(indexAccess.Index);
                    if (indexAccess.Array is IdentifierExpression ident)
                        SetAssigned(ident.Name, assignment.IsGlobal);
                }
                break;

            case FunctionDeclaration function:
                PushScope();
                foreach (var parameter in function.Parameters)
                {
                    if (parameter.DefaultValue != null)
                        CheckExpression(parameter.DefaultValue);
                    Declare(parameter.Name, assigned: true, isGlobal: false);
                }

                foreach (var nested in function.Body)
                    CheckStatement(nested);
                PopScope();
                break;

            case IfStatement ifStatement:
                CheckExpression(ifStatement.Condition);
                MergeConditionalBranches(ifStatement);
                break;

            case SwitchStatement switchStatement:
                CheckExpression(switchStatement.Value);
                foreach (var clause in switchStatement.Cases)
                {
                    CheckExpression(clause.Pattern);
                    AnalyzeIsolatedBlock(clause.Body);
                }
                break;

            case ForLoop forLoop:
                if (forLoop.Range != null)
                    CheckExpression(forLoop.Range);
                if (forLoop.Step != null)
                    CheckExpression(forLoop.Step);

                AnalyzeLoopBody(forLoop.Variable, forLoop.Body);
                break;
            case SelectLoop selectLoop:
                if (selectLoop.Options != null)
                    CheckExpression(selectLoop.Options);
                AnalyzeLoopBody(selectLoop.Variable, selectLoop.Body);
                break;

            case WhileLoop whileLoop:
                CheckExpression(whileLoop.Condition);
                AnalyzeIsolatedBlock(whileLoop.Body);
                break;

            case UntilLoop untilLoop:
                CheckExpression(untilLoop.Condition);
                AnalyzeIsolatedBlock(untilLoop.Body);
                break;

            case ReturnStatement returnStatement when returnStatement.Value != null:
                CheckExpression(returnStatement.Value);
                break;

            case ShiftStatement shiftStatement when shiftStatement.Amount != null:
                CheckExpression(shiftStatement.Amount);
                break;

            case SubshellStatement subshellStatement:
                AnalyzeIsolatedBlock(subshellStatement.Body);
                if (!string.IsNullOrEmpty(subshellStatement.IntoVariable))
                    SetAssigned(subshellStatement.IntoVariable!, isGlobal: false);
                break;
            case CoprocStatement coprocStatement:
                AnalyzeIsolatedBlock(coprocStatement.Body);
                if (!string.IsNullOrEmpty(coprocStatement.IntoVariable))
                    SetAssigned(coprocStatement.IntoVariable!, isGlobal: false);
                break;

            case WaitStatement waitStatement:
                if (waitStatement.TargetKind == WaitTargetKind.Target && waitStatement.Target != null)
                    CheckExpression(waitStatement.Target);
                if (!string.IsNullOrEmpty(waitStatement.IntoVariable))
                    SetAssigned(waitStatement.IntoVariable!, isGlobal: false);
                break;

            case ShellStatement shellStatement:
                CheckExpression(shellStatement.Command);
                break;
            case TestStatement testStatement:
                CheckExpression(testStatement.Condition);
                break;

            case ExpressionStatement expressionStatement:
                CheckExpression(expressionStatement.Expression);
                break;
        }
    }

    private void MergeConditionalBranches(IfStatement ifStatement)
    {
        var baseline = CloneScope();
        var branchResults = new List<Dictionary<string, bool>>();

        branchResults.Add(AnalyzeBranchFromBaseline(ifStatement.ThenBlock, baseline));

        foreach (var clause in ifStatement.ElifClauses)
        {
            CheckExpression(clause.Condition);
            branchResults.Add(AnalyzeBranchFromBaseline(clause.Body, baseline));
        }

        if (ifStatement.ElseBlock.Count > 0)
        {
            branchResults.Add(AnalyzeBranchFromBaseline(ifStatement.ElseBlock, baseline));
        }
        else
        {
            branchResults.Add(new Dictionary<string, bool>(baseline, StringComparer.Ordinal));
        }

        var merged = new Dictionary<string, bool>(baseline, StringComparer.Ordinal);
        foreach (var key in baseline.Keys)
        {
            merged[key] = branchResults.All(result => result.TryGetValue(key, out var assigned) && assigned);
        }

        ReplaceCurrentScope(merged);
    }

    private Dictionary<string, bool> AnalyzeBranchFromBaseline(IEnumerable<Statement> statements, Dictionary<string, bool> baseline)
    {
        PushScope(new Dictionary<string, bool>(baseline, StringComparer.Ordinal));
        foreach (var statement in statements)
            CheckStatement(statement);
        var snapshot = CloneScope();
        PopScope();
        return snapshot;
    }

    private void AnalyzeLoopBody(string loopVariable, IEnumerable<Statement> body)
    {
        PushScope();
        Declare(loopVariable, assigned: true, isGlobal: false);
        foreach (var nested in body)
            CheckStatement(nested);
        PopScope();
    }

    private void AnalyzeIsolatedBlock(IEnumerable<Statement> statements)
    {
        PushScope();
        foreach (var nested in statements)
            CheckStatement(nested);
        PopScope();
    }

    private void CheckExpression(Expression expression)
    {
        switch (expression)
        {
            case IdentifierExpression identifier:
                if (string.Equals(identifier.Name, "argv", StringComparison.Ordinal))
                    return;

                if (!TryResolveAssigned(identifier.Name, out var assigned))
                    return;

                if (!assigned)
                {
                    diagnostics.AddError(
                        DiagnosticMessage.WithTip(
                            $"Variable '{identifier.Name}' may be used before it is initialized.",
                            "Initialize the variable before reading it, or assign on all control-flow paths."),
                        identifier.Line,
                        identifier.Column,
                        DiagnosticCodes.MaybeUninitializedVariable);
                }
                break;

            case FunctionCallExpression functionCall:
                foreach (var argument in functionCall.Arguments)
                    CheckExpression(argument);
                break;

            case ShellCaptureExpression shellCapture:
                CheckExpression(shellCapture.Command);
                break;
            case TestCaptureExpression testCapture:
                CheckExpression(testCapture.Condition);
                break;

            case PipeExpression pipe:
                CheckExpression(pipe.Left);
                if (pipe.Right is IdentifierExpression sink)
                    SetAssigned(sink.Name, isGlobal: false);
                else
                    CheckExpression(pipe.Right);
                break;

            case RedirectExpression redirect:
                CheckExpression(redirect.Left);
                CheckExpression(redirect.Right);
                break;

            case UnaryExpression unary:
                CheckExpression(unary.Operand);
                break;

            case BinaryExpression binary:
                CheckExpression(binary.Left);
                CheckExpression(binary.Right);
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

    private void PushScope()
    {
        PushScope(new Dictionary<string, bool>(scopes.Peek(), StringComparer.Ordinal));
    }

    private void PushScope(Dictionary<string, bool> scope)
    {
        scopes.Push(scope);
    }

    private void PopScope()
    {
        scopes.Pop();
    }

    private void ReplaceCurrentScope(Dictionary<string, bool> scope)
    {
        scopes.Pop();
        scopes.Push(scope);
    }

    private void Declare(string name, bool assigned, bool isGlobal)
    {
        if (isGlobal)
        {
            var global = scopes.Last();
            global[name] = assigned;
            return;
        }

        scopes.Peek()[name] = assigned;
    }

    private void SetAssigned(string name, bool isGlobal)
    {
        if (string.Equals(name, "argv", StringComparison.Ordinal))
            return;

        if (isGlobal)
        {
            var global = scopes.Last();
            global[name] = true;
            return;
        }

        foreach (var scope in scopes)
        {
            if (!scope.ContainsKey(name))
                continue;

            scope[name] = true;
            return;
        }

        scopes.Peek()[name] = true;
    }

    private bool TryResolveAssigned(string name, out bool assigned)
    {
        foreach (var scope in scopes)
        {
            if (scope.TryGetValue(name, out assigned))
                return true;
        }

        assigned = false;
        return false;
    }

    private Dictionary<string, bool> CloneScope()
    {
        return new Dictionary<string, bool>(scopes.Peek(), StringComparer.Ordinal);
    }

    private static bool IsInitialized(Expression value)
    {
        return value is not NullLiteral;
    }
}
