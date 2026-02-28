using Lash.Compiler.Tests.TestSupport;
using Xunit;

namespace Lash.Compiler.Tests.Integration;

public class RoundTripTests
{
    [Fact]
    public void RoundTrip_ExecutesInterpolationAndArrayLength()
    {
        var result = CompilerPipeline.Compile(
            """
            let name = "Rob"
            let greeting = $"Hi {name}"
            let items = ["a", "b", "c"]
            let count = #$items
            echo "$greeting"
            echo "$count"
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("Hi Rob\n3\n", run.StdOut);
    }

    [Fact]
    public void RoundTrip_GlobalVariableMutationInsideFunctionPersists()
    {
        var result = CompilerPipeline.Compile(
            """
            global let counter = 0
            fn bump()
                global counter = $counter + 1
            end
            bump()
            bump()
            echo "$counter"
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("2\n", run.StdOut);
    }

    [Fact]
    public void RoundTrip_ForLoopRangeAndGlobalAccumulation()
    {
        var result = CompilerPipeline.Compile(
            """
            global let sum = 0
            for i in 1 .. 3
                global sum = $sum + $i
            end
            echo "$sum"
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("6\n", run.StdOut);
    }

    [Fact]
    public void RoundTrip_PreservesMultilineRawStringContent()
    {
        var result = CompilerPipeline.Compile(
            """
            let raw = [[line1
            echo "still text"
            line3]]
            sh $"printf '%s\n' {raw}"
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("line1\necho \"still text\"\nline3\n", run.StdOut);
    }

    [Fact]
    public void RoundTrip_BareCommandAcceptsMultilineLiteralArgument()
    {
        var result = CompilerPipeline.Compile(
            """
            echo [[line1
            line2]]
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("line1\nline2\n", run.StdOut);
    }

    [Fact]
    public void RoundTrip_CommandCaptureSupportsInterpolationAndSpread()
    {
        var result = CompilerPipeline.Compile(
            """
            let name = "pilot"
            let values = ["a", "b", "c"]
            let greeting = $(printf $"hello-{name}")
            let csv = $(printf '%s,' $values... | sed 's/,$//')
            echo $greeting
            echo $csv
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("hello-pilot\na,b,c\n", run.StdOut);
    }

    [Fact]
    public void RoundTrip_EnumAccessLowersToStableString()
    {
        var result = CompilerPipeline.Compile(
            """
            enum AccountType
                Checking
                Savings
            end

            let selected = AccountType::Checking
            echo "$selected"
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("AccountTypeChecking\n", run.StdOut);
    }

    [Fact]
    public void RoundTrip_AppendRedirections_WorkForStdoutStderrAndCombined()
    {
        var outPath = Path.Combine(Path.GetTempPath(), $"lash-out-{Guid.NewGuid():N}.txt");
        var errPath = Path.Combine(Path.GetTempPath(), $"lash-err-{Guid.NewGuid():N}.txt");
        var allPath = Path.Combine(Path.GetTempPath(), $"lash-all-{Guid.NewGuid():N}.txt");

        try
        {
            var source =
                $"""
                fn produce()
                    echo out
                    echo err 1>&2
                end

                produce() >> "{outPath}"
                produce() 2>> "{errPath}"
                produce() &>> "{allPath}"
                """;

            var result = CompilerPipeline.Compile(source);
            Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
            var bash = Assert.IsType<string>(result.Bash);

            var run = CompilerPipeline.RunBash(bash);
            Assert.Equal(0, run.ExitCode);

            var outText = File.ReadAllText(outPath);
            var errText = File.ReadAllText(errPath);
            var allText = File.ReadAllText(allPath);

            Assert.Contains("out", outText, StringComparison.Ordinal);
            Assert.Contains("err", errText, StringComparison.Ordinal);
            Assert.Contains("out", allText, StringComparison.Ordinal);
            Assert.Contains("err", allText, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(outPath))
                File.Delete(outPath);
            if (File.Exists(errPath))
                File.Delete(errPath);
            if (File.Exists(allPath))
                File.Delete(allPath);
        }
    }

    [Fact]
    public void RoundTrip_TruncatingAndInputRedirections_WorkForStdoutStderrCombinedAndInput()
    {
        var outPath = Path.Combine(Path.GetTempPath(), $"lash-out-truncate-{Guid.NewGuid():N}.txt");
        var errPath = Path.Combine(Path.GetTempPath(), $"lash-err-truncate-{Guid.NewGuid():N}.txt");
        var allPath = Path.Combine(Path.GetTempPath(), $"lash-all-truncate-{Guid.NewGuid():N}.txt");
        var inputPath = Path.Combine(Path.GetTempPath(), $"lash-input-{Guid.NewGuid():N}.txt");

        File.WriteAllText(inputPath, "payload\n");

        try
        {
            var source =
                $"""
                fn produce()
                    echo out
                    echo err 1>&2
                end

                fn feed()
                    cat
                end

                produce() > "{outPath}"
                produce() 2> "{errPath}"
                produce() &> "{allPath}"
                feed() < "{inputPath}"
                feed() <> "{inputPath}"
                """;

            var result = CompilerPipeline.Compile(source);
            Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
            var bash = Assert.IsType<string>(result.Bash);

            var run = CompilerPipeline.RunBash(bash);
            Assert.Equal(0, run.ExitCode);
            Assert.Equal("out\npayload\npayload\n", run.StdOut);

            var outText = File.ReadAllText(outPath);
            var errText = File.ReadAllText(errPath);
            var allText = File.ReadAllText(allPath);

            Assert.Contains("out", outText, StringComparison.Ordinal);
            Assert.DoesNotContain("err", outText, StringComparison.Ordinal);
            Assert.Contains("err", errText, StringComparison.Ordinal);
            Assert.Contains("out", allText, StringComparison.Ordinal);
            Assert.Contains("err", allText, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(outPath))
                File.Delete(outPath);
            if (File.Exists(errPath))
                File.Delete(errPath);
            if (File.Exists(allPath))
                File.Delete(allPath);
            if (File.Exists(inputPath))
                File.Delete(inputPath);
        }
    }

    [Fact]
    public void RoundTrip_HereString_RedirectsInputToFunctionCall()
    {
        var result = CompilerPipeline.Compile(
            """
            fn feed()
                cat
            end

            feed() <<< "payload"
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("payload\n", run.StdOut);
    }

    [Fact]
    public void RoundTrip_Heredoc_RedirectsMultilineInputToFunctionCall()
    {
        var result = CompilerPipeline.Compile(
            """
            fn feed()
                cat
            end

            feed() << [[line1
            line2]]
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("line1\nline2\n", run.StdOut);
    }

    [Fact]
    public void RoundTrip_FdDupRedirection_CanMirrorStdoutToStderrAndPreserveStdout()
    {
        var result = CompilerPipeline.Compile(
            """
            fn emit()
                echo out
            end

            emit() 1>&2
            emit() 3>&1
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("out\n", run.StdOut);
        Assert.Equal("out\n", run.StdErr);
    }

    [Fact]
    public void RoundTrip_BreakAndContinueBehaveLikeBashLoops()
    {
        var result = CompilerPipeline.Compile(
            """
            let i = 0
            while $i < 5
                i = $i + 1
                if $i == 2
                    continue
                end
                if $i == 4
                    break
                end
                echo "$i"
            end
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("1\n3\n", run.StdOut);
    }

    [Fact]
    public void RoundTrip_UntilLoopBehavesLikeBashUntil()
    {
        var result = CompilerPipeline.Compile(
            """
            let i = 0
            until $i >= 3
                i = $i + 1
            end
            echo "$i"
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("3\n", run.StdOut);
    }

    [Fact]
    public void RoundTrip_ArgvIndexLengthAndShiftWork()
    {
        var result = CompilerPipeline.Compile(
            """
            let first = $argv[0]
            shift
            let remaining = #$argv
            echo "$first"
            echo "$remaining"
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash, "alpha", "beta", "gamma");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("alpha\n2\n", run.StdOut);
    }

    [Fact]
    public void RoundTrip_ShellCaptureCapturesOutput()
    {
        var result = CompilerPipeline.Compile(
            """
            let value = $(printf '%s' ok)
            echo "$value"
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("ok\n", run.StdOut);
    }

    [Fact]
    public void RoundTrip_ShAndShellCaptureSupportInterpolation()
    {
        var result = CompilerPipeline.Compile(
            """
            let name = "ok"
            let captured = $(echo $name)
            sh $"echo from-sh-{name}"
            echo "$captured"
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("from-sh-ok\nok\n", run.StdOut);
    }

    [Fact]
    public void RoundTrip_TestStatementAndCaptureWork()
    {
        var result = CompilerPipeline.Compile(
            """
            let value = "ok"
            let is_ok = $(test "-n \"${value}\"")
            if $is_ok
                echo "pass"
            end
            test "-n \"${value}\""
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("pass\n", run.StdOut);
    }

    [Fact]
    public void RoundTrip_TrapIntoFunctionRunsOnExit()
    {
        var result = CompilerPipeline.Compile(
            """
            fn cleanup()
                echo "cleanup"
            end

            trap EXIT into cleanup()
            echo "work"
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("work\ncleanup\n", run.StdOut);
    }

    [Fact]
    public void RoundTrip_ShellCaptureInterpolatesInsideSingleQuotedTemplateSegments()
    {
        var result = CompilerPipeline.Compile(
            """
            let raw_version = "v1.4.5"
            let version = $(echo $raw_version | sed 's/^v//')
            let tag = $"v{version}"
            echo "$raw_version"
            echo "$version"
            echo "$tag"
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("v1.4.5\n1.4.5\nv1.4.5\n", run.StdOut);
    }

    [Fact]
    public void RoundTrip_PipeFunctionStageAssignsToTargetVariable()
    {
        var result = CompilerPipeline.Compile(
            """
            fn greet(word)
                return "hello-" + $word
            end

            let word = "Rob"
            let greeting = ""
            $word | greet() | $greeting
            echo "$greeting"
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("hello-Rob\n", run.StdOut);
    }

    [Fact]
    public void RoundTrip_AssociativeArrayReadWriteWorks()
    {
        var result = CompilerPipeline.Compile(
            """
            let meta = []
            $meta["name"] = "lash"
            let selected = $meta["name"]
            echo "$selected"
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("lash\n", run.StdOut);
    }

    [Fact]
    public void RoundTrip_SwitchCaseGlobPatternMatches()
    {
        var result = CompilerPipeline.Compile(
            """
            let os = "win-11"
            switch $os
                case "win-*":
                    echo match
                case "linux-*":
                    echo no
            end
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("match\n", run.StdOut);
    }

    [Fact]
    public void RoundTrip_BackgroundSubshellWaitByPidCapturesStatus()
    {
        var result = CompilerPipeline.Compile(
            """
            let pid = 0
            let status = 0
            subshell into pid
                sh "sleep 0.05"
            end &
            wait $pid into status
            echo "$status"
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("0\n", run.StdOut);
    }

    [Fact]
    public void RoundTrip_WaitJobsWaitsTrackedBackgroundSubshells()
    {
        var result = CompilerPipeline.Compile(
            """
            let status = 0
            subshell
                sh "sleep 0.03"
            end &
            subshell
                sh "sleep 0.01"
            end &
            wait jobs into status
            echo "$status"
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        var bash = Assert.IsType<string>(result.Bash);

        var run = CompilerPipeline.RunBash(bash);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("0\n", run.StdOut);
    }
}
