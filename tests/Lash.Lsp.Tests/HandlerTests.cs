using Lash.Lsp.Handlers;
using Lash.Lsp.Infrastructure;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Xunit;

namespace Lash.Lsp.Tests;

public class HandlerTests
{
    [Fact]
    public async Task CompletionHandler_IncludesSymbolsKeywordsAndSnippets()
    {
        var snapshot = TestHelpers.CreateSnapshot(
            """
            let alpha = 1
            let beta = $alpha

            """);

        var store = new DocumentStore();
        var tracked = store.Upsert(snapshot.Uri, snapshot.Text);
        tracked.Analysis = snapshot.Analysis;

        var handler = new CompletionHandler(store, new SymbolQueryService(new SnapshotTextService()), new SnapshotTextService());
        var result = await handler.Handle(
            new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = snapshot.Uri },
                Position = new Position(2, 0)
            },
            CancellationToken.None);

        var labels = result.Items.Select(static i => i.Label).ToList();
        Assert.Contains("alpha", labels);
        Assert.Contains("if", labels);
        Assert.Contains("if-else", labels);

        var alphaItem = result.Items.First(i => i.Label == "alpha");
        Assert.Equal("$alpha", alphaItem.InsertText);
    }

    [Fact]
    public async Task CodeActionHandler_OffersPrefixDollarQuickFixForParseErrorIdentifier()
    {
        var snapshot = TestHelpers.CreateSnapshot(
            """
            let value = 1
            if value == 1
                echo "ok"
            end
            """);

        var store = new DocumentStore();
        var tracked = store.Upsert(snapshot.Uri, snapshot.Text);
        tracked.Analysis = snapshot.Analysis;

        var handler = new CodeActionHandler(store, new SnapshotTextService());
        var result = await handler.Handle(
            new CodeActionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = snapshot.Uri },
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(new Position(1, 3), new Position(1, 8)),
                Context = new CodeActionContext
                {
                    Diagnostics = new Container<Diagnostic>(
                        new Diagnostic
                        {
                            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(new Position(1, 3), new Position(1, 8)),
                            Code = "E001",
                            Source = "lash",
                            Message = "Invalid syntax near 'value'"
                        })
                }
            },
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotEmpty(result!);
        var action = Assert.IsType<CodeAction>(result!.First().CodeAction);
        Assert.Contains("Prefix 'value' with '$'", action.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareRenameHandler_ReturnsRangeForRenameableSymbol()
    {
        var snapshot = TestHelpers.CreateSnapshot("let value = 1\nlet x = $value\n");

        var store = new DocumentStore();
        var tracked = store.Upsert(snapshot.Uri, snapshot.Text);
        tracked.Analysis = snapshot.Analysis;

        var handler = new PrepareRenameHandler(store, new SymbolQueryService(new SnapshotTextService()));
        var result = await handler.Handle(
            new PrepareRenameParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = snapshot.Uri },
                Position = new Position(0, 5)
            },
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.IsRange);
    }

    [Fact]
    public async Task RenameHandler_RejectsInvalidIdentifierNames()
    {
        var snapshot = TestHelpers.CreateSnapshot("let value = 1\nlet x = $value\n");

        var store = new DocumentStore();
        var tracked = store.Upsert(snapshot.Uri, snapshot.Text);
        tracked.Analysis = snapshot.Analysis;

        var handler = new RenameHandler(store, new SymbolQueryService(new SnapshotTextService()));

        var ex = await Assert.ThrowsAsync<RpcErrorException>(
            async () => await handler.Handle(
                new RenameParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = snapshot.Uri },
                    Position = new Position(0, 5),
                    NewName = "1bad"
                },
                CancellationToken.None));

        Assert.Contains("valid Lash identifier", ex.Message, StringComparison.Ordinal);
    }
}
