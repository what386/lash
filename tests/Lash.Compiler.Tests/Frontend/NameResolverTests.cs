using Lash.Compiler.Diagnostics;
using Lash.Compiler.Frontend;
using Lash.Compiler.Frontend.Semantics;
using Lash.Compiler.Tests.TestSupport;
using Xunit;

namespace Lash.Compiler.Tests.Frontend;

public class NameResolverTests
{
    [Fact]
    public void NameResolver_RejectsAssignmentToConst()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            const x = 1
            x = 2
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == "E101" && e.Message.Contains("Cannot assign to const variable 'x'", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics.GetErrors(), e => e.Message.Contains("Type error:", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsGlobalAssignmentToConst()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            global const x = 1
            fn mutate()
                global x = 2
            end
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == "E101" && e.Message.Contains("Cannot assign to const variable 'x'", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_AllowsGlobalAssignmentToMutableVariable()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            global let x = 1
            fn mutate()
                global x = 2
            end
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.DoesNotContain(diagnostics.GetErrors(), e => e.Code == "E101");
    }

    [Fact]
    public void NameResolver_RejectsUnknownEnumMember()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            enum AccountType
                Checking
            end

            let kind = AccountType::Savings
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == "E102" && e.Message.Contains("Unknown enum member 'AccountType::Savings'", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsUseOfUndeclaredVariable()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let x = y + 1
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == "E103" && e.Message.Contains("undeclared variable 'y'", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics.GetErrors(), e => e.Message.Contains("Type error:", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsAssignmentToUndeclaredVariable()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            y = 2
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == "E103" && e.Message.Contains("undeclared variable 'y'", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsCallWithWrongArgumentCount()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn greet(name, greeting = "Hello")
                return greeting + ", " + name
            end

            greet()
            greet("a", "b", "c")
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        var arityErrors = diagnostics.GetErrors().Where(e => e.Code == "E104").ToList();
        Assert.Equal(2, arityErrors.Count);
        Assert.All(arityErrors, error => Assert.Contains("Function 'greet' expects 1..2", error.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_RejectsUnknownFunctionCall()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            missing("x")
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == "E104" && e.Message.Contains("Unknown function 'missing'", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_AllowsBuiltInArgvWithoutDeclaration()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let first = argv[0]
            let count = #argv
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.DoesNotContain(diagnostics.GetErrors(), e => e.Code == "E103");
    }

    [Fact]
    public void NameResolver_RejectsDeclaringBuiltInArgv()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let argv = []
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == "E101" && e.Message.Contains("built-in variable 'argv'", StringComparison.Ordinal));
    }

    [Fact]
    public void NameResolver_AllowsShellCaptureWithoutFunctionDeclaration()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let size = $sh "du -sh ."
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);

        Assert.DoesNotContain(diagnostics.GetErrors(), e => e.Code == "E104");
    }
}
