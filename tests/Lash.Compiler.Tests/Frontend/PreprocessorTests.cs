using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;
using Lash.Compiler.Tests.TestSupport;
using Xunit;

namespace Lash.Compiler.Tests.Frontend;

public class PreprocessorTests
{
    [Fact]
    public void Preprocessor_StripsLineAndBlockComments()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            // full line comment
            let x = 1 // trailing comment
            /*
               let hidden = 0
            */
            let y = 2
            """);

        Assert.Equal(2, program.Statements.Count);
        Assert.Contains(program.Statements, s => s is VariableDeclaration { Name: "x" });
        Assert.Contains(program.Statements, s => s is VariableDeclaration { Name: "y" });
    }

    [Fact]
    public void Preprocessor_KeepsHashUnaryExpressions()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let items = ["a", "b"]
            let count = #items
            """);

        var count = Assert.IsType<VariableDeclaration>(program.Statements[1]);
        var unary = Assert.IsType<UnaryExpression>(count.Value);
        Assert.Equal("#", unary.Operator);
    }

    [Fact]
    public void Preprocessor_DoesNotRewriteLinesInsideMultilineStrings()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let value = [[line1
            echo "still text"
            line3]]
            """);

        var decl = Assert.IsType<VariableDeclaration>(Assert.Single(program.Statements));
        var literal = Assert.IsType<LiteralExpression>(decl.Value);
        Assert.Contains("echo \"still text\"", Assert.IsType<string>(literal.Value));
    }

    [Fact]
    public void Preprocessor_DoesNotStripCommentMarkersInsideQuotedStrings()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let x = "// keep"
            let y = "/* keep */"
            """);

        var first = Assert.IsType<VariableDeclaration>(program.Statements[0]);
        var firstValue = Assert.IsType<LiteralExpression>(first.Value);
        Assert.Equal("// keep", Assert.IsType<string>(firstValue.Value));

        var second = Assert.IsType<VariableDeclaration>(program.Statements[1]);
        var secondValue = Assert.IsType<LiteralExpression>(second.Value);
        Assert.Equal("/* keep */", Assert.IsType<string>(secondValue.Value));
    }

    [Fact]
    public void Preprocessor_StripsLeadingShebangLine()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            #!/usr/bin/env -S lash run
            let x = 1
            """);

        var declaration = Assert.IsType<VariableDeclaration>(Assert.Single(program.Statements));
        Assert.Equal("x", declaration.Name);
    }

    [Fact]
    public void Preprocessor_DirectiveIfElse_KeepsActiveBranchOnly()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            @if true
            let active = 1
            @else
            let inactive = 0
            @endif
            """);

        var declaration = Assert.IsType<VariableDeclaration>(Assert.Single(program.Statements));
        Assert.Equal("active", declaration.Name);
    }

    [Fact]
    public void Preprocessor_DirectiveElif_SelectsFirstMatchingBranch()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            @if false
            let never = 0
            @elif true
            let chosen = 1
            @else
            let also_never = 2
            @endif
            """);

        var declaration = Assert.IsType<VariableDeclaration>(Assert.Single(program.Statements));
        Assert.Equal("chosen", declaration.Name);
    }

    [Fact]
    public void Preprocessor_DirectiveIf_SupportsDefinedWithDefine()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            @define TARGET linux
            @if defined(TARGET) && TARGET == "linux"
            let platform = "ok"
            @endif
            """);

        var declaration = Assert.IsType<VariableDeclaration>(Assert.Single(program.Statements));
        Assert.Equal("platform", declaration.Name);
    }

    [Fact]
    public void Preprocessor_DirectiveIfWithoutEndif_ReportsError()
    {
        var result = TestCompiler.LoadProgram(
            """
            @if true
            let x = 1
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics.GetErrors(), e => e.Message.Contains("Missing '@endif'", StringComparison.Ordinal));
    }
}
