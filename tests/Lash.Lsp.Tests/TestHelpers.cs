using Lash.Compiler.Analysis;
using Lash.Lsp.Infrastructure;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace Lash.Lsp.Tests;

internal static class TestHelpers
{
    public static DocumentSnapshot CreateSnapshot(string source, string? filePath = null)
    {
        var path = filePath ?? Path.Combine(Path.GetTempPath(), $"lash-lsp-test-{Guid.NewGuid():N}.lash");
        var uri = DocumentUri.FromFileSystemPath(path);

        var analyzer = new LashAnalyzer();
        var analysis = analyzer.AnalyzeSource(
            source,
            path,
            new AnalysisOptions(IncludeWarnings: true, BuildSymbolIndex: true));

        return new DocumentSnapshot
        {
            Uri = uri,
            SourcePath = path,
            Text = source,
            Analysis = analysis
        };
    }
}
