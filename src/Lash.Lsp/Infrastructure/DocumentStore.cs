namespace Lash.Lsp.Infrastructure;

using System.Collections.Concurrent;
using Lash.Compiler.Analysis;
using OmniSharp.Extensions.LanguageServer.Protocol;

internal sealed class DocumentSnapshot
{
    public required DocumentUri Uri { get; init; }
    public required string SourcePath { get; set; }
    public required string Text { get; set; }
    public AnalysisResult? Analysis { get; set; }
}

internal sealed class DocumentStore
{
    private readonly ConcurrentDictionary<DocumentUri, DocumentSnapshot> documents = new();

    public DocumentSnapshot Upsert(DocumentUri uri, string text)
    {
        var sourcePath = uri.ToUri().LocalPath;
        var snapshot = documents.AddOrUpdate(
            uri,
            static (key, state) => new DocumentSnapshot
            {
                Uri = key,
                SourcePath = state.SourcePath,
                Text = state.Text
            },
            static (_, existing, state) =>
            {
                existing.Text = state.Text;
                existing.SourcePath = state.SourcePath;
                return existing;
            },
            (SourcePath: sourcePath, Text: text));

        return snapshot;
    }

    public bool TryGet(DocumentUri uri, out DocumentSnapshot? snapshot)
    {
        var ok = documents.TryGetValue(uri, out var found);
        snapshot = found;
        return ok;
    }

    public void Remove(DocumentUri uri)
    {
        documents.TryRemove(uri, out _);
    }
}
