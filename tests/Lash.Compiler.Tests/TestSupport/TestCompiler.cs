using Lash.Compiler.Ast;
using Lash.Compiler.Diagnostics;
using Lash.Compiler.Frontend;
using Xunit;

namespace Lash.Compiler.Tests.TestSupport;

internal static class TestCompiler
{
    public static (bool Success, ProgramNode? Program, DiagnosticBag Diagnostics) LoadProgram(string source)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lash-test-{Guid.NewGuid():N}.lash");
        File.WriteAllText(path, source);

        try
        {
            var diagnostics = new DiagnosticBag();
            var success = ModuleLoader.TryLoadProgram(path, diagnostics, out var program);
            return (success, program, diagnostics);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    public static ProgramNode ParseOrThrow(string source)
    {
        var result = LoadProgram(source);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        return Assert.IsType<ProgramNode>(result.Program);
    }
}
