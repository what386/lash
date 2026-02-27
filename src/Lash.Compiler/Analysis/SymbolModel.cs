namespace Lash.Compiler.Analysis;

public enum LashSymbolKind
{
    Variable,
    Constant,
    Function,
    Parameter,
    Enum
}

public sealed record SymbolSpan(int Line, int Column, int EndColumn);

public sealed record SymbolInfo(
    string Name,
    LashSymbolKind Kind,
    SymbolSpan DeclarationSpan,
    bool IsConst,
    string? TypeText);

public sealed record SymbolReference(
    string Name,
    SymbolSpan Span,
    SymbolInfo? Resolved);

public sealed class SymbolIndex
{
    public IReadOnlyList<SymbolInfo> Declarations { get; init; } = Array.Empty<SymbolInfo>();
    public IReadOnlyList<SymbolReference> References { get; init; } = Array.Empty<SymbolReference>();
}
