namespace Lash.Compiler.Analysis;

using Lash.Compiler.Ast;
using Lash.Compiler.Diagnostics;

public sealed record AnalysisResult(
    ProgramNode? Program,
    IReadOnlyList<Diagnostic> Diagnostics,
    SymbolIndex? Symbols)
{
    public bool HasErrors => Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error);
}
