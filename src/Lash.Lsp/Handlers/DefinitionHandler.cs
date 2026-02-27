namespace Lash.Lsp.Handlers;

using Lash.Lsp.Infrastructure;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

internal sealed class DefinitionHandler : DefinitionHandlerBase
{
    private readonly DocumentStore documents;
    private readonly SymbolQueryService symbols;
    private readonly TextDocumentSelector selector = new(
        new TextDocumentFilter
        {
            Pattern = "**/*.lash",
            Language = "lash"
        });

    public DefinitionHandler(DocumentStore documents, SymbolQueryService symbols)
    {
        this.documents = documents;
        this.symbols = symbols;
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(DefinitionCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = selector
        };

    public override Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGet(request.TextDocument.Uri, out var snapshot) || snapshot is null)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        if (!symbols.TryFindContext(snapshot, request.Position, out var declaration, out _)
            || declaration is null
            || !symbols.TryGetLocalDeclarationLocation(snapshot, declaration, out var location))
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(new LocationOrLocationLink[] { location }));
    }
}
