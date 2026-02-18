using Lash.Compiler.Diagnostics;
using Lash.Compiler.Frontend;
using Xunit;

namespace Lash.Compiler.Tests.Frontend;

public class SyntaxDiagnosticFormattingTests
{
    [Fact]
    public void ParserErrors_ReportUnknownAtTokensAsUnrecognizedSymbols()
    {
        var diagnostics = Parse(
            """
            let x = #@
            """);

        var error = diagnostics.GetErrors().First();
        Assert.Contains("Unrecognized symbol '@'", error.Message);
    }

    [Fact]
    public void ParserErrors_UseConciseUnexpectedTokenMessage_ForDanglingEnd()
    {
        var diagnostics = Parse(
            """
            let x = 1
            end
            """);

        var error = Assert.Single(diagnostics.GetErrors());
        Assert.Contains("'end'", error.Message);
        Assert.DoesNotContain("expecting {", error.Message);
    }

    [Fact]
    public void ParserErrors_ReportInvalidVariableName()
    {
        var diagnostics = Parse(
            """
            let 1abc = 1
            """);

        Assert.True(diagnostics.HasErrors);
    }

    private static DiagnosticBag Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var path = Path.Combine(Path.GetTempPath(), $"lash-syntax-{Guid.NewGuid():N}.lash");
        File.WriteAllText(path, source);

        try
        {
            ModuleLoader.TryLoadProgram(path, diagnostics, out _);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        return diagnostics;
    }
}
