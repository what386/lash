namespace Lash.Compiler.CodeGen;

using System.Text;
using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;
using Lash.Compiler.Ast.Types;
using Lash.Compiler.Frontend.Comptime;

public class BashGenerator
{
    private readonly StringBuilder output = new();
    private readonly List<string> warnings = new();
    private readonly HashSet<string> associativeVariables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> functionLocalSymbols = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool[]> functionArrayParameters = new(StringComparer.Ordinal);
    private readonly ExpressionGenerator expressions;
    private readonly StatementGenerator statements;
    private int indentLevel = 0;
    private const string IndentString = "    ";
    private const string GlobalScope = "<global>";
    private const string TrackedJobsRuntimeName = "__lash_jobs";
    private const string WaitPidRuntimeName = "__lash_wait_pid";
    private string currentContext = "<unknown>";
    private string? currentFunctionName;
    private bool needsTrackedJobs;

    public BashGenerator()
    {
        expressions = new ExpressionGenerator(this);
        statements = new StatementGenerator(this);
    }

    public IReadOnlyList<string> Warnings => warnings;

    internal static string TrackedJobsName => TrackedJobsRuntimeName;
    internal static string WaitPidName => WaitPidRuntimeName;
    internal string? CurrentFunctionName
    {
        get => currentFunctionName;
        set => currentFunctionName = value;
    }

    internal string CurrentContext
    {
        get => currentContext;
        set => currentContext = value;
    }

    internal int IndentLevel
    {
        get => indentLevel;
        set => indentLevel = value;
    }

    internal bool NeedsTrackedJobs => needsTrackedJobs;

    public string Generate(ProgramNode program)
    {
        output.Clear();
        warnings.Clear();
        associativeVariables.Clear();
        functionLocalSymbols.Clear();
        functionArrayParameters.Clear();
        indentLevel = 0;
        needsTrackedJobs = false;

        new ComptimePipeline().Run(program);
        AnalyzeAssociativeVariables(program);
        AnalyzeFunctionArrayParameters(program.Statements);
        needsTrackedJobs = RequiresTrackedJobs(program.Statements);

        EmitLine("#!/usr/bin/env bash");
        if (needsTrackedJobs)
            EmitLine($"declare -a {TrackedJobsRuntimeName}=()");

        foreach (var stmt in program.Statements)
        {
            GenerateStatement(stmt);
            EmitLine();
        }

        return output.ToString();
    }

    internal void Emit(string code)
    {
        output.Append(new string(' ', indentLevel * IndentString.Length));
        output.Append(code);
    }

    internal void EmitLine(string code = "")
    {
        if (!string.IsNullOrEmpty(code))
            Emit(code);
        output.AppendLine();
    }

    internal void EmitComment(string comment)
    {
        Emit($"# {comment}");
    }

    internal string EscapeString(string str, bool preserveLineBreaks = false)
    {
        var escaped = str.Replace("\\", "\\\\")
                         .Replace("\"", "\\\"")
                         .Replace("$", "\\$")
                         .Replace("`", "\\`");

        if (!preserveLineBreaks)
        {
            escaped = escaped.Replace("\n", "\\n")
                             .Replace("\r", "\\r");
        }

        return escaped.Replace("\t", "\\t");
    }

    internal void ReportUnsupported(string feature)
    {
        if (!warnings.Contains(feature))
            warnings.Add(feature);
    }

    internal string UnsupportedExpression(Expression expr)
    {
        ReportUnsupported($"expression '{expr.GetType().Name}' in {currentContext}");
        return "\"\"";
    }

    internal string GenerateExpression(Expression expr) => expressions.GenerateExpression(expr);

    internal string GenerateArithmeticExpression(Expression expr) => expressions.GenerateArithmeticExpression(expr);

    internal string GenerateArrayLiteral(ArrayLiteral array) => expressions.GenerateArrayLiteral(array);

    internal string GenerateCollectionIndex(Expression index, bool preferString) => expressions.GenerateCollectionIndex(index, preferString);

    internal bool TryGenerateShellPayload(Expression expression, out string payload) => expressions.TryGenerateShellPayload(expression, out payload);

    internal static string GenerateInterpolatedStringLiteral(string template) => ExpressionGenerator.GenerateInterpolatedStringLiteral(template);

    internal void GenerateStatement(Statement stmt) => statements.GenerateStatement(stmt);

    private void AnalyzeAssociativeVariables(ProgramNode program)
    {
        CollectFunctionLocals(program.Statements);
        CollectAssociativeUsages(program.Statements, functionName: null);
    }

    private void CollectFunctionLocals(IEnumerable<Statement> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case FunctionDeclaration function:
                    CollectFunctionLocalsForFunction(function);
                    break;
                case IfStatement ifStatement:
                    CollectFunctionLocals(ifStatement.ThenBlock);
                    foreach (var clause in ifStatement.ElifClauses)
                        CollectFunctionLocals(clause.Body);
                    CollectFunctionLocals(ifStatement.ElseBlock);
                    break;
                case SwitchStatement switchStatement:
                    foreach (var clause in switchStatement.Cases)
                        CollectFunctionLocals(clause.Body);
                    break;
                case ForLoop forLoop:
                    CollectFunctionLocals(forLoop.Body);
                    break;
                case SelectLoop selectLoop:
                    CollectFunctionLocals(selectLoop.Body);
                    break;
                case WhileLoop whileLoop:
                    CollectFunctionLocals(whileLoop.Body);
                    break;
                case UntilLoop untilLoop:
                    CollectFunctionLocals(untilLoop.Body);
                    break;
                case SubshellStatement subshellStatement:
                    CollectFunctionLocals(subshellStatement.Body);
                    break;
                case CoprocStatement coprocStatement:
                    CollectFunctionLocals(coprocStatement.Body);
                    break;
            }
        }
    }

    private void CollectFunctionLocalsForFunction(FunctionDeclaration function)
    {
        var locals = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in function.Parameters)
            locals.Add(parameter.Name);

        CollectLocalDeclarations(function.Body, locals);
        functionLocalSymbols[function.Name] = locals;
        CollectFunctionLocals(function.Body);
    }

    private static void CollectLocalDeclarations(IEnumerable<Statement> statements, HashSet<string> locals)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case VariableDeclaration variable when !variable.IsGlobal:
                    locals.Add(variable.Name);
                    break;
                case IfStatement ifStatement:
                    CollectLocalDeclarations(ifStatement.ThenBlock, locals);
                    foreach (var clause in ifStatement.ElifClauses)
                        CollectLocalDeclarations(clause.Body, locals);
                    CollectLocalDeclarations(ifStatement.ElseBlock, locals);
                    break;
                case SwitchStatement switchStatement:
                    foreach (var clause in switchStatement.Cases)
                        CollectLocalDeclarations(clause.Body, locals);
                    break;
                case ForLoop forLoop:
                    CollectLocalDeclarations(forLoop.Body, locals);
                    break;
                case SelectLoop selectLoop:
                    CollectLocalDeclarations(selectLoop.Body, locals);
                    break;
                case WhileLoop whileLoop:
                    CollectLocalDeclarations(whileLoop.Body, locals);
                    break;
                case UntilLoop untilLoop:
                    CollectLocalDeclarations(untilLoop.Body, locals);
                    break;
                case SubshellStatement subshellStatement:
                    CollectLocalDeclarations(subshellStatement.Body, locals);
                    break;
                case CoprocStatement coprocStatement:
                    CollectLocalDeclarations(coprocStatement.Body, locals);
                    break;
            }
        }
    }

    private void CollectAssociativeUsages(IEnumerable<Statement> statements, string? functionName)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case VariableDeclaration variable:
                    CollectAssociativeUsages(variable.Value, functionName);
                    break;
                case Assignment assignment:
                    if (assignment.Target is IndexAccessExpression indexAccess)
                    {
                        CollectAssociativeIndexUsage(indexAccess, functionName, assignment.IsGlobal);
                    }
                    else if (assignment.Target is Expression targetExpr)
                    {
                        CollectAssociativeUsages(targetExpr, functionName);
                    }
                    CollectAssociativeUsages(assignment.Value, functionName);
                    break;
                case FunctionDeclaration function:
                    CollectAssociativeUsages(function.Body, function.Name);
                    break;
                case IfStatement ifStatement:
                    CollectAssociativeUsages(ifStatement.Condition, functionName);
                    CollectAssociativeUsages(ifStatement.ThenBlock, functionName);
                    foreach (var clause in ifStatement.ElifClauses)
                    {
                        CollectAssociativeUsages(clause.Condition, functionName);
                        CollectAssociativeUsages(clause.Body, functionName);
                    }
                    CollectAssociativeUsages(ifStatement.ElseBlock, functionName);
                    break;
                case SwitchStatement switchStatement:
                    CollectAssociativeUsages(switchStatement.Value, functionName);
                    foreach (var clause in switchStatement.Cases)
                    {
                        CollectAssociativeUsages(clause.Pattern, functionName);
                        CollectAssociativeUsages(clause.Body, functionName);
                    }
                    break;
                case ForLoop forLoop:
                    if (forLoop.Range != null)
                        CollectAssociativeUsages(forLoop.Range, functionName);
                    if (forLoop.Step != null)
                        CollectAssociativeUsages(forLoop.Step, functionName);
                    CollectAssociativeUsages(forLoop.Body, functionName);
                    break;
                case SelectLoop selectLoop:
                    if (selectLoop.Options != null)
                        CollectAssociativeUsages(selectLoop.Options, functionName);
                    CollectAssociativeUsages(selectLoop.Body, functionName);
                    break;
                case WhileLoop whileLoop:
                    CollectAssociativeUsages(whileLoop.Condition, functionName);
                    CollectAssociativeUsages(whileLoop.Body, functionName);
                    break;
                case UntilLoop untilLoop:
                    CollectAssociativeUsages(untilLoop.Condition, functionName);
                    CollectAssociativeUsages(untilLoop.Body, functionName);
                    break;
                case SubshellStatement subshellStatement:
                    CollectAssociativeUsages(subshellStatement.Body, functionName);
                    break;
                case CoprocStatement coprocStatement:
                    CollectAssociativeUsages(coprocStatement.Body, functionName);
                    break;
                case WaitStatement waitStatement when waitStatement.TargetKind == WaitTargetKind.Target && waitStatement.Target != null:
                    CollectAssociativeUsages(waitStatement.Target, functionName);
                    break;
                case ReturnStatement returnStatement when returnStatement.Value != null:
                    CollectAssociativeUsages(returnStatement.Value, functionName);
                    break;
                case ShiftStatement shiftStatement when shiftStatement.Amount != null:
                    CollectAssociativeUsages(shiftStatement.Amount, functionName);
                    break;
                case ShellStatement shellStatement:
                    CollectAssociativeUsages(shellStatement.Command, functionName);
                    break;
                case TestStatement testStatement:
                    CollectAssociativeUsages(testStatement.Condition, functionName);
                    break;
                case TrapStatement trapStatement:
                    if (trapStatement.Handler is not null)
                    {
                        foreach (var argument in trapStatement.Handler.Arguments)
                            CollectAssociativeUsages(argument, functionName);
                    }
                    else if (trapStatement.Command is not null)
                    {
                        CollectAssociativeUsages(trapStatement.Command, functionName);
                    }
                    break;
                case UntrapStatement:
                    break;
                case ExpressionStatement expressionStatement:
                    CollectAssociativeUsages(expressionStatement.Expression, functionName);
                    break;
            }
        }
    }

    private void CollectAssociativeUsages(Expression expression, string? functionName)
    {
        switch (expression)
        {
            case IndexAccessExpression indexAccess:
                CollectAssociativeIndexUsage(indexAccess, functionName, forceGlobal: false);
                break;
            case BinaryExpression binary:
                CollectAssociativeUsages(binary.Left, functionName);
                CollectAssociativeUsages(binary.Right, functionName);
                break;
            case UnaryExpression unary:
                CollectAssociativeUsages(unary.Operand, functionName);
                break;
            case FunctionCallExpression call:
                foreach (var arg in call.Arguments)
                    CollectAssociativeUsages(arg, functionName);
                break;
            case ShellCaptureExpression shellCapture:
                CollectAssociativeUsages(shellCapture.Command, functionName);
                break;
            case TestCaptureExpression testCapture:
                CollectAssociativeUsages(testCapture.Condition, functionName);
                break;
            case PipeExpression pipe:
                CollectAssociativeUsages(pipe.Left, functionName);
                CollectAssociativeUsages(pipe.Right, functionName);
                break;
            case RedirectExpression redirect:
                CollectAssociativeUsages(redirect.Left, functionName);
                CollectAssociativeUsages(redirect.Right, functionName);
                break;
            case ArrayLiteral array:
                foreach (var element in array.Elements)
                    CollectAssociativeUsages(element, functionName);
                break;
            case RangeExpression range:
                CollectAssociativeUsages(range.Start, functionName);
                CollectAssociativeUsages(range.End, functionName);
                break;
        }
    }

    private void CollectAssociativeIndexUsage(IndexAccessExpression indexAccess, string? functionName, bool forceGlobal)
    {
        CollectAssociativeUsages(indexAccess.Array, functionName);
        CollectAssociativeUsages(indexAccess.Index, functionName);

        if (indexAccess.Array is not IdentifierExpression identifier)
            return;

        if (!IsStringKeyIndex(indexAccess.Index))
            return;

        var scope = ResolveScopeForIdentifier(identifier.Name, functionName, forceGlobal);
        associativeVariables.Add(ScopedVariableKey(scope, identifier.Name));
    }

    private static bool IsStringKeyIndex(Expression expression)
    {
        if (expression is LiteralExpression literal &&
            literal.LiteralType is PrimitiveType { PrimitiveKind: PrimitiveType.Kind.String })
        {
            return true;
        }

        return expression.Type is StringType;
    }

    private string ResolveScopeForIdentifier(string name, string? functionName, bool forceGlobal)
    {
        if (forceGlobal || functionName == null)
            return GlobalScope;

        if (functionLocalSymbols.TryGetValue(functionName, out var locals) && locals.Contains(name))
            return functionName;

        return GlobalScope;
    }

    private static string ScopedVariableKey(string scope, string name) => $"{scope}::{name}";

    internal bool IsAssociativeVariable(string name, bool isGlobal)
    {
        var scope = isGlobal ? GlobalScope : (currentFunctionName ?? GlobalScope);
        return associativeVariables.Contains(ScopedVariableKey(scope, name));
    }

    internal bool IsCurrentScopeAssociative(string name)
    {
        var scope = ResolveScopeForIdentifier(name, currentFunctionName, forceGlobal: false);
        return associativeVariables.Contains(ScopedVariableKey(scope, name));
    }

    internal bool IsArrayParameter(string functionName, int parameterIndex)
    {
        return functionArrayParameters.TryGetValue(functionName, out var flags)
               && parameterIndex >= 0
               && parameterIndex < flags.Length
               && flags[parameterIndex];
    }

    private static bool RequiresTrackedJobs(IEnumerable<Statement> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case SubshellStatement subshellStatement:
                    if (subshellStatement.RunInBackground)
                        return true;
                    if (RequiresTrackedJobs(subshellStatement.Body))
                        return true;
                    break;
                case CoprocStatement coprocStatement:
                    if (RequiresTrackedJobs(coprocStatement.Body))
                        return true;
                    break;

                case WaitStatement waitStatement when waitStatement.TargetKind == WaitTargetKind.Jobs:
                    return true;

                case FunctionDeclaration function:
                    if (RequiresTrackedJobs(function.Body))
                        return true;
                    break;

                case IfStatement ifStatement:
                    if (RequiresTrackedJobs(ifStatement.ThenBlock))
                        return true;
                    foreach (var clause in ifStatement.ElifClauses)
                    {
                        if (RequiresTrackedJobs(clause.Body))
                            return true;
                    }
                    if (RequiresTrackedJobs(ifStatement.ElseBlock))
                        return true;
                    break;

                case SwitchStatement switchStatement:
                    foreach (var clause in switchStatement.Cases)
                    {
                        if (RequiresTrackedJobs(clause.Body))
                            return true;
                    }
                    break;

                case ForLoop forLoop:
                    if (RequiresTrackedJobs(forLoop.Body))
                        return true;
                    break;
                case SelectLoop selectLoop:
                    if (RequiresTrackedJobs(selectLoop.Body))
                        return true;
                    break;

                case WhileLoop whileLoop:
                    if (RequiresTrackedJobs(whileLoop.Body))
                        return true;
                    break;
                case UntilLoop untilLoop:
                    if (RequiresTrackedJobs(untilLoop.Body))
                        return true;
                    break;
            }
        }

        return false;
    }

    private void AnalyzeFunctionArrayParameters(IEnumerable<Statement> statements)
    {
        foreach (var function in EnumerateFunctionDeclarations(statements))
        {
            if (function.Parameters.Count == 0)
                continue;

            var parameterNames = function.Parameters
                .Select(static p => p.Name)
                .ToHashSet(StringComparer.Ordinal);
            if (parameterNames.Count == 0)
                continue;

            var arrayLikeNames = new HashSet<string>(StringComparer.Ordinal);
            MarkArrayLikeParameterUsages(function.Body, parameterNames, arrayLikeNames);
            if (arrayLikeNames.Count == 0)
                continue;

            var flags = new bool[function.Parameters.Count];
            for (var i = 0; i < function.Parameters.Count; i++)
                flags[i] = arrayLikeNames.Contains(function.Parameters[i].Name);

            functionArrayParameters[function.Name] = flags;
        }
    }

    private static IEnumerable<FunctionDeclaration> EnumerateFunctionDeclarations(IEnumerable<Statement> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case FunctionDeclaration function:
                    yield return function;
                    foreach (var nested in EnumerateFunctionDeclarations(function.Body))
                        yield return nested;
                    break;
                case IfStatement ifStatement:
                    foreach (var nested in EnumerateFunctionDeclarations(ifStatement.ThenBlock))
                        yield return nested;
                    foreach (var clause in ifStatement.ElifClauses)
                    {
                        foreach (var nested in EnumerateFunctionDeclarations(clause.Body))
                            yield return nested;
                    }
                    foreach (var nested in EnumerateFunctionDeclarations(ifStatement.ElseBlock))
                        yield return nested;
                    break;
                case SwitchStatement switchStatement:
                    foreach (var clause in switchStatement.Cases)
                    {
                        foreach (var nested in EnumerateFunctionDeclarations(clause.Body))
                            yield return nested;
                    }
                    break;
                case ForLoop forLoop:
                    foreach (var nested in EnumerateFunctionDeclarations(forLoop.Body))
                        yield return nested;
                    break;
                case SelectLoop selectLoop:
                    foreach (var nested in EnumerateFunctionDeclarations(selectLoop.Body))
                        yield return nested;
                    break;
                case WhileLoop whileLoop:
                    foreach (var nested in EnumerateFunctionDeclarations(whileLoop.Body))
                        yield return nested;
                    break;
                case UntilLoop untilLoop:
                    foreach (var nested in EnumerateFunctionDeclarations(untilLoop.Body))
                        yield return nested;
                    break;
                case SubshellStatement subshellStatement:
                    foreach (var nested in EnumerateFunctionDeclarations(subshellStatement.Body))
                        yield return nested;
                    break;
                case CoprocStatement coprocStatement:
                    foreach (var nested in EnumerateFunctionDeclarations(coprocStatement.Body))
                        yield return nested;
                    break;
            }
        }
    }

    private static void MarkArrayLikeParameterUsages(
        IEnumerable<Statement> statements,
        HashSet<string> parameterNames,
        HashSet<string> arrayLikeNames)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case FunctionDeclaration:
                    break;
                case VariableDeclaration variable:
                    MarkArrayLikeParameterUsages(variable.Value, parameterNames, arrayLikeNames);
                    break;
                case Assignment assignment:
                    MarkArrayLikeParameterUsages(assignment.Target, parameterNames, arrayLikeNames);
                    MarkArrayLikeParameterUsages(assignment.Value, parameterNames, arrayLikeNames);
                    break;
                case IfStatement ifStatement:
                    MarkArrayLikeParameterUsages(ifStatement.Condition, parameterNames, arrayLikeNames);
                    MarkArrayLikeParameterUsages(ifStatement.ThenBlock, parameterNames, arrayLikeNames);
                    foreach (var clause in ifStatement.ElifClauses)
                    {
                        MarkArrayLikeParameterUsages(clause.Condition, parameterNames, arrayLikeNames);
                        MarkArrayLikeParameterUsages(clause.Body, parameterNames, arrayLikeNames);
                    }
                    MarkArrayLikeParameterUsages(ifStatement.ElseBlock, parameterNames, arrayLikeNames);
                    break;
                case SwitchStatement switchStatement:
                    MarkArrayLikeParameterUsages(switchStatement.Value, parameterNames, arrayLikeNames);
                    foreach (var clause in switchStatement.Cases)
                    {
                        MarkArrayLikeParameterUsages(clause.Pattern, parameterNames, arrayLikeNames);
                        MarkArrayLikeParameterUsages(clause.Body, parameterNames, arrayLikeNames);
                    }
                    break;
                case ForLoop forLoop:
                    if (forLoop.Range is IdentifierExpression ident && parameterNames.Contains(ident.Name))
                        arrayLikeNames.Add(ident.Name);
                    if (forLoop.Range != null)
                        MarkArrayLikeParameterUsages(forLoop.Range, parameterNames, arrayLikeNames);
                    if (forLoop.Step != null)
                        MarkArrayLikeParameterUsages(forLoop.Step, parameterNames, arrayLikeNames);
                    MarkArrayLikeParameterUsages(forLoop.Body, parameterNames, arrayLikeNames);
                    break;
                case SelectLoop selectLoop:
                    if (selectLoop.Options is IdentifierExpression optionsIdent && parameterNames.Contains(optionsIdent.Name))
                        arrayLikeNames.Add(optionsIdent.Name);
                    if (selectLoop.Options != null)
                        MarkArrayLikeParameterUsages(selectLoop.Options, parameterNames, arrayLikeNames);
                    MarkArrayLikeParameterUsages(selectLoop.Body, parameterNames, arrayLikeNames);
                    break;
                case WhileLoop whileLoop:
                    MarkArrayLikeParameterUsages(whileLoop.Condition, parameterNames, arrayLikeNames);
                    MarkArrayLikeParameterUsages(whileLoop.Body, parameterNames, arrayLikeNames);
                    break;
                case UntilLoop untilLoop:
                    MarkArrayLikeParameterUsages(untilLoop.Condition, parameterNames, arrayLikeNames);
                    MarkArrayLikeParameterUsages(untilLoop.Body, parameterNames, arrayLikeNames);
                    break;
                case SubshellStatement subshellStatement:
                    MarkArrayLikeParameterUsages(subshellStatement.Body, parameterNames, arrayLikeNames);
                    break;
                case CoprocStatement coprocStatement:
                    MarkArrayLikeParameterUsages(coprocStatement.Body, parameterNames, arrayLikeNames);
                    break;
                case WaitStatement waitStatement when waitStatement.Target != null:
                    MarkArrayLikeParameterUsages(waitStatement.Target, parameterNames, arrayLikeNames);
                    break;
                case ReturnStatement returnStatement when returnStatement.Value != null:
                    MarkArrayLikeParameterUsages(returnStatement.Value, parameterNames, arrayLikeNames);
                    break;
                case ShiftStatement shiftStatement when shiftStatement.Amount != null:
                    MarkArrayLikeParameterUsages(shiftStatement.Amount, parameterNames, arrayLikeNames);
                    break;
                case ShellStatement shellStatement:
                    MarkArrayLikeParameterUsages(shellStatement.Command, parameterNames, arrayLikeNames);
                    break;
                case TestStatement testStatement:
                    MarkArrayLikeParameterUsages(testStatement.Condition, parameterNames, arrayLikeNames);
                    break;
                case TrapStatement trapStatement:
                    if (trapStatement.Handler is not null)
                    {
                        foreach (var arg in trapStatement.Handler.Arguments)
                            MarkArrayLikeParameterUsages(arg, parameterNames, arrayLikeNames);
                    }
                    else if (trapStatement.Command is not null)
                    {
                        MarkArrayLikeParameterUsages(trapStatement.Command, parameterNames, arrayLikeNames);
                    }
                    break;
                case ExpressionStatement expressionStatement:
                    MarkArrayLikeParameterUsages(expressionStatement.Expression, parameterNames, arrayLikeNames);
                    break;
            }
        }
    }

    private static void MarkArrayLikeParameterUsages(
        Expression expression,
        HashSet<string> parameterNames,
        HashSet<string> arrayLikeNames)
    {
        switch (expression)
        {
            case UnaryExpression { Operator: "#" } unary when unary.Operand is IdentifierExpression ident && parameterNames.Contains(ident.Name):
                arrayLikeNames.Add(ident.Name);
                MarkArrayLikeParameterUsages(unary.Operand, parameterNames, arrayLikeNames);
                break;
            case IndexAccessExpression indexAccess when indexAccess.Array is IdentifierExpression arrayIdent && parameterNames.Contains(arrayIdent.Name):
                arrayLikeNames.Add(arrayIdent.Name);
                MarkArrayLikeParameterUsages(indexAccess.Index, parameterNames, arrayLikeNames);
                break;
            case BinaryExpression binary:
                MarkArrayLikeParameterUsages(binary.Left, parameterNames, arrayLikeNames);
                MarkArrayLikeParameterUsages(binary.Right, parameterNames, arrayLikeNames);
                break;
            case UnaryExpression unary:
                MarkArrayLikeParameterUsages(unary.Operand, parameterNames, arrayLikeNames);
                break;
            case FunctionCallExpression call:
                foreach (var argument in call.Arguments)
                    MarkArrayLikeParameterUsages(argument, parameterNames, arrayLikeNames);
                break;
            case ShellCaptureExpression shellCapture:
                MarkArrayLikeParameterUsages(shellCapture.Command, parameterNames, arrayLikeNames);
                break;
            case TestCaptureExpression testCapture:
                MarkArrayLikeParameterUsages(testCapture.Condition, parameterNames, arrayLikeNames);
                break;
            case PipeExpression pipe:
                MarkArrayLikeParameterUsages(pipe.Left, parameterNames, arrayLikeNames);
                MarkArrayLikeParameterUsages(pipe.Right, parameterNames, arrayLikeNames);
                break;
            case RedirectExpression redirect:
                MarkArrayLikeParameterUsages(redirect.Left, parameterNames, arrayLikeNames);
                MarkArrayLikeParameterUsages(redirect.Right, parameterNames, arrayLikeNames);
                break;
            case ArrayLiteral arrayLiteral:
                foreach (var element in arrayLiteral.Elements)
                    MarkArrayLikeParameterUsages(element, parameterNames, arrayLikeNames);
                break;
            case RangeExpression range:
                MarkArrayLikeParameterUsages(range.Start, parameterNames, arrayLikeNames);
                MarkArrayLikeParameterUsages(range.End, parameterNames, arrayLikeNames);
                break;
        }
    }
}
