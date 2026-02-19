namespace Lash.Compiler.Frontend.Semantics;

using System.Text.RegularExpressions;
using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;
using Lash.Compiler.Ast.Types;
using Lash.Compiler.Diagnostics;

public sealed class WarningAnalyzer
{
    private const int MaxConstRangeElements = 256;
    private static readonly Regex BracedCommandVariableRegex = new(@"(?<!\\)\$\{([A-Za-z_][A-Za-z0-9_]*)[^}]*\}", RegexOptions.Compiled);
    private static readonly Regex PlainCommandVariableRegex = new(@"(?<!\\)\$([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private static readonly Regex InterpolationPlaceholderRegex = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

    private readonly DiagnosticBag diagnostics;
    private readonly Stack<ScopeFrame> scopes = new();
    private readonly Stack<int> trackedJobs = new();
    private readonly Stack<Dictionary<string, ConstValue>> constValues = new();

    public WarningAnalyzer(DiagnosticBag diagnostics)
    {
        this.diagnostics = diagnostics;
    }

    public void Analyze(ProgramNode program)
    {
        PushScope();
        PushTrackedJobs(0);
        PushConstScope();
        AnalyzeBlock(program.Statements, inLoop: false);
        PopConstScope();
        PopTrackedJobs();
        PopScope();
    }

    private bool AnalyzeBlock(IEnumerable<Statement> statements, bool inLoop)
    {
        bool terminated = false;

        foreach (var statement in statements)
        {
            if (terminated)
            {
                diagnostics.AddWarning(
                    "Unreachable statement.",
                    statement.Line,
                    statement.Column,
                    DiagnosticCodes.UnreachableStatement);
                continue;
            }

            terminated = AnalyzeStatement(statement, inLoop);
        }

        return terminated;
    }

    private bool AnalyzeStatement(Statement statement, bool inLoop)
    {
        switch (statement)
        {
            case VariableDeclaration variable:
                AnalyzeExpression(variable.Value);
                WarnIfShadowing(variable.Name, variable.Line, variable.Column);
                DeclareSymbol(
                    variable.Name,
                    SymbolKind.Variable,
                    variable.Line,
                    variable.Column,
                    ignoreUnused: ShouldIgnoreUnusedSymbol(variable.Name) || variable.IsPublic);

                if (variable.Kind == VariableDeclaration.VarKind.Const && TryEvaluateConstValue(variable.Value, out var constValue))
                {
                    constValues.Peek()[variable.Name] = constValue;
                }
                else
                {
                    constValues.Peek().Remove(variable.Name);
                }
                return false;

            case Assignment assignment:
                AnalyzeExpression(assignment.Value);

                if (assignment.Target is IdentifierExpression identifier)
                {
                    InvalidateConst(identifier.Name);
                }
                else if (assignment.Target is IndexAccessExpression indexTarget)
                {
                    AnalyzeExpression(indexTarget.Array);
                    AnalyzeExpression(indexTarget.Index);
                    if (indexTarget.Array is IdentifierExpression arrayIdentifier)
                        InvalidateConst(arrayIdentifier.Name);
                }

                return false;

            case FunctionDeclaration function:
                WarnIfShadowing(function.Name, function.Line, function.Column);
                DeclareSymbol(
                    function.Name,
                    SymbolKind.Function,
                    function.Line,
                    function.Column,
                    ignoreUnused: ShouldIgnoreUnusedSymbol(function.Name) || function.IsPublic);

                PushScope();
                PushTrackedJobs(0);
                PushConstScope();
                foreach (var parameter in function.Parameters)
                {
                    if (parameter.DefaultValue != null)
                        AnalyzeExpression(parameter.DefaultValue);

                    WarnIfShadowing(parameter.Name, parameter.Line, parameter.Column);
                    DeclareSymbol(
                        parameter.Name,
                        SymbolKind.Parameter,
                        parameter.Line,
                        parameter.Column,
                        ignoreUnused: ShouldIgnoreUnusedSymbol(parameter.Name));
                    InvalidateConst(parameter.Name);
                }

                AnalyzeBlock(function.Body, inLoop: false);
                PopConstScope();
                PopTrackedJobs();
                PopScope();
                return false;

            case IfStatement ifStatement:
                AnalyzeExpression(ifStatement.Condition);
                return AnalyzeIfStatement(ifStatement, inLoop);

            case SwitchStatement switchStatement:
                AnalyzeExpression(switchStatement.Value);
                return AnalyzeSwitchStatement(switchStatement, inLoop);

            case ForLoop forLoop:
                AnalyzeExpression(forLoop.Range);
                if (forLoop.Step != null)
                    AnalyzeExpression(forLoop.Step);

                PushScope();
                PushTrackedJobs(CurrentTrackedJobs());
                PushConstScope();
                WarnIfShadowing(forLoop.Variable, forLoop.Line, forLoop.Column);
                DeclareSymbol(
                    forLoop.Variable,
                    SymbolKind.Variable,
                    forLoop.Line,
                    forLoop.Column,
                    ignoreUnused: ShouldIgnoreUnusedSymbol(forLoop.Variable));
                InvalidateConst(forLoop.Variable);
                AnalyzeBlock(forLoop.Body, inLoop: true);
                PopConstScope();
                PopTrackedJobs();
                PopScope();
                return false;

            case WhileLoop whileLoop:
                if (TryEvaluateBool(whileLoop.Condition, out var whileCondition) && !whileCondition)
                {
                    WarnBlockUnreachable(whileLoop.Body, "Unreachable loop body: condition is always false.");
                    return false;
                }

                PushScope();
                PushTrackedJobs(CurrentTrackedJobs());
                PushConstScope();
                AnalyzeBlock(whileLoop.Body, inLoop: true);
                PopConstScope();
                PopTrackedJobs();
                PopScope();
                return false;

            case ReturnStatement returnStatement:
                if (returnStatement.Value != null)
                    AnalyzeExpression(returnStatement.Value);
                return true;

            case BreakStatement:
                return inLoop;

            case ContinueStatement:
                return inLoop;

            case SubshellStatement subshellStatement:
                PushScope();
                PushTrackedJobs(0);
                PushConstScope();
                AnalyzeBlock(subshellStatement.Body, inLoop: false);
                PopConstScope();
                PopTrackedJobs();
                PopScope();

                if (!string.IsNullOrEmpty(subshellStatement.IntoVariable))
                    InvalidateConst(subshellStatement.IntoVariable!);

                if (subshellStatement.RunInBackground)
                    SetCurrentTrackedJobs(CurrentTrackedJobs() + 1);
                return false;

            case WaitStatement waitStatement when waitStatement.TargetKind == WaitTargetKind.Jobs:
                if (CurrentTrackedJobs() == 0)
                {
                    diagnostics.AddWarning(
                        "'wait jobs' has no tracked background subshells to wait for.",
                        waitStatement.Line,
                        waitStatement.Column,
                        DiagnosticCodes.WaitJobsWithoutTrackedJobs);
                }

                if (!string.IsNullOrEmpty(waitStatement.IntoVariable))
                    InvalidateConst(waitStatement.IntoVariable!);

                SetCurrentTrackedJobs(0);
                return false;

            case WaitStatement waitStatement:
                if (waitStatement.TargetKind == WaitTargetKind.Target && waitStatement.Target != null)
                    AnalyzeExpression(waitStatement.Target);

                if (!string.IsNullOrEmpty(waitStatement.IntoVariable))
                    InvalidateConst(waitStatement.IntoVariable!);

                return false;

            case ShiftStatement shiftStatement when shiftStatement.Amount != null:
                AnalyzeExpression(shiftStatement.Amount);
                return false;

            case CommandStatement commandStatement:
                AnalyzeCommandScript(commandStatement.Script);
                return false;

            case ShellStatement shellStatement:
                AnalyzeExpression(shellStatement.Command);
                return false;

            case ExpressionStatement expressionStatement:
                AnalyzeExpression(expressionStatement.Expression);
                return false;

            default:
                return false;
        }
    }

    private bool AnalyzeIfStatement(IfStatement ifStatement, bool inLoop)
    {
        if (TryEvaluateBool(ifStatement.Condition, out var conditionValue))
        {
            if (conditionValue)
            {
                var thenResult = AnalyzeBranch(ifStatement.ThenBlock, inLoop, CurrentTrackedJobs());
                foreach (var clause in ifStatement.ElifClauses)
                    WarnBlockUnreachable(clause.Body, "Unreachable branch: previous condition is always true.", clause.Line, clause.Column);
                WarnBlockUnreachable(ifStatement.ElseBlock, "Unreachable branch: previous condition is always true.");
                SetCurrentTrackedJobs(thenResult.TrackedJobs);
                return thenResult.Terminated;
            }

            WarnBlockUnreachable(ifStatement.ThenBlock, "Unreachable branch: condition is always false.", ifStatement.Line, ifStatement.Column);
            return AnalyzeElifChain(ifStatement.ElifClauses, ifStatement.ElseBlock, inLoop, CurrentTrackedJobs());
        }

        foreach (var clause in ifStatement.ElifClauses)
            AnalyzeExpression(clause.Condition);

        return AnalyzeIfPaths(
            ifStatement.ThenBlock,
            ifStatement.ElifClauses.Select(c => c.Body).ToList(),
            ifStatement.ElseBlock,
            inLoop,
            CurrentTrackedJobs());
    }

    private bool AnalyzeElifChain(
        IReadOnlyList<ElifClause> elifClauses,
        List<Statement> elseBlock,
        bool inLoop,
        int baseTrackedJobs)
    {
        for (int i = 0; i < elifClauses.Count; i++)
        {
            var clause = elifClauses[i];
            AnalyzeExpression(clause.Condition);

            if (!TryEvaluateBool(clause.Condition, out var clauseValue))
            {
                foreach (var laterClause in elifClauses.Skip(i + 1))
                    AnalyzeExpression(laterClause.Condition);

                var remainingElifs = elifClauses.Skip(i).Select(c => c.Body).ToList();
                return AnalyzeIfPaths(
                    remainingElifs[0],
                    remainingElifs.Skip(1).ToList(),
                    elseBlock,
                    inLoop,
                    baseTrackedJobs);
            }

            if (!clauseValue)
            {
                WarnBlockUnreachable(clause.Body, "Unreachable branch: condition is always false.", clause.Line, clause.Column);
                continue;
            }

            var branchResult = AnalyzeBranch(clause.Body, inLoop, baseTrackedJobs);
            foreach (var laterClause in elifClauses.Skip(i + 1))
                WarnBlockUnreachable(laterClause.Body, "Unreachable branch: previous condition is always true.", laterClause.Line, laterClause.Column);
            WarnBlockUnreachable(elseBlock, "Unreachable branch: previous condition is always true.");
            SetCurrentTrackedJobs(branchResult.TrackedJobs);
            return branchResult.Terminated;
        }

        if (elseBlock.Count == 0)
        {
            SetCurrentTrackedJobs(baseTrackedJobs);
            return false;
        }

        var elseResult = AnalyzeBranch(elseBlock, inLoop, baseTrackedJobs);
        SetCurrentTrackedJobs(elseResult.TrackedJobs);
        return elseResult.Terminated;
    }

    private bool AnalyzeIfPaths(
        List<Statement> thenBlock,
        IReadOnlyList<List<Statement>> elifBlocks,
        List<Statement> elseBlock,
        bool inLoop,
        int baseTrackedJobs)
    {
        var branchTracked = new List<int>();

        var thenResult = AnalyzeBranch(thenBlock, inLoop, baseTrackedJobs);
        branchTracked.Add(thenResult.TrackedJobs);

        var elifTerminates = new List<bool>();
        foreach (var block in elifBlocks)
        {
            var result = AnalyzeBranch(block, inLoop, baseTrackedJobs);
            elifTerminates.Add(result.Terminated);
            branchTracked.Add(result.TrackedJobs);
        }

        bool elseTerminates = false;
        if (elseBlock.Count > 0)
        {
            var elseResult = AnalyzeBranch(elseBlock, inLoop, baseTrackedJobs);
            elseTerminates = elseResult.Terminated;
            branchTracked.Add(elseResult.TrackedJobs);
        }
        else
        {
            branchTracked.Add(baseTrackedJobs);
        }

        SetCurrentTrackedJobs(branchTracked.Max());
        return thenResult.Terminated
               && elifTerminates.All(t => t)
               && elseBlock.Count > 0
               && elseTerminates;
    }

    private bool AnalyzeSwitchStatement(SwitchStatement switchStatement, bool inLoop)
    {
        if (!TryEvaluateConstValue(switchStatement.Value, out var switchValue))
        {
            foreach (var clause in switchStatement.Cases)
                AnalyzeExpression(clause.Pattern);
            return AnalyzeSwitchPaths(switchStatement.Cases, inLoop, CurrentTrackedJobs());
        }

        for (int i = 0; i < switchStatement.Cases.Count; i++)
        {
            var clause = switchStatement.Cases[i];
            AnalyzeExpression(clause.Pattern);

            if (!TryEvaluateConstValue(clause.Pattern, out var patternValue) || !IsExactPattern(clause.Pattern))
            {
                var remaining = switchStatement.Cases.Skip(i).ToList();
                return AnalyzeSwitchPaths(remaining, inLoop, CurrentTrackedJobs());
            }

            if (!ConstValuesEqual(switchValue, patternValue))
            {
                WarnBlockUnreachable(clause.Body, "Unreachable case: pattern can never match this constant switch value.", clause.Line, clause.Column);
                continue;
            }

            var matched = AnalyzeBranch(clause.Body, inLoop, CurrentTrackedJobs());
            foreach (var laterClause in switchStatement.Cases.Skip(i + 1))
            {
                WarnBlockUnreachable(
                    laterClause.Body,
                    "Unreachable case: an earlier case always matches this constant switch value.",
                    laterClause.Line,
                    laterClause.Column);
            }

            SetCurrentTrackedJobs(matched.TrackedJobs);
            return matched.Terminated;
        }

        return false;
    }

    private bool AnalyzeSwitchPaths(IReadOnlyList<SwitchCaseClause> cases, bool inLoop, int baseTrackedJobs)
    {
        var branchTracked = new List<int>();

        foreach (var clause in cases)
        {
            var result = AnalyzeBranch(clause.Body, inLoop, baseTrackedJobs);
            branchTracked.Add(result.TrackedJobs);
        }

        if (branchTracked.Count > 0)
            SetCurrentTrackedJobs(branchTracked.Max());
        return false;
    }

    private BranchResult AnalyzeBranch(List<Statement> body, bool inLoop, int baseTrackedJobs)
    {
        PushScope();
        PushTrackedJobs(baseTrackedJobs);
        PushConstScope();
        var terminated = AnalyzeBlock(body, inLoop);
        var jobs = CurrentTrackedJobs();
        PopConstScope();
        PopTrackedJobs();
        PopScope();
        return new BranchResult(terminated, jobs);
    }

    private void AnalyzeExpression(Expression expression)
    {
        switch (expression)
        {
            case LiteralExpression literal:
                AnalyzeInterpolatedLiteral(literal);
                break;

            case IdentifierExpression identifier:
                MarkVariableRead(identifier.Name);
                break;

            case FunctionCallExpression functionCall:
                MarkFunctionUsed(functionCall.FunctionName);
                foreach (var argument in functionCall.Arguments)
                    AnalyzeExpression(argument);
                break;

            case ShellCaptureExpression shellCapture:
                AnalyzeExpression(shellCapture.Command);
                break;

            case PipeExpression pipe:
                AnalyzeExpression(pipe.Left);
                if (pipe.Right is IdentifierExpression sink)
                {
                    InvalidateConst(sink.Name);
                }
                else
                {
                    AnalyzeExpression(pipe.Right);
                }
                break;

            case RedirectExpression redirect:
                AnalyzeExpression(redirect.Left);
                AnalyzeExpression(redirect.Right);
                break;

            case UnaryExpression unary:
                AnalyzeExpression(unary.Operand);
                break;

            case BinaryExpression binary:
                AnalyzeExpression(binary.Left);
                if (binary.Operator == "&&" &&
                    TryEvaluateBool(binary.Left, out var leftAndValue) &&
                    !leftAndValue)
                {
                    break;
                }

                if (binary.Operator == "||" &&
                    TryEvaluateBool(binary.Left, out var leftOrValue) &&
                    leftOrValue)
                {
                    break;
                }

                AnalyzeExpression(binary.Right);
                break;

            case RangeExpression range:
                AnalyzeExpression(range.Start);
                AnalyzeExpression(range.End);
                break;

            case IndexAccessExpression indexAccess:
                AnalyzeExpression(indexAccess.Array);
                AnalyzeExpression(indexAccess.Index);
                break;

            case ArrayLiteral arrayLiteral:
                foreach (var element in arrayLiteral.Elements)
                    AnalyzeExpression(element);
                break;
        }
    }

    private void AnalyzeCommandScript(string script)
    {
        foreach (Match match in BracedCommandVariableRegex.Matches(script))
            MarkVariableRead(match.Groups[1].Value);

        foreach (Match match in PlainCommandVariableRegex.Matches(script))
            MarkVariableRead(match.Groups[1].Value);
    }

    private void AnalyzeInterpolatedLiteral(LiteralExpression literal)
    {
        if (!literal.IsInterpolated || literal.Value is not string template)
            return;

        foreach (Match match in InterpolationPlaceholderRegex.Matches(template))
        {
            if (TryGetInterpolationSymbolName(match.Groups[1].Value, out var symbolName))
                MarkVariableRead(symbolName);
        }
    }

    private static bool TryGetInterpolationSymbolName(string placeholder, out string symbolName)
    {
        symbolName = string.Empty;
        if (string.IsNullOrWhiteSpace(placeholder))
            return false;

        var parts = placeholder
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        foreach (var part in parts)
        {
            if (!IsIdentifier(part))
                return false;
        }

        symbolName = string.Join("_", parts);
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

    private void WarnBlockUnreachable(List<Statement> statements, string reason, int? line = null, int? column = null)
    {
        if (statements.Count == 0 && line == null)
            return;

        diagnostics.AddWarning(
            reason,
            line ?? statements[0].Line,
            column ?? statements[0].Column,
            DiagnosticCodes.UnreachableStatement);
    }

    private bool TryEvaluateBool(Expression expression, out bool value)
    {
        if (!TryEvaluateConstValue(expression, out var constValue))
        {
            value = false;
            return false;
        }

        value = ToBool(constValue);
        return true;
    }

    private bool TryEvaluateInt(Expression expression, out int value)
    {
        if (TryEvaluateConstValue(expression, out var constValue) && constValue.Kind == ConstValueKind.Int)
        {
            value = constValue.IntValue;
            return true;
        }

        value = 0;
        return false;
    }

    private bool TryEvaluateConstValue(Expression expression, out ConstValue value)
    {
        switch (expression)
        {
            case LiteralExpression literal when literal.LiteralType is PrimitiveType primitive:
                switch (primitive.PrimitiveKind)
                {
                    case PrimitiveType.Kind.Bool when literal.Value is bool b:
                        value = ConstValue.FromBool(b);
                        return true;
                    case PrimitiveType.Kind.Int when literal.Value is int i:
                        value = ConstValue.FromInt(i);
                        return true;
                    case PrimitiveType.Kind.String:
                        value = ConstValue.FromString(literal.Value?.ToString() ?? string.Empty);
                        return true;
                }
                break;

            case IdentifierExpression identifier:
                if (TryResolveConst(identifier.Name, out value))
                    return true;
                break;

            case ArrayLiteral arrayLiteral:
                {
                    var elements = new List<ConstValue>(arrayLiteral.Elements.Count);
                    foreach (var element in arrayLiteral.Elements)
                    {
                        if (!TryEvaluateConstValue(element, out var elementValue))
                        {
                            value = default;
                            return false;
                        }

                        elements.Add(elementValue);
                    }

                    value = ConstValue.FromArray(elements);
                    return true;
                }

            case RangeExpression rangeExpression:
                if (TryEvaluateInt(rangeExpression.Start, out var rangeStart) &&
                    TryEvaluateInt(rangeExpression.End, out var rangeEnd) &&
                    TryBuildConstRange(rangeStart, rangeEnd, out value))
                {
                    return true;
                }
                break;

            case IndexAccessExpression indexAccess:
                if (!TryEvaluateConstValue(indexAccess.Array, out var source) ||
                    !TryEvaluateInt(indexAccess.Index, out var index) ||
                    index < 0)
                {
                    break;
                }

                if (source.Kind == ConstValueKind.Array)
                {
                    if (index < source.ArrayValues.Count)
                    {
                        value = source.ArrayValues[index];
                        return true;
                    }

                    break;
                }

                if (source.Kind == ConstValueKind.String && index < source.StringValue.Length)
                {
                    value = ConstValue.FromString(source.StringValue[index].ToString());
                    return true;
                }

                break;

            case UnaryExpression unary:
                if (unary.Operator == "#")
                {
                    if (!TryEvaluateConstValue(unary.Operand, out var lengthValue))
                        break;

                    switch (lengthValue.Kind)
                    {
                        case ConstValueKind.String:
                            value = ConstValue.FromInt(lengthValue.StringValue.Length);
                            return true;
                        case ConstValueKind.Array:
                            value = ConstValue.FromInt(lengthValue.ArrayValues.Count);
                            return true;
                    }

                    break;
                }

                if (!TryEvaluateConstValue(unary.Operand, out var operandValue))
                    break;

                switch (unary.Operator)
                {
                    case "!":
                        value = ConstValue.FromBool(!ToBool(operandValue));
                        return true;
                    case "-" when operandValue.Kind == ConstValueKind.Int:
                        value = ConstValue.FromInt(-operandValue.IntValue);
                        return true;
                    case "+" when operandValue.Kind == ConstValueKind.Int:
                        value = ConstValue.FromInt(operandValue.IntValue);
                        return true;
                }
                break;

            case BinaryExpression binary:
                if (binary.Operator == "&&")
                {
                    if (!TryEvaluateConstValue(binary.Left, out var leftAnd))
                        break;

                    if (!ToBool(leftAnd))
                    {
                        value = ConstValue.FromBool(false);
                        return true;
                    }

                    if (!TryEvaluateConstValue(binary.Right, out var rightAnd))
                        break;

                    value = ConstValue.FromBool(ToBool(rightAnd));
                    return true;
                }

                if (binary.Operator == "||")
                {
                    if (!TryEvaluateConstValue(binary.Left, out var leftOr))
                        break;

                    if (ToBool(leftOr))
                    {
                        value = ConstValue.FromBool(true);
                        return true;
                    }

                    if (!TryEvaluateConstValue(binary.Right, out var rightOr))
                        break;

                    value = ConstValue.FromBool(ToBool(rightOr));
                    return true;
                }

                if (!TryEvaluateConstValue(binary.Left, out var left) || !TryEvaluateConstValue(binary.Right, out var right))
                    break;

                switch (binary.Operator)
                {
                    case "==" when CanCompare(left, right):
                        value = ConstValue.FromBool(ConstValuesEqual(left, right));
                        return true;
                    case "!=" when CanCompare(left, right):
                        value = ConstValue.FromBool(!ConstValuesEqual(left, right));
                        return true;
                    case "<" when left.Kind == ConstValueKind.Int && right.Kind == ConstValueKind.Int:
                        value = ConstValue.FromBool(left.IntValue < right.IntValue);
                        return true;
                    case ">" when left.Kind == ConstValueKind.Int && right.Kind == ConstValueKind.Int:
                        value = ConstValue.FromBool(left.IntValue > right.IntValue);
                        return true;
                    case "<=" when left.Kind == ConstValueKind.Int && right.Kind == ConstValueKind.Int:
                        value = ConstValue.FromBool(left.IntValue <= right.IntValue);
                        return true;
                    case ">=" when left.Kind == ConstValueKind.Int && right.Kind == ConstValueKind.Int:
                        value = ConstValue.FromBool(left.IntValue >= right.IntValue);
                        return true;
                    case "+" when left.Kind == ConstValueKind.Int && right.Kind == ConstValueKind.Int:
                        value = ConstValue.FromInt(left.IntValue + right.IntValue);
                        return true;
                    case "-" when left.Kind == ConstValueKind.Int && right.Kind == ConstValueKind.Int:
                        value = ConstValue.FromInt(left.IntValue - right.IntValue);
                        return true;
                    case "*" when left.Kind == ConstValueKind.Int && right.Kind == ConstValueKind.Int:
                        value = ConstValue.FromInt(left.IntValue * right.IntValue);
                        return true;
                    case "/" when left.Kind == ConstValueKind.Int && right.Kind == ConstValueKind.Int && right.IntValue != 0:
                        value = ConstValue.FromInt(left.IntValue / right.IntValue);
                        return true;
                    case "%" when left.Kind == ConstValueKind.Int && right.Kind == ConstValueKind.Int && right.IntValue != 0:
                        value = ConstValue.FromInt(left.IntValue % right.IntValue);
                        return true;
                    case "+" when left.Kind == ConstValueKind.String && right.Kind == ConstValueKind.String:
                        value = ConstValue.FromString(left.StringValue + right.StringValue);
                        return true;
                    case ".." when left.Kind == ConstValueKind.Int && right.Kind == ConstValueKind.Int:
                        return TryBuildConstRange(left.IntValue, right.IntValue, out value);
                }

                break;
        }

        value = default;
        return false;
    }

    private static bool TryBuildConstRange(int start, int end, out ConstValue value)
    {
        var length = Math.Abs(end - start) + 1;
        if (length > MaxConstRangeElements)
        {
            value = default;
            return false;
        }

        var step = start <= end ? 1 : -1;
        var elements = new List<ConstValue>(length);
        for (int current = start; ; current += step)
        {
            elements.Add(ConstValue.FromInt(current));
            if (current == end)
                break;
        }

        value = ConstValue.FromArray(elements);
        return true;
    }

    private bool TryResolveConst(string name, out ConstValue value)
    {
        foreach (var scope in constValues)
        {
            if (scope.TryGetValue(name, out value))
                return true;
        }

        value = default;
        return false;
    }

    private static bool IsExactPattern(Expression pattern)
    {
        if (pattern is not LiteralExpression literal ||
            literal.LiteralType is not PrimitiveType primitive)
        {
            return false;
        }

        if (primitive.PrimitiveKind != PrimitiveType.Kind.String)
            return true;

        var text = literal.Value?.ToString() ?? string.Empty;
        return text.IndexOfAny(['*', '?', '[', ']']) < 0;
    }

    private static bool ToBool(ConstValue value)
    {
        return value.Kind switch
        {
            ConstValueKind.Bool => value.BoolValue,
            ConstValueKind.Int => value.IntValue != 0,
            ConstValueKind.String => !string.IsNullOrEmpty(value.StringValue),
            ConstValueKind.Array => value.ArrayValues.Count > 0,
            _ => false
        };
    }

    private static bool CanCompare(ConstValue left, ConstValue right)
    {
        return left.Kind == right.Kind;
    }

    private static bool ConstValuesEqual(ConstValue left, ConstValue right)
    {
        if (left.Kind != right.Kind)
            return false;

        return left.Kind switch
        {
            ConstValueKind.Bool => left.BoolValue == right.BoolValue,
            ConstValueKind.Int => left.IntValue == right.IntValue,
            ConstValueKind.String => string.Equals(left.StringValue, right.StringValue, StringComparison.Ordinal),
            ConstValueKind.Array => ArraysEqual(left.ArrayValues, right.ArrayValues),
            _ => false
        };
    }

    private static bool ArraysEqual(IReadOnlyList<ConstValue> left, IReadOnlyList<ConstValue> right)
    {
        if (left.Count != right.Count)
            return false;

        for (int i = 0; i < left.Count; i++)
        {
            if (!ConstValuesEqual(left[i], right[i]))
                return false;
        }

        return true;
    }

    private void WarnIfShadowing(string name, int line, int column)
    {
        if (scopes.Count <= 1)
            return;

        bool first = true;
        foreach (var scope in scopes)
        {
            if (first)
            {
                first = false;
                continue;
            }

            if (!scope.Symbols.ContainsKey(name))
                continue;

            diagnostics.AddWarning(
                $"Declaration '{name}' shadows an outer scope variable.",
                line,
                column,
                DiagnosticCodes.ShadowedVariable);
            return;
        }
    }

    private void PushScope()
    {
        scopes.Push(new ScopeFrame());
    }

    private void PopScope()
    {
        var scope = scopes.Pop();
        EmitUnusedSymbolWarnings(scope);
    }

    private void DeclareSymbol(string name, SymbolKind kind, int line, int column, bool ignoreUnused)
    {
        scopes.Peek().Symbols[name] = new SymbolEntry(name, kind, line, column, ignoreUnused);
    }

    private void MarkVariableRead(string name)
    {
        foreach (var scope in scopes)
        {
            if (!scope.Symbols.TryGetValue(name, out var symbol))
                continue;

            if (symbol.Kind is SymbolKind.Variable or SymbolKind.Parameter)
                symbol.IsUsed = true;
            return;
        }
    }

    private void MarkFunctionUsed(string name)
    {
        foreach (var scope in scopes)
        {
            if (!scope.Symbols.TryGetValue(name, out var symbol))
                continue;

            if (symbol.Kind == SymbolKind.Function)
                symbol.IsUsed = true;
            return;
        }
    }

    private void EmitUnusedSymbolWarnings(ScopeFrame scope)
    {
        foreach (var symbol in scope.Symbols.Values.OrderBy(static s => s.Line).ThenBy(static s => s.Column))
        {
            if (symbol.IsUsed || symbol.IgnoreUnused)
                continue;

            switch (symbol.Kind)
            {
                case SymbolKind.Variable:
                    diagnostics.AddWarning(
                        $"Variable '{symbol.Name}' is declared but never used.",
                        symbol.Line,
                        symbol.Column,
                        DiagnosticCodes.UnusedVariable);
                    break;

                case SymbolKind.Parameter:
                    diagnostics.AddWarning(
                        $"Parameter '{symbol.Name}' is never used.",
                        symbol.Line,
                        symbol.Column,
                        DiagnosticCodes.UnusedParameter);
                    break;

                case SymbolKind.Function:
                    diagnostics.AddWarning(
                        $"Function '{symbol.Name}' is declared but never called.",
                        symbol.Line,
                        symbol.Column,
                        DiagnosticCodes.UnusedFunction);
                    break;
            }
        }
    }

    private static bool ShouldIgnoreUnusedSymbol(string name)
    {
        return string.IsNullOrEmpty(name) || name.StartsWith('_');
    }

    private void PushTrackedJobs(int count)
    {
        trackedJobs.Push(count);
    }

    private void PopTrackedJobs()
    {
        trackedJobs.Pop();
    }

    private int CurrentTrackedJobs()
    {
        return trackedJobs.Peek();
    }

    private void SetCurrentTrackedJobs(int count)
    {
        trackedJobs.Pop();
        trackedJobs.Push(count);
    }

    private void PushConstScope()
    {
        var scope = constValues.Count == 0
            ? new Dictionary<string, ConstValue>(StringComparer.Ordinal)
            : new Dictionary<string, ConstValue>(constValues.Peek(), StringComparer.Ordinal);
        constValues.Push(scope);
    }

    private void PopConstScope()
    {
        constValues.Pop();
    }

    private void InvalidateConst(string name)
    {
        constValues.Peek().Remove(name);
    }

    private readonly record struct BranchResult(bool Terminated, int TrackedJobs);

    private sealed class ScopeFrame
    {
        public Dictionary<string, SymbolEntry> Symbols { get; } = new(StringComparer.Ordinal);
    }

    private sealed class SymbolEntry
    {
        public SymbolEntry(string name, SymbolKind kind, int line, int column, bool ignoreUnused)
        {
            Name = name;
            Kind = kind;
            Line = line;
            Column = column;
            IgnoreUnused = ignoreUnused;
        }

        public string Name { get; }
        public SymbolKind Kind { get; }
        public int Line { get; }
        public int Column { get; }
        public bool IgnoreUnused { get; }
        public bool IsUsed { get; set; }
    }

    private enum SymbolKind
    {
        Variable,
        Parameter,
        Function
    }

    private enum ConstValueKind
    {
        Bool,
        Int,
        String,
        Array
    }

    private readonly record struct ConstValue(
        ConstValueKind Kind,
        bool BoolValue,
        int IntValue,
        string StringValue,
        IReadOnlyList<ConstValue> ArrayValues)
    {
        public static ConstValue FromBool(bool value) => new(ConstValueKind.Bool, value, 0, string.Empty, Array.Empty<ConstValue>());
        public static ConstValue FromInt(int value) => new(ConstValueKind.Int, false, value, string.Empty, Array.Empty<ConstValue>());
        public static ConstValue FromString(string value) => new(ConstValueKind.String, false, 0, value, Array.Empty<ConstValue>());
        public static ConstValue FromArray(IReadOnlyList<ConstValue> value) => new(ConstValueKind.Array, false, 0, string.Empty, value);
    }
}
