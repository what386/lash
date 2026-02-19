namespace Lash.Compiler.Frontend.Semantics;

using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;
using Lash.Compiler.Diagnostics;

public sealed class NameResolver
{
    private readonly DiagnosticBag diagnostics;
    private readonly Dictionary<string, SymbolInfo> globalScope;
    private readonly HashSet<string> globalDeclared = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> enums = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FunctionInfo> functions = new(StringComparer.Ordinal);
    private readonly Stack<Dictionary<string, SymbolInfo>> scopes = new();
    private readonly Stack<HashSet<string>> declaredInScope = new();
    private int loopDepth;
    private int functionDepth;

    public NameResolver(DiagnosticBag diagnostics)
    {
        this.diagnostics = diagnostics;
        globalScope = new Dictionary<string, SymbolInfo>(StringComparer.Ordinal);
        scopes.Push(globalScope);
        declaredInScope.Push(globalDeclared);
    }

    public void Analyze(ProgramNode program)
    {
        CollectDeclarations(program.Statements);

        foreach (var statement in program.Statements)
            CheckStatement(statement);
    }

    private void CollectDeclarations(IEnumerable<Statement> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case FunctionDeclaration function:
                    if (functions.ContainsKey(function.Name))
                    {
                        Report(
                            function,
                            $"Duplicate function declaration '{function.Name}'.",
                            DiagnosticCodes.DuplicateDeclaration);
                    }
                    else
                    {
                        var required = function.Parameters.Count(p => p.DefaultValue == null);
                        functions[function.Name] = new FunctionInfo(function.Parameters.Count, required);
                    }

                    CollectDeclarations(function.Body);
                    break;

                case EnumDeclaration enumDeclaration:
                    if (enums.ContainsKey(enumDeclaration.Name))
                    {
                        Report(
                            enumDeclaration,
                            $"Duplicate enum declaration '{enumDeclaration.Name}'.",
                            DiagnosticCodes.DuplicateDeclaration);
                    }
                    else
                    {
                        enums[enumDeclaration.Name] = new HashSet<string>(enumDeclaration.Members, StringComparer.Ordinal);
                    }
                    break;

                case IfStatement ifStatement:
                    CollectDeclarations(ifStatement.ThenBlock);
                    foreach (var elifClause in ifStatement.ElifClauses)
                        CollectDeclarations(elifClause.Body);
                    CollectDeclarations(ifStatement.ElseBlock);
                    break;

                case SwitchStatement switchStatement:
                    foreach (var clause in switchStatement.Cases)
                        CollectDeclarations(clause.Body);
                    break;

                case ForLoop forLoop:
                    CollectDeclarations(forLoop.Body);
                    break;

                case WhileLoop whileLoop:
                    CollectDeclarations(whileLoop.Body);
                    break;

                case SubshellStatement subshellStatement:
                    CollectDeclarations(subshellStatement.Body);
                    break;
            }
        }
    }

    private void CheckStatement(Statement statement)
    {
        switch (statement)
        {
            case VariableDeclaration variable:
                CheckExpression(variable.Value);
                if (IsBuiltinIdentifier(variable.Name))
                {
                    Report(
                        variable,
                        $"Cannot declare built-in variable '{variable.Name}'.",
                        DiagnosticCodes.InvalidAssignmentTarget);
                    break;
                }

                Declare(variable.Name, variable.Kind == VariableDeclaration.VarKind.Const, variable, variable.IsGlobal);
                break;

            case EnumDeclaration:
                break;

            case Assignment assignment:
                CheckExpression(assignment.Value);

                if (assignment.Target is IdentifierExpression identifier)
                    ValidateAssignmentTarget(identifier, assignment.IsGlobal);
                else if (assignment.Target is IndexAccessExpression indexAccess)
                    ValidateIndexAssignmentTarget(indexAccess);
                break;

            case FunctionDeclaration function:
                CheckFunction(function);
                break;

            case IfStatement ifStatement:
                CheckExpression(ifStatement.Condition);
                PushScope();
                foreach (var nested in ifStatement.ThenBlock)
                    CheckStatement(nested);
                PopScope();

                foreach (var elifClause in ifStatement.ElifClauses)
                {
                    CheckExpression(elifClause.Condition);
                    PushScope();
                    foreach (var nested in elifClause.Body)
                        CheckStatement(nested);
                    PopScope();
                }

                PushScope();
                foreach (var nested in ifStatement.ElseBlock)
                    CheckStatement(nested);
                PopScope();
                break;

            case SwitchStatement switchStatement:
                CheckExpression(switchStatement.Value);
                foreach (var clause in switchStatement.Cases)
                {
                    CheckExpression(clause.Pattern);
                    PushScope();
                    foreach (var nested in clause.Body)
                        CheckStatement(nested);
                    PopScope();
                }
                break;

            case ForLoop forLoop:
                CheckExpression(forLoop.Range);
                if (forLoop.Step != null)
                    CheckExpression(forLoop.Step);

                PushScope();
                loopDepth++;
                Declare(forLoop.Variable, isConst: false, forLoop);
                foreach (var nested in forLoop.Body)
                    CheckStatement(nested);
                loopDepth--;
                PopScope();
                break;

            case WhileLoop whileLoop:
                CheckExpression(whileLoop.Condition);
                PushScope();
                loopDepth++;
                foreach (var nested in whileLoop.Body)
                    CheckStatement(nested);
                loopDepth--;
                PopScope();
                break;

            case BreakStatement:
                if (loopDepth == 0)
                {
                    Report(
                        statement,
                        "'break' can only be used inside a loop.",
                        DiagnosticCodes.InvalidControlFlowContext);
                }
                break;

            case ContinueStatement:
                if (loopDepth == 0)
                {
                    Report(
                        statement,
                        "'continue' can only be used inside a loop.",
                        DiagnosticCodes.InvalidControlFlowContext);
                }
                break;

            case ReturnStatement returnStatement:
                if (functionDepth == 0)
                {
                    Report(
                        returnStatement,
                        "'return' can only be used inside a function.",
                        DiagnosticCodes.InvalidControlFlowContext);
                }

                if (returnStatement.Value != null)
                    CheckExpression(returnStatement.Value);
                break;

            case ShiftStatement shiftStatement when shiftStatement.Amount != null:
                CheckExpression(shiftStatement.Amount);
                break;

            case SubshellStatement subshellStatement:
                PushScope();
                foreach (var nested in subshellStatement.Body)
                    CheckStatement(nested);
                PopScope();

                ResolveIntoBinding(
                    subshellStatement.IntoVariable,
                    subshellStatement.IntoMode,
                    subshellStatement,
                    (creates, createConst) =>
                    {
                        subshellStatement.IntoCreatesVariable = creates;
                        subshellStatement.IntoCreatesConst = createConst;
                    });
                break;

            case WaitStatement waitStatement:
                if (waitStatement.TargetKind == WaitTargetKind.Target && waitStatement.Target != null)
                    CheckExpression(waitStatement.Target);

                ResolveIntoBinding(
                    waitStatement.IntoVariable,
                    waitStatement.IntoMode,
                    waitStatement,
                    (creates, createConst) =>
                    {
                        waitStatement.IntoCreatesVariable = creates;
                        waitStatement.IntoCreatesConst = createConst;
                    });
                break;

            case ShellStatement shellStatement:
                CheckExpression(shellStatement.Command);
                break;

            case ExpressionStatement expressionStatement:
                CheckExpression(expressionStatement.Expression);
                break;
        }
    }

    private void CheckFunction(FunctionDeclaration function)
    {
        PushScope();
        functionDepth++;
        bool sawDefault = false;

        foreach (var parameter in function.Parameters)
        {
            if (IsBuiltinIdentifier(parameter.Name))
            {
                Report(
                    parameter,
                    $"Cannot declare built-in variable '{parameter.Name}'.",
                    DiagnosticCodes.InvalidAssignmentTarget);
            }

            if (parameter.DefaultValue == null)
            {
                if (sawDefault)
                {
                    Report(
                        parameter,
                        $"Required parameter '{parameter.Name}' cannot appear after defaulted parameters.",
                        DiagnosticCodes.InvalidParameterDeclaration);
                }
            }
            else
            {
                sawDefault = true;
                CheckExpression(parameter.DefaultValue);
            }

            Declare(parameter.Name, isConst: false, parameter);
        }

        foreach (var statement in function.Body)
            CheckStatement(statement);

        functionDepth--;
        PopScope();
    }

    private void CheckExpression(Expression expression)
    {
        switch (expression)
        {
            case IdentifierExpression identifier:
                ValidateIdentifierUse(identifier);
                break;

            case EnumAccessExpression enumAccess:
                ValidateEnumAccess(enumAccess);
                break;

            case FunctionCallExpression functionCall:
                ValidateFunctionCall(functionCall, implicitArgs: 0);
                break;

            case ShellCaptureExpression shellCapture:
                CheckExpression(shellCapture.Command);
                break;

            case PipeExpression pipe:
                CheckExpression(pipe.Left);
                if (pipe.Right is IdentifierExpression target)
                {
                    ValidateAssignmentTarget(target, isGlobal: false);
                }
                else if (pipe.Right is FunctionCallExpression call)
                {
                    ValidateFunctionCall(call, implicitArgs: 1);
                }
                else
                {
                    CheckExpression(pipe.Right);
                }
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

    private void ValidateFunctionCall(FunctionCallExpression functionCall, int implicitArgs)
    {
        foreach (var argument in functionCall.Arguments)
            CheckExpression(argument);

        if (!functions.TryGetValue(functionCall.FunctionName, out var functionInfo))
        {
            Report(
                functionCall,
                $"Unknown function '{functionCall.FunctionName}'.",
                DiagnosticCodes.UnknownFunction);
            return;
        }

        var actual = functionCall.Arguments.Count + implicitArgs;
        if (actual < functionInfo.RequiredParameterCount || actual > functionInfo.ParameterCount)
        {
            Report(
                functionCall,
                $"Function '{functionCall.FunctionName}' expects {FormatArity(functionInfo.RequiredParameterCount, functionInfo.ParameterCount)}, got {actual}.",
                DiagnosticCodes.FunctionArityMismatch);
        }
    }

    private void ValidateIdentifierUse(IdentifierExpression identifier)
    {
        if (IsBuiltinIdentifier(identifier.Name))
            return;

        if (TryResolveSymbol(identifier.Name, out _))
            return;

        Report(
            identifier,
            $"Use of undeclared variable '{identifier.Name}'.",
            DiagnosticCodes.UndeclaredVariable);
    }

    private void ValidateAssignmentTarget(IdentifierExpression identifier, bool isGlobal)
    {
        if (IsBuiltinIdentifier(identifier.Name))
        {
            Report(
                identifier,
                $"Cannot assign to built-in variable '{identifier.Name}'.",
                DiagnosticCodes.InvalidAssignmentTarget);
            return;
        }

        if (isGlobal)
        {
            if (!globalScope.TryGetValue(identifier.Name, out var symbol))
            {
                Report(
                    identifier,
                    $"Use of undeclared variable '{identifier.Name}'.",
                    DiagnosticCodes.UndeclaredVariable);
                return;
            }

            if (symbol.IsConst)
            {
                Report(
                    identifier,
                    $"Cannot assign to const variable '{identifier.Name}'.",
                    DiagnosticCodes.InvalidAssignmentTarget);
            }

            return;
        }

        if (!TryResolveSymbol(identifier.Name, out var resolved))
        {
            Report(
                identifier,
                $"Use of undeclared variable '{identifier.Name}'.",
                DiagnosticCodes.UndeclaredVariable);
            return;
        }

        if (resolved.IsConst)
        {
            Report(
                identifier,
                $"Cannot assign to const variable '{identifier.Name}'.",
                DiagnosticCodes.InvalidAssignmentTarget);
        }
    }

    private void ValidateIndexAssignmentTarget(IndexAccessExpression indexAccess)
    {
        CheckExpression(indexAccess.Array);
        CheckExpression(indexAccess.Index);

        if (indexAccess.Array is IdentifierExpression identifier && IsBuiltinIdentifier(identifier.Name))
        {
            Report(
                indexAccess,
                $"Cannot assign to built-in variable '{identifier.Name}'.",
                DiagnosticCodes.InvalidAssignmentTarget);
        }
    }

    private void ValidateEnumAccess(EnumAccessExpression enumAccess)
    {
        if (!enums.TryGetValue(enumAccess.EnumName, out var members))
        {
            Report(
                enumAccess,
                $"Unknown enum '{enumAccess.EnumName}'.",
                DiagnosticCodes.UndeclaredVariable);
            return;
        }

        if (!members.Contains(enumAccess.MemberName))
        {
            Report(
                enumAccess,
                $"Unknown enum member '{enumAccess.EnumName}::{enumAccess.MemberName}'.",
                DiagnosticCodes.UndeclaredVariable);
        }
    }

    private void ResolveIntoBinding(
        string? targetName,
        IntoBindingMode mode,
        AstNode node,
        Action<bool, bool> setResolution)
    {
        setResolution(false, false);
        if (string.IsNullOrEmpty(targetName))
            return;

        if (IsBuiltinIdentifier(targetName))
        {
            Report(
                node,
                $"Cannot assign to built-in variable '{targetName}'.",
                DiagnosticCodes.InvalidAssignmentTarget);
            return;
        }

        if (TryResolveSymbol(targetName, out var resolved))
        {
            if (resolved.IsConst)
            {
                Report(
                    node,
                    $"Cannot assign to const variable '{targetName}'.",
                    DiagnosticCodes.InvalidAssignmentTarget);
            }

            return;
        }

        var createConst = mode == IntoBindingMode.Const;
        Declare(targetName, createConst, node, isGlobal: false);
        setResolution(true, createConst);
    }

    private void PushScope()
    {
        scopes.Push(new Dictionary<string, SymbolInfo>(scopes.Peek(), StringComparer.Ordinal));
        declaredInScope.Push(new HashSet<string>(StringComparer.Ordinal));
    }

    private void PopScope()
    {
        scopes.Pop();
        declaredInScope.Pop();
    }

    private void Declare(string name, bool isConst, AstNode node, bool isGlobal = false)
    {
        if (isGlobal)
        {
            if (!globalDeclared.Add(name))
            {
                Report(
                    node,
                    $"Duplicate declaration of '{name}' in the same scope.",
                    DiagnosticCodes.DuplicateDeclaration);
                return;
            }

            globalScope[name] = new SymbolInfo(isConst);
            return;
        }

        if (!declaredInScope.Peek().Add(name))
        {
            Report(
                node,
                $"Duplicate declaration of '{name}' in the same scope.",
                DiagnosticCodes.DuplicateDeclaration);
            return;
        }

        scopes.Peek()[name] = new SymbolInfo(isConst);
    }

    private bool TryResolveSymbol(string name, out SymbolInfo symbol)
    {
        foreach (var scope in scopes)
        {
            if (scope.TryGetValue(name, out symbol))
                return true;
        }

        symbol = default;
        return false;
    }

    private void Report(AstNode node, string message, string code)
    {
        diagnostics.AddError(message, node.Line, node.Column, code);
    }

    private static string FormatArity(int required, int total)
    {
        if (required == total)
            return total.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"{required}..{total}";
    }

    private static bool IsBuiltinIdentifier(string name) => string.Equals(name, "argv", StringComparison.Ordinal);

    private readonly record struct SymbolInfo(bool IsConst);
    private readonly record struct FunctionInfo(int ParameterCount, int RequiredParameterCount);
}
