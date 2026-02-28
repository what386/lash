namespace Lash.Lsp.Handlers;

using Lash.Lsp.Infrastructure;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

internal sealed class CodeActionHandler : CodeActionHandlerBase
{
    private readonly DocumentStore documents;
    private readonly SnapshotTextService snapshotText;
    private readonly TextDocumentSelector selector = new(
        new TextDocumentFilter
        {
            Pattern = "**/*.lash",
            Language = "lash"
        });

    public CodeActionHandler(DocumentStore documents, SnapshotTextService snapshotText)
    {
        this.documents = documents;
        this.snapshotText = snapshotText;
    }

    protected override CodeActionRegistrationOptions CreateRegistrationOptions(
        CodeActionCapability capability,
        ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = selector,
            CodeActionKinds = new Container<CodeActionKind>(CodeActionKind.QuickFix),
            ResolveProvider = false
        };

    public override Task<CommandOrCodeActionContainer?> Handle(CodeActionParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGet(request.TextDocument.Uri, out var snapshot) || snapshot is null)
            return Task.FromResult<CommandOrCodeActionContainer?>(new CommandOrCodeActionContainer());

        if (!HasParseErrorOnLine(request, request.Range.Start.Line))
            return Task.FromResult<CommandOrCodeActionContainer?>(new CommandOrCodeActionContainer());

        if (!snapshotText.TryGetTokenAt(snapshot, request.Range.Start, out var token))
            return Task.FromResult<CommandOrCodeActionContainer?>(new CommandOrCodeActionContainer());

        if (token.Text.StartsWith("@", StringComparison.Ordinal) || !IdentifierRules.IsValidIdentifier(token.Text))
            return Task.FromResult<CommandOrCodeActionContainer?>(new CommandOrCodeActionContainer());

        if (!snapshotText.TryGetLine(snapshot, (int)request.Range.Start.Line, out var lineText))
            return Task.FromResult<CommandOrCodeActionContainer?>(new CommandOrCodeActionContainer());

        var tokenStart = (int)token.Range.Start.Character;
        if (tokenStart > 0 && lineText[tokenStart - 1] == '$')
            return Task.FromResult<CommandOrCodeActionContainer?>(new CommandOrCodeActionContainer());

        var edit = new TextEdit
        {
            Range = new Range(token.Range.Start, token.Range.Start),
            NewText = "$"
        };

        var action = new CodeAction
        {
            Title = $"Prefix '{token.Text}' with '$'",
            Kind = CodeActionKind.QuickFix,
            Edit = new WorkspaceEdit
            {
                Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                {
                    [request.TextDocument.Uri] = new[] { edit }
                }
            }
        };

        return Task.FromResult<CommandOrCodeActionContainer?>(
            new CommandOrCodeActionContainer(new CommandOrCodeAction(action)));
    }

    public override Task<CodeAction> Handle(CodeAction request, CancellationToken cancellationToken)
    {
        return Task.FromResult(request);
    }

    private static bool HasParseErrorOnLine(CodeActionParams request, long line)
    {
        foreach (var diagnostic in request.Context.Diagnostics)
        {
            if (diagnostic.Range.Start.Line != line)
                continue;

            var code = diagnostic.Code?.String ?? diagnostic.Code?.Long.ToString() ?? string.Empty;
            if (string.Equals(code, "E001", StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
