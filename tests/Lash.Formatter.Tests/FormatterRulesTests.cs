using Lash.Formatter;
using Xunit;

namespace Lash.Formatter.Tests;

public class FormatterRulesTests
{
    [Fact]
    public void Formatter_NormalizesIndentationForBlocks()
    {
        const string input =
            """
            fn main()
            let x=1
            if x>0
            echo "ok"
            end
            end
            """;

        const string expected =
            """
            fn main()
                let x = 1
                if x > 0
                    echo "ok"
                end
            end
            """;

        var formatted = LashFormatter.Format(input);
        Assert.Equal(expected.Replace("\r\n", "\n") + "\n", formatted);
    }

    [Fact]
    public void Formatter_AddsSpacingAroundCommasAndOperators()
    {
        const string input = "let a=cmd(\"echo\",\"x\")|cmd(\"wc\",\"-c\")";
        const string expected = "let a = cmd(\"echo\", \"x\") | cmd(\"wc\", \"-c\")\n";

        var formatted = LashFormatter.Format(input);
        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void Formatter_CollapsesRepeatedBlankLines()
    {
        const string input =
            """
            let x=1



            let y=2
            """;

        const string expected =
            """
            let x = 1

            let y = 2
            """;

        var formatted = LashFormatter.Format(input);
        Assert.Equal(expected.Replace("\r\n", "\n") + "\n", formatted);
    }

    [Fact]
    public void Formatter_PreservesInlineCommentDelimiters()
    {
        const string input = "let x=1 // keep inline comment";
        const string expected = "let x = 1 // keep inline comment\n";

        var formatted = LashFormatter.Format(input);
        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void Formatter_PreservesShellRedirectionSyntaxInRawCommandStatements()
    {
        const string input = "echo err 1>&2";
        const string expected = "echo err 1>&2\n";

        var formatted = LashFormatter.Format(input);
        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void Formatter_PreservesSingleWordAndPathCommandStatements()
    {
        const string input =
            """
            pwd
            ./script.sh --mode=fast
            /bin/echo foo=bar
            """;

        const string expected =
            """
            pwd
            ./script.sh --mode=fast
            /bin/echo foo=bar
            """;

        var formatted = LashFormatter.Format(input);
        Assert.Equal(expected.Replace("\r\n", "\n") + "\n", formatted);
    }

    [Fact]
    public void Formatter_FormatsPipeExpressionWithFunctionStage()
    {
        const string input = "word|greet()|greeting";
        const string expected = "word | greet() | greeting\n";

        var formatted = LashFormatter.Format(input);
        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void Formatter_FormatsRedirectionOperatorsAsAtomicTokens()
    {
        const string input =
            """
            write()>>"out.log"
            write()2>>"err.log"
            write()&>>"all.log"
            write()>"out-truncate.log"
            write()2>"err-truncate.log"
            write()&>"all-truncate.log"
            feed()<"input.log"
            feed()<>"rw.log"
            feed()<<<"payload"
            feed()3>&1
            feed()1>&-
            """;

        const string expected =
            """
            write() >> "out.log"
            write() 2>> "err.log"
            write() &>> "all.log"
            write() > "out-truncate.log"
            write() 2> "err-truncate.log"
            write() &> "all-truncate.log"
            feed() < "input.log"
            feed() <> "rw.log"
            feed() <<< "payload"
            feed() 3>&1
            feed() 1>&-
            """;

        var formatted = LashFormatter.Format(input);
        Assert.Equal(expected.Replace("\r\n", "\n") + "\n", formatted);
    }

    [Fact]
    public void Formatter_PreservesEnumAccessOperatorWithoutSpaces()
    {
        const string input = "let selected=AccountType :: Checking";
        const string expected = "let selected = AccountType::Checking\n";

        var formatted = LashFormatter.Format(input);
        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void Formatter_WrapsLongArgumentLists()
    {
        const string input =
            """
            let result = very_long_function_name("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "cccccccccccccccccccccccccccccccc")
            """;

        const string expected =
            """
            let result = very_long_function_name(
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                "cccccccccccccccccccccccccccccccc"
            )
            """;

        var formatted = LashFormatter.Format(input);
        Assert.Equal(expected.Replace("\r\n", "\n") + "\n", formatted);
    }

    [Fact]
    public void Formatter_InsertsBlankLineBetweenTopLevelMajorDeclarations()
    {
        const string input =
            """
            fn a()
            end
            enum Kind
                One
            end
            fn b()
            end
            """;

        const string expected =
            """
            fn a()
            end

            enum Kind
                One
            end

            fn b()
            end
            """;

        var formatted = LashFormatter.Format(input);
        Assert.Equal(expected.Replace("\r\n", "\n") + "\n", formatted);
    }

    [Fact]
    public void Formatter_NormalizesFunctionDeclarationSpacing()
    {
        const string input = "fn  greet  ( name , greeting = \"Hello\" )";
        const string expected = "fn greet(name, greeting = \"Hello\")\n";

        var formatted = LashFormatter.Format(input);
        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void Formatter_IsIdempotent_ForCurrentRules()
    {
        const string input =
            """
            fn main()
            let x=1
            if x>0
            echo "ok"
            end
            end
            """;

        var once = LashFormatter.Format(input);
        var twice = LashFormatter.Format(once);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Formatter_NormalizesIfElifElseIndentation()
    {
        const string input =
            """
            if x>10
            echo "high"
            elif x>0
            echo "mid"
            else
            echo "low"
            end
            """;

        const string expected =
            """
            if x > 10
                echo "high"
            elif x > 0
                echo "mid"
            else
                echo "low"
            end
            """;

        var formatted = LashFormatter.Format(input);
        Assert.Equal(expected.Replace("\r\n", "\n") + "\n", formatted);
    }
}
