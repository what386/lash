namespace Lash.Lsp.Handlers;

using Lash.Lsp.Infrastructure;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;

internal sealed class TextDocumentSyncHandler : TextDocumentSyncHandlerBase
{
    private readonly ILanguageServerFacade server;
    private readonly DocumentStore documentStore;
    private readonly AnalysisService analysisService;
    private readonly TextDocumentSelector documentSelector = new(
        new TextDocumentFilter
        {
            Pattern = "**/*.lash",
            Language = "lash"
        });

    public TextDocumentSyncHandler(
        ILanguageServerFacade server,
        DocumentStore documentStore,
        AnalysisService analysisService)
    {
        this.server = server;
        this.documentStore = documentStore;
        this.analysisService = analysisService;
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
        => new(uri, "lash");

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability,
        ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = documentSelector,
            Change = TextDocumentSyncKind.Full,
            Save = new SaveOptions { IncludeText = true }
        };

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        var snapshot = documentStore.Upsert(request.TextDocument.Uri, request.TextDocument.Text);
        PublishDiagnostics(snapshot);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        var text = request.ContentChanges.LastOrDefault()?.Text ?? string.Empty;
        var snapshot = documentStore.Upsert(request.TextDocument.Uri, text);
        PublishDiagnostics(snapshot);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
    {
        documentStore.Remove(request.TextDocument.Uri);
        server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = request.TextDocument.Uri,
            Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>()
        });

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
    {
        if (request.Text is null)
            return Unit.Task;

        var snapshot = documentStore.Upsert(request.TextDocument.Uri, request.Text);
        PublishDiagnostics(snapshot);
        return Unit.Task;
    }

    private void PublishDiagnostics(DocumentSnapshot snapshot)
    {
        var analysis = analysisService.Analyze(snapshot);
        var diagnostics = analysis.Diagnostics.Select(LspConversions.ToLspDiagnostic).ToList();

        server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = snapshot.Uri,
            Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>(diagnostics)
        });
    }
}
