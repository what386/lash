namespace Lash.Lsp.Infrastructure;

using Lash.Compiler.Analysis;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

internal sealed class SymbolQueryService
{
    private readonly SnapshotTextService snapshotText;

    public SymbolQueryService(SnapshotTextService snapshotText)
    {
        this.snapshotText = snapshotText;
    }

    public bool TryFindContext(DocumentSnapshot snapshot, Position position, out SymbolInfo? declaration, out SymbolReference? reference)
    {
        declaration = null;
        reference = null;

        var symbols = snapshot.Analysis?.Symbols;
        if (symbols is null)
            return false;

        reference = symbols.References.FirstOrDefault(r =>
            LspConversions.Contains(r.Span, (int)position.Line, (int)position.Character)
            && IsLocalReference(snapshot, r));

        if (reference?.Resolved is not null)
        {
            declaration = reference.Resolved;
            return true;
        }

        declaration = symbols.Declarations.FirstOrDefault(d =>
        {
            if (!TryResolveDeclarationSpan(snapshot, d, out var span))
                return false;

            return LspConversions.Contains(span, (int)position.Line, (int)position.Character);
        });

        return declaration is not null;
    }

    public bool TryGetLocalDeclarationLocation(DocumentSnapshot snapshot, SymbolInfo declaration, out Location location)
    {
        if (!TryResolveDeclarationSpan(snapshot, declaration, out var span))
        {
            location = new Location
            {
                Uri = snapshot.Uri,
                Range = new Range(new Position(0, 0), new Position(0, 0))
            };
            return false;
        }

        location = new Location
        {
            Uri = snapshot.Uri,
            Range = LspConversions.ToRange(span)
        };

        return true;
    }

    public IReadOnlyList<SymbolReference> GetLocalReferencesForDeclaration(DocumentSnapshot snapshot, SymbolInfo declaration)
    {
        var symbols = snapshot.Analysis?.Symbols;
        if (symbols is null)
            return Array.Empty<SymbolReference>();

        var results = new List<SymbolReference>();
        foreach (var reference in symbols.References)
        {
            if (reference.Resolved is null)
                continue;

            if (!MatchesDeclaration(reference.Resolved, declaration))
                continue;

            if (!IsLocalReference(snapshot, reference))
                continue;

            results.Add(reference);
        }

        return results;
    }

    public IReadOnlyList<SymbolInfo> GetLocalDeclarations(DocumentSnapshot snapshot)
    {
        var symbols = snapshot.Analysis?.Symbols;
        if (symbols is null)
            return Array.Empty<SymbolInfo>();

        return symbols.Declarations
            .Where(d => IsLocalDeclaration(snapshot, d))
            .ToList();
    }

    private bool IsLocalDeclaration(DocumentSnapshot snapshot, SymbolInfo declaration)
    {
        return TryResolveDeclarationSpan(snapshot, declaration, out _);
    }

    private bool IsLocalReference(DocumentSnapshot snapshot, SymbolReference reference)
    {
        var span = reference.Span;
        if (!snapshotText.IsSpanBackedBySnapshotText(snapshot, span.Line, span.Column, span.EndColumn, reference.Name))
            return false;

        if (reference.Resolved is null)
            return false;

        return TryResolveDeclarationSpan(snapshot, reference.Resolved, out _);
    }

    private bool TryResolveDeclarationSpan(DocumentSnapshot snapshot, SymbolInfo declaration, out SymbolSpan span)
    {
        span = declaration.DeclarationSpan;
        if (snapshotText.IsSpanBackedBySnapshotText(snapshot, span.Line, span.Column, span.EndColumn, declaration.Name))
            return true;

        if (!snapshotText.TryFindIdentifierInLine(snapshot, span.Line, declaration.Name, out var startColumn))
            return false;

        span = new SymbolSpan(span.Line, startColumn, startColumn + declaration.Name.Length);
        return true;
    }

    private static bool MatchesDeclaration(SymbolInfo left, SymbolInfo right)
    {
        return string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            && left.DeclarationSpan.Line == right.DeclarationSpan.Line
            && left.DeclarationSpan.Column == right.DeclarationSpan.Column
            && left.DeclarationSpan.EndColumn == right.DeclarationSpan.EndColumn;
    }
}
