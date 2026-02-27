namespace Lash.Lsp.Handlers;

using Lash.Compiler.Analysis;
using Lash.Lsp.Infrastructure;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

internal sealed class HoverHandler : HoverHandlerBase
{
    private readonly DocumentStore documents;
    private readonly SymbolQueryService symbols;
    private readonly SnapshotTextService snapshotText;
    private readonly LanguageDocs languageDocs;
    private readonly TextDocumentSelector selector = new(
        new TextDocumentFilter
        {
            Pattern = "**/*.lash",
            Language = "lash"
        });

    public HoverHandler(
        DocumentStore documents,
        SymbolQueryService symbols,
        SnapshotTextService snapshotText,
        LanguageDocs languageDocs)
    {
        this.documents = documents;
        this.symbols = symbols;
        this.snapshotText = snapshotText;
        this.languageDocs = languageDocs;
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(HoverCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = selector
        };

    public override Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGet(request.TextDocument.Uri, out var snapshot) || snapshot is null)
            return Task.FromResult<Hover?>(null);

        if (symbols.TryFindContext(snapshot, request.Position, out var declaration, out _) && declaration is not null)
        {
            var markdown = BuildSymbolMarkdown(declaration);
            return Task.FromResult<Hover?>(new Hover
            {
                Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = markdown
                }),
                Range = LspConversions.ToRange(declaration.DeclarationSpan)
            });
        }

        if (!snapshotText.TryGetTokenAt(snapshot, request.Position, out var token))
            return Task.FromResult<Hover?>(null);

        if (!languageDocs.TryGet(token.Text, out var doc))
            return Task.FromResult<Hover?>(null);

        return Task.FromResult<Hover?>(new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = doc
            }),
            Range = token.Range
        });
    }

    private static string BuildSymbolMarkdown(SymbolInfo symbol)
    {
        var kind = symbol.Kind.ToString().ToLowerInvariant();
        var typeText = string.IsNullOrWhiteSpace(symbol.TypeText) ? string.Empty : $": `{symbol.TypeText}`";
        var constText = symbol.IsConst ? " const" : string.Empty;
        var usageHint = symbol.Kind switch
        {
            LashSymbolKind.Function => "Call with `name(args...)`.",
            LashSymbolKind.Parameter => "Function parameter in current function scope.",
            LashSymbolKind.Constant => "Immutable binding.",
            LashSymbolKind.Enum => "Enum type used with `Enum::Member`.",
            _ => "Lexical variable binding."
        };

        return $"`{kind}{constText}` **{symbol.Name}**{typeText}\n\n{usageHint}";
    }
}
