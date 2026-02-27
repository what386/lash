namespace Lash.Lsp.Handlers;

using Lash.Lsp.Infrastructure;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

internal sealed class DefinitionHandler : DefinitionHandlerBase
{
    private readonly DocumentStore documents;
    private readonly TextDocumentSelector selector = new(
        new TextDocumentFilter
        {
            Pattern = "**/*.lash",
            Language = "lash"
        });

    public DefinitionHandler(DocumentStore documents)
    {
        this.documents = documents;
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(DefinitionCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = selector
        };

    public override Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGet(request.TextDocument.Uri, out var snapshot) || snapshot?.Analysis?.Symbols is null)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        var symbols = snapshot.Analysis.Symbols;
        var line = request.Position.Line;
        var column = request.Position.Character;

        var reference = symbols.References.FirstOrDefault(r => LspConversions.Contains(r.Span, line, column));
        if (reference?.Resolved is null)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        var location = new Location
        {
            Uri = request.TextDocument.Uri,
            Range = LspConversions.ToRange(reference.Resolved.DeclarationSpan)
        };

        return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(new LocationOrLocationLink[] { location }));
    }
}
