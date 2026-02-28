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
    private readonly ExpressionGenerator expressions;
    private readonly StatementGenerator statements;
    private int indentLevel = 0;
    private const string IndentString = "    ";
    private const string GlobalScope = "<global>";
    private const string ArgvRuntimeName = "__lash_argv";
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

    internal static string ArgvName => ArgvRuntimeName;
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
        indentLevel = 0;
        needsTrackedJobs = false;

        new ComptimePipeline().Run(program);
        AnalyzeAssociativeVariables(program);
        needsTrackedJobs = RequiresTrackedJobs(program.Statements);

        EmitLine("#!/usr/bin/env bash");
        EmitLine($"declare -a {ArgvRuntimeName}=(\"$@\")");
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
}
