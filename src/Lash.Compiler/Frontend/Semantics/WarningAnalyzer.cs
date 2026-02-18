namespace Lash.Compiler.Frontend.Semantics;

using Lash.Compiler.Ast;
using Lash.Compiler.Ast.Statements;
using Lash.Compiler.Diagnostics;

public sealed class WarningAnalyzer
{
    private readonly DiagnosticBag diagnostics;
    private readonly Stack<HashSet<string>> scopeDeclarations = new();
    private readonly Stack<int> trackedJobs = new();

    public WarningAnalyzer(DiagnosticBag diagnostics)
    {
        this.diagnostics = diagnostics;
    }

    public void Analyze(ProgramNode program)
    {
        PushScope();
        PushTrackedJobs(0);
        AnalyzeBlock(program.Statements, inLoop: false);
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
                return false;

            case Assignment:
                return false;

            case FunctionDeclaration function:
                WarnIfShadowing(function.Name, function.Line, function.Column);
                DeclareInCurrentScope(function.Name);

                PushScope();
                PushTrackedJobs(0);
                foreach (var parameter in function.Parameters)
                {
                    WarnIfShadowing(parameter.Name, parameter.Line, parameter.Column);
                    DeclareInCurrentScope(parameter.Name);
                }
                AnalyzeBlock(function.Body, inLoop: false);
                PopTrackedJobs();
                PopScope();
                return false;

            case IfStatement ifStatement:
                {
                    var baseTracked = CurrentTrackedJobs();
                    var branchTracked = new List<int>();

                    PushScope();
                    PushTrackedJobs(baseTracked);
                    var thenTerminates = AnalyzeBlock(ifStatement.ThenBlock, inLoop);
                    branchTracked.Add(CurrentTrackedJobs());
                    PopTrackedJobs();
                    PopScope();

                    var elifTerminates = new List<bool>();
                    foreach (var clause in ifStatement.ElifClauses)
                    {
                        PushScope();
                        PushTrackedJobs(baseTracked);
                        elifTerminates.Add(AnalyzeBlock(clause.Body, inLoop));
                        branchTracked.Add(CurrentTrackedJobs());
                        PopTrackedJobs();
                        PopScope();
                    }

                    bool elseTerminates = false;
                    if (ifStatement.ElseBlock.Count > 0)
                    {
                        PushScope();
                        PushTrackedJobs(baseTracked);
                        elseTerminates = AnalyzeBlock(ifStatement.ElseBlock, inLoop);
                        branchTracked.Add(CurrentTrackedJobs());
                        PopTrackedJobs();
                        PopScope();
                    }
                    else
                    {
                        branchTracked.Add(baseTracked);
                    }

                    SetCurrentTrackedJobs(branchTracked.Max());
                    return thenTerminates
                           && elifTerminates.All(t => t)
                           && ifStatement.ElseBlock.Count > 0
                           && elseTerminates;
                }

            case SwitchStatement switchStatement:
                {
                    bool allCasesTerminate = true;
                    var baseTracked = CurrentTrackedJobs();
                    var branchTracked = new List<int>();

                    foreach (var clause in switchStatement.Cases)
                    {
                        PushScope();
                        PushTrackedJobs(baseTracked);
                        var caseTerminates = AnalyzeBlock(clause.Body, inLoop);
                        allCasesTerminate &= caseTerminates;
                        branchTracked.Add(CurrentTrackedJobs());
                        PopTrackedJobs();
                        PopScope();
                    }

                    if (branchTracked.Count > 0)
                        SetCurrentTrackedJobs(branchTracked.Max());
                    return allCasesTerminate && switchStatement.Cases.Count > 0;
                }

            case ForLoop forLoop:
                PushScope();
                PushTrackedJobs(CurrentTrackedJobs());
                WarnIfShadowing(forLoop.Variable, forLoop.Line, forLoop.Column);
                DeclareInCurrentScope(forLoop.Variable);
                AnalyzeBlock(forLoop.Body, inLoop: true);
                PopTrackedJobs();
                PopScope();
                return false;

            case WhileLoop whileLoop:
                PushScope();
                PushTrackedJobs(CurrentTrackedJobs());
                AnalyzeBlock(whileLoop.Body, inLoop: true);
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
                AnalyzeBlock(subshellStatement.Body, inLoop: false);
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
}
