namespace Lash.Formatter.Core;

public sealed record FormatterOptions
{
    public int SpacesPerIndent { get; init; } = 4;
    public bool CollapseConsecutiveBlankLines { get; init; } = true;
    public bool EnsureTrailingNewline { get; init; } = true;
    public int MaxLineLength { get; init; } = 100;
    public int WrappedArgumentIndent { get; init; } = 4;

    public static FormatterOptions Default { get; } = new();
}
