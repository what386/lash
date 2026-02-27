namespace Lash.Compiler.Analysis;

using Lash.Compiler.Ast;
using Lash.Compiler.Diagnostics;
using Lash.Compiler.Frontend;
using Lash.Compiler.Frontend.Semantics;

public sealed class LashAnalyzer
{
    public AnalysisResult AnalyzePath(string path, AnalysisOptions? options = null)
    {
        options ??= new AnalysisOptions();

        var diagnostics = new DiagnosticBag();
        ProgramNode? program;
        if (!ModuleLoader.TryLoadProgram(path, diagnostics, out program) || program is null)
        {
            return new AnalysisResult(
                Program: null,
                Diagnostics: diagnostics.GetDiagnostics().ToArray(),
                Symbols: null);
        }

        return AnalyzeProgram(program, diagnostics, options);
    }

    public AnalysisResult AnalyzeSource(string source, string sourcePath, AnalysisOptions? options = null)
    {
        options ??= new AnalysisOptions();

        var diagnostics = new DiagnosticBag();
        ProgramNode? program;
        if (!ModuleLoader.TryLoadProgramFromSource(source, sourcePath, diagnostics, out program) || program is null)
        {
            return new AnalysisResult(
                Program: null,
                Diagnostics: diagnostics.GetDiagnostics().ToArray(),
                Symbols: null);
        }

        return AnalyzeProgram(program, diagnostics, options);
    }

    private static AnalysisResult AnalyzeProgram(ProgramNode program, DiagnosticBag diagnostics, AnalysisOptions options)
    {
        new NameResolver(diagnostics).Analyze(program);
        if (!diagnostics.HasErrors)
        {
            new TypeChecker(diagnostics).Analyze(program);
        }

        if (!diagnostics.HasErrors)
        {
            new DefiniteAssignmentAnalyzer(diagnostics).Analyze(program);
        }

        if (!diagnostics.HasErrors)
        {
            new ConstantSafetyAnalyzer(diagnostics).Analyze(program);
        }

        if (!diagnostics.HasErrors)
        {
            new CodegenFeasibilityAnalyzer(diagnostics).Analyze(program);
        }

        if (options.IncludeWarnings)
        {
            new WarningAnalyzer(diagnostics).Analyze(program);
        }

        SymbolIndex? symbolIndex = null;
        if (options.BuildSymbolIndex && !diagnostics.HasErrors)
        {
            symbolIndex = new SymbolIndexBuilder().Build(program);
        }

        return new AnalysisResult(
            Program: program,
            Diagnostics: diagnostics.GetDiagnostics().ToArray(),
            Symbols: symbolIndex);
    }
}
