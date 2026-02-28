using Lash.Compiler.Ast.Statements;
using Lash.Compiler.CodeGen;
using Lash.Compiler.Tests.TestSupport;
using Xunit;

namespace Lash.Compiler.Tests.CodeGen;

public class BashGeneratorTests
{
    [Fact]
    public void BashGenerator_EmitsSimpleStringConcat()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let greeting = "Hello" + ", " + "World"
            """);

        var bash = new BashGenerator().Generate(program);

        Assert.Contains("greeting=\"Hello, World\"", bash);
    }

    [Fact]
    public void BashGenerator_FoldsNumericConstExpressions()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let value = (1 + 2) * 3
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("value=9", bash);
    }

    [Fact]
    public void BashGenerator_EmitsArrayIndexReadAndWriteWithoutHelpers()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let items = ["zero", "one"]
            let first = items[0]
            items[1] = "updated"
            """);

        var generator = new BashGenerator();
        var bash = generator.Generate(program);

        Assert.Contains("items=(\"zero\" \"one\")", bash);
        Assert.Contains("first=${items[0]}", bash);
        Assert.Contains("items[1]=\"updated\"", bash);
        Assert.Empty(generator.Warnings);
    }

    [Fact]
    public void BashGenerator_EmitsReadonlyForConstDeclarations()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            const name = "lash"
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("readonly name=\"lash\"", bash);
    }

    [Fact]
    public void BashGenerator_EmitsLocalDeclarationsInsideFunctionsByDefault()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn demo()
                let x = 1
                const y = 2
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("local x=1", bash);
        Assert.Contains("local -r y=2", bash);
    }

    [Fact]
    public void BashGenerator_EmitsGlobalDeclarationWhenRequested()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn demo()
                global let x = 1
                global const y = 2
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.DoesNotContain("local x=1", bash);
        Assert.Contains("x=1", bash);
        Assert.DoesNotContain("local -r y=2", bash);
        Assert.Contains("readonly y=2", bash);
    }

    [Fact]
    public void BashGenerator_EmitsArrayLengthWithHashUnary()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let items = ["zero", "one"]
            let count = #items
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("count=${#items[@]}", bash);
    }

    [Fact]
    public void BashGenerator_EmitsSwitchAsBashCase()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            switch value
                case "a":
                    echo A
                case "b":
                    echo B
            end
            """);

        var bash = new BashGenerator().Generate(program);

        Assert.Contains("case ${value} in", bash);
        Assert.Contains("a)", bash);
        Assert.Contains("b)", bash);
        Assert.Contains("echo A", bash);
        Assert.Contains("echo B", bash);
        Assert.Contains("esac", bash);
    }

    [Fact]
    public void BashGenerator_EmitsSingleWordCommandsAsRawStatements()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            pwd
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("pwd", bash);
        Assert.DoesNotContain("${pwd}", bash);
    }

    [Fact]
    public void BashGenerator_InterpolatesInRawCommandStatements()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let planet = "Mars"
            echo $"Approaching {planet}"
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("echo \"Approaching ${planet}\"", bash);
    }

    [Fact]
    public void BashGenerator_EmitsGlobForLoopAsDirectBashForLoop()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            for file in ./*.txt
                echo $file
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("for file in ./*.txt; do", bash);
        Assert.Contains("echo $file", bash);
    }

    [Fact]
    public void BashGenerator_DoesNotAutoInvokeMainWhenDeclared()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn main()
                echo done
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.DoesNotContain("main \"$@\"", bash);
    }

    [Fact]
    public void BashGenerator_EmitsIfElifElseAsBashConditionChain()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let x = 5
            if x > 10
                echo high
            elif x > 0
                echo mid
            else
                echo low
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("if (( x > 10 )); then", bash);
        Assert.Contains("elif (( x > 0 )); then", bash);
        Assert.Contains("else", bash);
        Assert.Contains("echo high", bash);
        Assert.Contains("echo mid", bash);
        Assert.Contains("echo low", bash);
        Assert.Contains("fi", bash);
    }

    [Fact]
    public void BashGenerator_LowersPipeFunctionStageIntoAssignmentCapture()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn greet(word)
                return "hello-" + word
            end

            let word = "Rob"
            let greeting = ""
            word | greet() | greeting
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("greeting=$(greet \"${word}\")", bash);
        Assert.DoesNotContain("word | greet() | greeting", bash);
    }

    [Fact]
    public void BashGenerator_UsesDirectPositionalBindingForRequiredParams()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn greet(name, greeting = "Hello")
                return greeting + ", " + name
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("local name=\"$1\"", bash);
        Assert.Contains("local greeting=\"${2-}\"", bash);
        Assert.Contains("if (( $# < 2 )); then greeting=\"Hello\"; fi", bash);
    }

    [Fact]
    public void BashGenerator_EmitsGlobalAssignmentInsideFunctionWithoutLocal()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            global let counter = 0
            fn bump()
                global counter = counter + 1
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("counter=0", bash);
        Assert.Contains("counter=$(( ${counter} + 1 ))", bash);
        Assert.DoesNotContain("local counter=", bash);
    }

    [Fact]
    public void BashGenerator_EmitsInterpolatedStringsAsBashExpansion()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let name = "Rob"
            let greeting = $"Hello {name}!"
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("greeting=\"Hello ${name}!\"", bash);
    }

    [Fact]
    public void BashGenerator_FoldsInterpolatedStringWithConstInputs()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            const name = "Rob"
            let greeting = $"Hello {name}!"
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("greeting=\"Hello Rob!\"", bash);
    }

    [Fact]
    public void BashGenerator_LowersEnumAccessToStringLiteral()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            enum AccountType
                Checking
                Savings
            end

            let selected = AccountType::Checking
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.DoesNotContain("enum AccountType", bash);
        Assert.Contains("selected=\"AccountTypeChecking\"", bash);
    }

    [Fact]
    public void BashGenerator_EmitsRedirectionOperators()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn write()
                echo "out"
                echo "err" 1>&2
            end

            fn feed()
                cat
            end

            write() >> "out.log"
            write() 2>> "err.log"
            write() &>> "all.log"
            write() > "out-truncate.log"
            write() 2> "err-truncate.log"
            write() &> "all-truncate.log"
            feed() < "input.log"
            feed() <> "rw.log"
            feed() <<< "payload"
            feed() << [[line1
            line2]]
            feed() 3>&1
            feed() 1>&-
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("write >> \"out.log\"", bash);
        Assert.Contains("write 2>> \"err.log\"", bash);
        Assert.Contains("write &>> \"all.log\"", bash);
        Assert.Contains("write > \"out-truncate.log\"", bash);
        Assert.Contains("write 2> \"err-truncate.log\"", bash);
        Assert.Contains("write &> \"all-truncate.log\"", bash);
        Assert.Contains("feed < \"input.log\"", bash);
        Assert.Contains("feed <> \"rw.log\"", bash);
        Assert.Contains("feed <<< \"payload\"", bash);
        Assert.Contains("feed <<'LASH_HEREDOC'", bash);
        Assert.Contains("line1", bash);
        Assert.Contains("line2", bash);
        Assert.Contains("feed 3>&1", bash);
        Assert.Contains("feed 1>&-", bash);
    }

    [Fact]
    public void BashGenerator_PreservesMultilineRawStringContent()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let raw = [[line1
            echo "still text"
            line3]]
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("raw=\"line1", bash);
        Assert.Contains("echo \\\"still text\\\"", bash);
        Assert.DoesNotContain("__cmd echo \\\"still text\\\"", bash);
    }

    [Fact]
    public void BashGenerator_EmitsBreakAndContinueInLoopBodies()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let keep_looping = true
            while true
                if keep_looping
                    continue
                end
                break
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("while ", bash);
        Assert.Contains("; do", bash);
        Assert.Contains("continue", bash);
        Assert.Contains("break", bash);
        Assert.Contains("done", bash);
    }

    [Fact]
    public void BashGenerator_EmitsUntilLoops()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let i = 0
            until i >= 3
                i = i + 1
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("until ", bash);
        Assert.Contains("; do", bash);
        Assert.Contains("done", bash);
    }

    [Fact]
    public void BashGenerator_EmitsSelectLoops()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            select choice in ["a", "b"]
                break
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("select choice in (\"a\" \"b\"); do", bash);
        Assert.Contains("break", bash);
        Assert.Contains("done", bash);
    }

    [Fact]
    public void BashGenerator_EliminatesConstantDeadIfBranches()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            if false
                echo "never"
            elif true
                echo "chosen"
            else
                echo "also-never"
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("echo \"chosen\"", bash);
        Assert.DoesNotContain("if ", bash);
        Assert.DoesNotContain("elif ", bash);
        Assert.DoesNotContain("else", bash);
        Assert.DoesNotContain("never", bash);
        Assert.DoesNotContain("also-never", bash);
    }

    [Fact]
    public void BashGenerator_EmitsArgvRuntimeFrameAndIndexAccess()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let first = argv[0]
            let count = #argv
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("declare -a __lash_argv=(\"$@\")", bash);
        Assert.Contains("first=${__lash_argv[0]}", bash);
        Assert.Contains("count=${#__lash_argv[@]}", bash);
    }

    [Fact]
    public void BashGenerator_EmitsShiftAsArgvSliceMutation()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn consume()
                shift 2
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("__lash_shift_n=$(( 2 ))", bash);
        Assert.Contains("__lash_argv=(\"${__lash_argv[@]:__lash_shift_n}\")", bash);
    }

    [Fact]
    public void BashGenerator_EmitsShellCaptureExpression()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let size = $sh "du -sh ."
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("size=$(du -sh .)", bash);
    }

    [Fact]
    public void BashGenerator_EmitsShStatementPayloadAsRawCommand()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            sh "bash dothing"
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("bash dothing", bash);
    }

    [Fact]
    public void BashGenerator_EmitsTestStatementPayloadAsBashTest()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            test "-n \"ok\""
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("[[ -n \"ok\" ]]", bash);
    }

    [Fact]
    public void BashGenerator_EmitsTestCaptureAsNumericTruthiness()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let ok = $test "-n \"ok\""
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("ok=$(if [[ -n \"ok\" ]]; then echo 1; else echo 0; fi)", bash);
    }

    [Fact]
    public void BashGenerator_InterpolatesVariablesInShAndShellCapturePayloads()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let name = "ok"
            let value = $sh $"printf '%s' \"{name}\""
            sh $"echo {name}"
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("value=$(printf '%s' \"${name}\")", bash);
        Assert.Contains("echo ${name}", bash);
    }

    [Fact]
    public void BashGenerator_InterpolatesVariablesInsideSingleQuotedShPayloadSegments()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let raw_version = "v1.4.5"
            let version = $sh $"printf '%s' '{raw_version}' | sed 's/^v//'"
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("version=$(printf '%s' ''\"${raw_version}\"'' | sed 's/^v//')", bash);
    }

    [Fact]
    public void BashGenerator_EmitsAssociativeArraySyntaxWhenStringKeysAreUsed()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let meta = []
            meta["name"] = "lash"
            let selected = meta["name"]
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("declare -A meta=()", bash);
        Assert.Contains("meta[\"name\"]=\"lash\"", bash);
        Assert.Contains("selected=${meta[\"name\"]}", bash);
    }

    [Fact]
    public void BashGenerator_EmitsCollectionConcatForPlusEquals()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let items = ["a"]
            items += ["b", "c"]
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("items+=(\"b\" \"c\")", bash);
    }

    [Fact]
    public void BashGenerator_EmitsSubshellAndWaitSyntax()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let pid = 0
            let status = 0
            subshell into pid
                echo hi
            end &
            wait pid into status
            wait jobs into status
            wait
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("declare -a __lash_jobs=()", bash);
        Assert.Contains(") &", bash);
        Assert.Contains("pid=$!", bash);
        Assert.Contains("__lash_jobs+=(\"$!\")", bash);
        Assert.Contains("wait \"${pid}\"", bash);
        Assert.Contains("for __lash_wait_pid in \"${__lash_jobs[@]}\"; do", bash);
        Assert.Contains("wait \"${__lash_wait_pid}\"", bash);
        Assert.Contains("status=$?", bash);
    }

    [Fact]
    public void BashGenerator_EmitsCoprocAndTracksJobsForWaitJobs()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let status = 0
            let pid = 0
            coproc into pid
                echo hi
            end
            wait jobs into status
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("coproc {", bash);
        Assert.Contains("pid=${COPROC_PID}", bash);
        Assert.Contains("__lash_jobs+=(\"${COPROC_PID}\")", bash);
        Assert.Contains("for __lash_wait_pid in \"${__lash_jobs[@]}\"; do", bash);
    }

    [Fact]
    public void BashGenerator_EmitsForegroundSubshellIntoAsExitStatusCapture()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let status = 0
            subshell into status
                echo hi
            end
            """);

        var bash = new BashGenerator().Generate(program);
        Assert.Contains("(", bash);
        Assert.Contains(")", bash);
        Assert.Contains("status=$?", bash);
        Assert.DoesNotContain("status=$!", bash);
    }
}
