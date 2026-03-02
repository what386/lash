namespace Lash.Compiler.Preprocessing;

using Lash.Compiler.Diagnostics;

internal sealed class SourcePreprocessor
{
    public string Process(string source, DiagnosticBag diagnostics, string? sourcePath = null)
    {
        ShebangValidator.Validate(source, diagnostics);
        return new DirectiveProcessor().Process(source, diagnostics, sourcePath);
    }
}
