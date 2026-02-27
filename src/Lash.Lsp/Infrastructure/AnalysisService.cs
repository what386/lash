namespace Lash.Lsp.Infrastructure;

using Lash.Compiler.Analysis;

internal sealed class AnalysisService
{
    private static readonly AnalysisOptions Options = new(
        IncludeWarnings: true,
        BuildSymbolIndex: true);

    private readonly LashAnalyzer analyzer = new();

    public AnalysisResult Analyze(DocumentSnapshot snapshot)
    {
        var result = analyzer.AnalyzeSource(snapshot.Text, snapshot.SourcePath, Options);
        snapshot.Analysis = result;
        return result;
    }
}
