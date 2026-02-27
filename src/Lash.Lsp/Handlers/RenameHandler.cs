namespace Lash.Lsp.Handlers;

using Lash.Compiler.Analysis;
using Lash.Lsp.Infrastructure;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

internal sealed class RenameHandler : RenameHandlerBase
{
    private readonly DocumentStore documents;
    private readonly SymbolQueryService symbols;
    private readonly TextDocumentSelector selector = new(
        new TextDocumentFilter
        {
            Pattern = "**/*.lash",
            Language = "lash"
        });

    public RenameHandler(DocumentStore documents, SymbolQueryService symbols)
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

    public override Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGet(request.TextDocument.Uri, out var snapshot) || snapshot is null)
            throw InvalidRequest("Rename is only available for open Lash documents.");

        if (!symbols.TryFindContext(snapshot, request.Position, out var declaration, out _) || declaration is null)
            throw InvalidRequest("Selected token is not a renameable Lash symbol.");

        if (!IsRenameableKind(declaration.Kind))
            throw InvalidRequest($"Symbol '{declaration.Name}' is not renameable.");

        var newName = request.NewName?.Trim() ?? string.Empty;
        if (!IdentifierRules.IsValidIdentifier(newName))
            throw InvalidRequest($"'{newName}' is not a valid Lash identifier.");

        if (IdentifierRules.IsReservedKeyword(newName))
            throw InvalidRequest($"'{newName}' is a reserved Lash keyword and cannot be used as an identifier.");

        if (string.Equals(newName, declaration.Name, StringComparison.Ordinal))
            return Task.FromResult<WorkspaceEdit?>(new WorkspaceEdit());

        if (!symbols.TryGetLocalDeclarationLocation(snapshot, declaration, out var declarationLocation))
            throw InvalidRequest("Unable to resolve declaration location for rename.");

        var refs = symbols.GetLocalReferencesForDeclaration(snapshot, declaration);
        var edits = new List<TextEdit>
        {
            new()
            {
                Range = declarationLocation.Range,
                NewText = newName
            }
        };

        foreach (var reference in refs)
        {
            edits.Add(new TextEdit
            {
                Range = LspConversions.ToRange(reference.Span),
                NewText = newName
            });
        }

        var deduped = edits
            .GroupBy(static e => $"{e.Range.Start.Line}:{e.Range.Start.Character}:{e.Range.End.Line}:{e.Range.End.Character}")
            .Select(static g => g.First())
            .ToArray();

        var changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
        {
            [snapshot.Uri] = deduped
        };

        return Task.FromResult<WorkspaceEdit?>(new WorkspaceEdit
        {
            Changes = changes
        });
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
        return new RpcErrorException(-32602, new { reason = "invalid-rename-request" }, message);
    }
}
