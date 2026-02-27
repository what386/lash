namespace Lash.Lsp.Handlers;

using Lash.Compiler.Analysis;
using Lash.Lsp.Infrastructure;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

internal sealed class PrepareRenameHandler : PrepareRenameHandlerBase
{
    private readonly DocumentStore documents;
    private readonly SymbolQueryService symbols;
    private readonly TextDocumentSelector selector = new(
        new TextDocumentFilter
        {
            Pattern = "**/*.lash",
            Language = "lash"
        });

    public PrepareRenameHandler(DocumentStore documents, SymbolQueryService symbols)
    {
        this.documents = documents;
        this.symbols = symbols;
    }

    protected override RenameRegistrationOptions CreateRegistrationOptions(RenameCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = selector,
            PrepareProvider = true
        };

    public override Task<RangeOrPlaceholderRange?> Handle(PrepareRenameParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGet(request.TextDocument.Uri, out var snapshot) || snapshot is null)
            throw InvalidRequest("Rename is only available for open Lash documents.");

        if (!symbols.TryFindContext(snapshot, request.Position, out var declaration, out var reference) || declaration is null)
            throw InvalidRequest("Selected token is not a renameable Lash symbol.");

        if (!IsRenameableKind(declaration.Kind))
            throw InvalidRequest($"Symbol '{declaration.Name}' is not renameable.");

        var range = reference is not null
            ? LspConversions.ToRange(reference.Span)
            : symbols.TryGetLocalDeclarationLocation(snapshot, declaration, out var declarationLocation)
                ? declarationLocation.Range
                : LspConversions.ToRange(declaration.DeclarationSpan);
        return Task.FromResult<RangeOrPlaceholderRange?>(new RangeOrPlaceholderRange(range));
    }

    private static bool IsRenameableKind(LashSymbolKind kind)
    {
        return kind is LashSymbolKind.Variable
            or LashSymbolKind.Constant
            or LashSymbolKind.Function
            or LashSymbolKind.Parameter
            or LashSymbolKind.Enum;
    }

    private static RpcErrorException InvalidRequest(string message)
    {
        return new RpcErrorException(-32602, new { reason = "invalid-rename-target" }, message);
    }
}
