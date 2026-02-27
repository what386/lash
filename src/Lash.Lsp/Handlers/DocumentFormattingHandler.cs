namespace Lash.Lsp.Handlers;

using Lash.Formatter.Api;
using Lash.Lsp.Infrastructure;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

internal sealed class DocumentFormattingHandler : DocumentFormattingHandlerBase
{
    private readonly DocumentStore documents;
    private readonly TextDocumentSelector selector = new(
        new TextDocumentFilter
        {
            Pattern = "**/*.lash",
            Language = "lash"
        });

    public DocumentFormattingHandler(DocumentStore documents)
    {
        this.documents = documents;
    }

    protected override DocumentFormattingRegistrationOptions CreateRegistrationOptions(
        DocumentFormattingCapability capability,
        ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = selector
        };

    public override Task<TextEditContainer?> Handle(DocumentFormattingParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGet(request.TextDocument.Uri, out var snapshot) || snapshot is null)
            return Task.FromResult<TextEditContainer?>(new TextEditContainer());

        var original = snapshot.Text;
        var formatted = LashFormatter.Format(original);
        if (formatted == original)
            return Task.FromResult<TextEditContainer?>(new TextEditContainer());

        snapshot.Text = formatted;

        var edit = new TextEdit
        {
            Range = FullDocumentRange(original),
            NewText = formatted
        };

        return Task.FromResult<TextEditContainer?>(new TextEditContainer(new[] { edit }));
    }

    private static Range FullDocumentRange(string text)
    {
        if (text.Length == 0)
            return new Range(new Position(0, 0), new Position(0, 0));

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var lastLineIndex = Math.Max(0, lines.Length - 1);
        var lastChar = lines[lastLineIndex].Length;

        return new Range(new Position(0, 0), new Position(lastLineIndex, lastChar));
    }
}
