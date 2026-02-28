using Lash.Lsp.Infrastructure;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Xunit;

namespace Lash.Lsp.Tests;

public class InfrastructureTests
{
    [Fact]
    public void SnapshotTextService_FindsDirectiveAndOperatorTokens()
    {
        var service = new SnapshotTextService();
        var snapshot = TestHelpers.CreateSnapshot("@import \"x\"\nlet y = a && b\n");

        var directive = service.TryGetTokenAt(snapshot, new Position(0, 1), out var directiveToken);
        var op = service.TryGetTokenAt(snapshot, new Position(1, 12), out var opToken);

        Assert.True(directive);
        Assert.Equal("@import", directiveToken.Text);
        Assert.True(op);
        Assert.Equal("&&", opToken.Text);
    }

    [Fact]
    public void SymbolQueryService_FiltersSymbolsNotBackedByCurrentSnapshotText()
    {
        var snapshot = TestHelpers.CreateSnapshot(
            """
            let x = 1
            let y = x
            """);

        snapshot.Text =
            """
            let z = 1
            let y = z
            """;

        var query = new SymbolQueryService(new SnapshotTextService());
        var declarations = query.GetLocalDeclarations(snapshot);

        Assert.DoesNotContain(declarations, d => d.Name == "x");
        Assert.DoesNotContain(declarations, d => d.Name == "z");
    }

    [Fact]
    public void LanguageDocs_ReturnsDocsForDirectiveAndFdDup()
    {
        var docs = new LanguageDocs();

        Assert.True(docs.TryGet("@raw", out var rawDoc));
        Assert.Contains("generated Bash", rawDoc, StringComparison.Ordinal);

        Assert.True(docs.TryGet("3>&1", out var fdDupDoc));
        Assert.Contains("Duplicate file descriptor", fdDupDoc, StringComparison.Ordinal);

        Assert.True(docs.TryGet("test", out var testDoc));
        Assert.Contains("$test", testDoc, StringComparison.Ordinal);

        Assert.True(docs.TryGet("trap", out var trapDoc));
        Assert.Contains("SIGNAL", trapDoc, StringComparison.Ordinal);
    }
}
