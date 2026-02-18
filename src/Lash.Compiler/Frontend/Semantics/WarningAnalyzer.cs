namespace Lash.Compiler.Frontend.Semantics;

using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;
using Lash.Compiler.Ast.Types;
using Lash.Compiler.Diagnostics;

public sealed class WarningAnalyzer
{
    private readonly DiagnosticBag diagnostics;
    private readonly Stack<HashSet<string>> scopeDeclarations = new();
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
                WarnIfShadowing(variable.Name, variable.Line, variable.Column);
                DeclareInCurrentScope(variable.Name);
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
                if (assignment.Target is IdentifierExpression identifier)
                    constValues.Peek().Remove(identifier.Name);
                return false;

            case FunctionDeclaration function:
                WarnIfShadowing(function.Name, function.Line, function.Column);
                DeclareInCurrentScope(function.Name);

                PushScope();
                PushTrackedJobs(0);
                PushConstScope();
                foreach (var parameter in function.Parameters)
                {
                    WarnIfShadowing(parameter.Name, parameter.Line, parameter.Column);
                    DeclareInCurrentScope(parameter.Name);
                    constValues.Peek().Remove(parameter.Name);
                }
                AnalyzeBlock(function.Body, inLoop: false);
                PopConstScope();
                PopTrackedJobs();
                PopScope();
                return false;

            case IfStatement ifStatement:
                return AnalyzeIfStatement(ifStatement, inLoop);

            case SwitchStatement switchStatement:
                return AnalyzeSwitchStatement(switchStatement, inLoop);

            case ForLoop forLoop:
                PushScope();
                PushTrackedJobs(CurrentTrackedJobs());
                PushConstScope();
                WarnIfShadowing(forLoop.Variable, forLoop.Line, forLoop.Column);
                DeclareInCurrentScope(forLoop.Variable);
                constValues.Peek().Remove(forLoop.Variable);
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

            case ReturnStatement:
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
                SetCurrentTrackedJobs(0);
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
            if (!TryEvaluateBool(clause.Condition, out var clauseValue))
            {
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
            return AnalyzeSwitchPaths(switchStatement.Cases, inLoop, CurrentTrackedJobs());

        for (int i = 0; i < switchStatement.Cases.Count; i++)
        {
            var clause = switchStatement.Cases[i];
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
        bool allCasesTerminate = true;
        var branchTracked = new List<int>();

        foreach (var clause in cases)
        {
            var result = AnalyzeBranch(clause.Body, inLoop, baseTrackedJobs);
            allCasesTerminate &= result.Terminated;
            branchTracked.Add(result.TrackedJobs);
        }

        if (branchTracked.Count > 0)
            SetCurrentTrackedJobs(branchTracked.Max());
        return allCasesTerminate && cases.Count > 0;
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
                if (constValues.Peek().TryGetValue(identifier.Name, out value))
                    return true;
                break;

            case UnaryExpression unary:
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
                    case "&&":
                        value = ConstValue.FromBool(ToBool(left) && ToBool(right));
                        return true;
                    case "||":
                        value = ConstValue.FromBool(ToBool(left) || ToBool(right));
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
                }
                break;
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
            _ => false
        };
    }

    private void WarnIfShadowing(string name, int line, int column)
    {
        if (scopeDeclarations.Count <= 1)
            return;

        bool first = true;
        foreach (var scope in scopeDeclarations)
        {
            if (first)
            {
                first = false;
                continue;
            }

            if (!scope.Contains(name))
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
        scopeDeclarations.Push(new HashSet<string>(StringComparer.Ordinal));
    }

    private void PopScope()
    {
        scopeDeclarations.Pop();
    }

    private void DeclareInCurrentScope(string name)
    {
        scopeDeclarations.Peek().Add(name);
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

    private readonly record struct BranchResult(bool Terminated, int TrackedJobs);

    private enum ConstValueKind
    {
        Bool,
        Int,
        String
    }

    private readonly record struct ConstValue(ConstValueKind Kind, bool BoolValue, int IntValue, string StringValue)
    {
        public static ConstValue FromBool(bool value) => new(ConstValueKind.Bool, value, 0, string.Empty);
        public static ConstValue FromInt(int value) => new(ConstValueKind.Int, false, value, string.Empty);
        public static ConstValue FromString(string value) => new(ConstValueKind.String, false, 0, value);
    }
}
