namespace Lash.Compiler.Preprocessing;

using Lash.Compiler.Diagnostics;

internal sealed class SourcePreprocessor
{
    public string Process(string source, DiagnosticBag diagnostics)
    {
        var normalized = Normalizer.Normalize(source);

        var afterDirectives = new DirectiveProcessor().Process(normalized, diagnostics);

        return afterDirectives;
    }
}
