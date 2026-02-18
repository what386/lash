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
}
