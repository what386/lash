namespace Lash.Lsp.Handlers;

using Lash.Compiler.Analysis;
using Lash.Lsp.Infrastructure;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

internal sealed class HoverHandler : HoverHandlerBase
{
    private readonly DocumentStore documents;
    private readonly TextDocumentSelector selector = new(
        new TextDocumentFilter
        {
            Pattern = "**/*.lash",
            Language = "lash"
        });

    public HoverHandler(DocumentStore documents)
    {
        this.documents = documents;
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(HoverCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = selector
        };

    public override Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGet(request.TextDocument.Uri, out var snapshot) || snapshot?.Analysis?.Symbols is null)
            return Task.FromResult<Hover?>(null);

        var symbols = snapshot.Analysis.Symbols;
        var line = request.Position.Line;
        var column = request.Position.Character;

        var reference = symbols.References.FirstOrDefault(r => LspConversions.Contains(r.Span, line, column));
        var symbol = reference?.Resolved
            ?? symbols.Declarations.FirstOrDefault(d => LspConversions.Contains(d.DeclarationSpan, line, column));
        if (symbol is null)
            return Task.FromResult<Hover?>(null);

        var markdown = BuildMarkdown(symbol);
        return Task.FromResult<Hover?>(new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = markdown
            }),
            Range = LspConversions.ToRange(symbol.DeclarationSpan)
        });
    }

    private static string BuildMarkdown(SymbolInfo symbol)
    {
        var kind = symbol.Kind.ToString().ToLowerInvariant();
        var typeText = string.IsNullOrWhiteSpace(symbol.TypeText) ? string.Empty : $": `{symbol.TypeText}`";
        var constText = symbol.IsConst ? " const" : string.Empty;
        return $"`{kind}{constText}` **{symbol.Name}**{typeText}";
    }
}
