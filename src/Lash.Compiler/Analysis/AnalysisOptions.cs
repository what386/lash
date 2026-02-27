namespace Lash.Compiler.Analysis;

public sealed record AnalysisOptions(
    bool IncludeWarnings = true,
    bool BuildSymbolIndex = false);
