namespace Lash.Lsp.Handlers;

using Lash.Lsp.Infrastructure;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

internal sealed class CodeActionHandler : CodeActionHandlerBase
{
    private const string LetNeverReassignedCode = "W509";
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

        var actions = new List<CommandOrCodeAction>();

        foreach (var action in CreateVarToLetQuickFixes(request, snapshot))
            actions.Add(new CommandOrCodeAction(action));

        return Task.FromResult<CommandOrCodeActionContainer?>(
            actions.Count == 0 ? new CommandOrCodeActionContainer() : new CommandOrCodeActionContainer(actions));
    }

    public override Task<CodeAction> Handle(CodeAction request, CancellationToken cancellationToken)
    {
        return Task.FromResult(request);
    }

    private IEnumerable<CodeAction> CreateVarToLetQuickFixes(CodeActionParams request, DocumentSnapshot snapshot)
    {
        var emittedLines = new HashSet<long>();
        foreach (var diagnostic in request.Context.Diagnostics)
        {
            var code = diagnostic.Code?.String ?? diagnostic.Code?.Long.ToString() ?? string.Empty;
            if (!string.Equals(code, LetNeverReassignedCode, StringComparison.Ordinal))
                continue;

            var line = diagnostic.Range.Start.Line;
            if (!emittedLines.Add(line))
                continue;

            if (!TryCreateVarToLetEdit(snapshot, line, out var edit))
                continue;

            yield return new CodeAction
            {
                Title = "Change 'var' to 'let'",
                Kind = CodeActionKind.QuickFix,
                Edit = new WorkspaceEdit
                {
                    Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                    {
                        [request.TextDocument.Uri] = new[] { edit }
                    }
                }
            };
        }
    }

    private bool TryCreateVarToLetEdit(DocumentSnapshot snapshot, long zeroBasedLine, out TextEdit edit)
    {
        edit = new TextEdit();
        if (!snapshotText.TryGetLine(snapshot, (int)zeroBasedLine, out var lineText))
            return false;

        var index = 0;
        while (index < lineText.Length && char.IsWhiteSpace(lineText[index]))
            index++;

        if (lineText.AsSpan(index).StartsWith("global ".AsSpan(), StringComparison.Ordinal))
        {
            index += "global".Length;
            while (index < lineText.Length && char.IsWhiteSpace(lineText[index]))
                index++;
        }

        if (index + 3 > lineText.Length || !lineText.AsSpan(index, 3).SequenceEqual("var".AsSpan()))
            return false;

        if (index + 3 < lineText.Length && IsIdentifierPart(lineText[index + 3]))
            return false;

        edit = new TextEdit
        {
            Range = new Range(
                new Position((int)zeroBasedLine, index),
                new Position((int)zeroBasedLine, index + 3)),
            NewText = "let"
        };
        return true;
    }

    private static bool IsIdentifierPart(char c)
    {
        return (c >= 'a' && c <= 'z')
            || (c >= 'A' && c <= 'Z')
            || (c >= '0' && c <= '9')
            || c == '_';
    }
}
