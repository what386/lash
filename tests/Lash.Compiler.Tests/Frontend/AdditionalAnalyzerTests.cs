using Lash.Compiler.Diagnostics;
using Lash.Compiler.Frontend.Semantics;
using Lash.Compiler.Tests.TestSupport;
using Xunit;

namespace Lash.Compiler.Tests.Frontend;

public class AdditionalAnalyzerTests
{
    [Fact]
    public void DefiniteAssignmentAnalyzer_RejectsUseBeforeInitialization()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let x
            let y = $x + 1
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);
        new TypeChecker(diagnostics).Analyze(program);
        new DefiniteAssignmentAnalyzer(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.MaybeUninitializedVariable && e.Message.Contains("may be used before it is initialized", StringComparison.Ordinal));
    }

    [Fact]
    public void ConstantSafetyAnalyzer_RejectsDivisionByZero()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            const z = 0
            let x = 10 / $z
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);
        new TypeChecker(diagnostics).Analyze(program);
        new ConstantSafetyAnalyzer(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.DivisionOrModuloByZero);
    }

    [Fact]
    public void ConstantSafetyAnalyzer_RejectsInvalidShiftAndForStepConstants()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            shift -1
            for i in 0 .. 10 step 0
                echo $i
            end
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);
        new TypeChecker(diagnostics).Analyze(program);
        new ConstantSafetyAnalyzer(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.InvalidShiftAmount);
        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.InvalidForStep);
    }

    [Fact]
    public void CodegenFeasibilityAnalyzer_RejectsUnsupportedValueExpressions()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let x = write() >> "out.log"
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);
        new TypeChecker(diagnostics).Analyze(program);
        new CodegenFeasibilityAnalyzer(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e => e.Code == DiagnosticCodes.UnsupportedExpressionForCodegen);
    }

    [Fact]
    public void CodegenFeasibilityAnalyzer_RejectsHeredocWithNonLiteralPayload()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn feed()
                cat
            end

            let payload = "text"
            feed() << $payload
            """);

        var diagnostics = new DiagnosticBag();
        new NameResolver(diagnostics).Analyze(program);
        new TypeChecker(diagnostics).Analyze(program);
        new CodegenFeasibilityAnalyzer(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.GetErrors(), e =>
            e.Code == DiagnosticCodes.UnsupportedStatementForCodegen
            && e.Message.Contains("Heredoc redirection", StringComparison.Ordinal));
    }

    [Fact]
    public void WarningAnalyzer_EmitsUnreachableShadowingAndWaitJobsWarnings()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let x = 1
            fn demo()
                let x = 2
                return $x
                echo "never"
            end
            wait jobs
            """);

        var diagnostics = new DiagnosticBag();
        new WarningAnalyzer(diagnostics).Analyze(program);

        var warnings = diagnostics.GetWarnings().ToList();
        Assert.Contains(warnings, w => w.Code == DiagnosticCodes.ShadowedVariable);
        Assert.Contains(warnings, w => w.Code == DiagnosticCodes.UnreachableStatement);
        Assert.Contains(warnings, w => w.Code == DiagnosticCodes.WaitJobsWithoutTrackedJobs);
    }

    [Fact]
    public void WarningAnalyzer_EmitsDeadBranchWarningsForConstantIfConditions()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            if true
                echo "then"
            else
                echo "else"
            end
            """);

        var diagnostics = new DiagnosticBag();
        new WarningAnalyzer(diagnostics).Analyze(program);

        Assert.Contains(
            diagnostics.GetWarnings(),
            w => w.Code == DiagnosticCodes.UnreachableStatement
                 && w.Message.Contains("always true", StringComparison.Ordinal));
    }

    [Fact]
    public void WarningAnalyzer_EmitsDeadLoopBodyWarningForConstantFalseWhile()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            while false
                echo "never"
            end
            """);

        var diagnostics = new DiagnosticBag();
        new WarningAnalyzer(diagnostics).Analyze(program);

        Assert.Contains(
            diagnostics.GetWarnings(),
            w => w.Code == DiagnosticCodes.UnreachableStatement
                 && w.Message.Contains("always false", StringComparison.Ordinal));
    }

    [Fact]
    public void WarningAnalyzer_EmitsDeadCaseWarningsForConstantSwitchValue()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            switch 2
                case 1:
                    echo "one"
                case 2:
                    echo "two"
                case 3:
                    echo "three"
            end
            """);

        var diagnostics = new DiagnosticBag();
        new WarningAnalyzer(diagnostics).Analyze(program);

        var warnings = diagnostics.GetWarnings().Where(w => w.Code == DiagnosticCodes.UnreachableStatement).ToList();
        Assert.Contains(warnings, w => w.Message.Contains("can never match", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Message.Contains("earlier case always matches", StringComparison.Ordinal));
    }

    [Fact]
    public void WarningAnalyzer_EvaluatesShortCircuitConditionsForDeadBranchWarnings()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let x = 1
            if false && $x > 0
                echo "never"
            else
                echo "ok"
            end
            """);

        var diagnostics = new DiagnosticBag();
        new WarningAnalyzer(diagnostics).Analyze(program);

        Assert.Contains(
            diagnostics.GetWarnings(),
            w => w.Code == DiagnosticCodes.UnreachableStatement
                 && w.Message.Contains("always false", StringComparison.Ordinal));
    }

    [Fact]
    public void WarningAnalyzer_EmitsEquivalentIfBranchWarning()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            if $flag
                echo "same"
            else
                echo "same"
            end
            """);

        var diagnostics = new DiagnosticBag();
        new WarningAnalyzer(diagnostics).Analyze(program);

        Assert.Contains(
            diagnostics.GetWarnings(),
            w => w.Code == DiagnosticCodes.EquivalentIfBranches
                 && w.Message.Contains("branches are equivalent", StringComparison.Ordinal));
    }

    [Fact]
    public void WarningAnalyzer_EmitsEquivalentBranchAssignmentWarning()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let out = ""
            if $flag
                out = "same"
            else
                out = "same"
            end
            """);

        var diagnostics = new DiagnosticBag();
        new WarningAnalyzer(diagnostics).Analyze(program);

        Assert.Contains(
            diagnostics.GetWarnings(),
            w => w.Code == DiagnosticCodes.EquivalentBranchAssignment
                 && w.Message.Contains("same target and value", StringComparison.Ordinal));
    }

    [Fact]
    public void WarningAnalyzer_EmitsDuplicateSwitchCaseBodyWarning()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            switch $mode
                case "a":
                    echo "same"
                case "b":
                    echo "same"
            end
            """);

        var diagnostics = new DiagnosticBag();
        new WarningAnalyzer(diagnostics).Analyze(program);

        Assert.Contains(
            diagnostics.GetWarnings(),
            w => w.Code == DiagnosticCodes.DuplicateSwitchCaseBody
                 && w.Message.Contains("Duplicate switch case body", StringComparison.Ordinal));
    }

    [Fact]
    public void WarningAnalyzer_EmitsUnusedSymbolWarnings()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn helper(unused_param)
                let unused_local = 1
                return 0
            end

            fn never_called()
                return 1
            end

            let unused_top = 10
            helper(7)
            """);

        var diagnostics = new DiagnosticBag();
        new WarningAnalyzer(diagnostics).Analyze(program);

        var warnings = diagnostics.GetWarnings().ToList();
        Assert.Contains(warnings, w => w.Code == DiagnosticCodes.UnusedVariable && w.Message.Contains("unused_local", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Code == DiagnosticCodes.UnusedVariable && w.Message.Contains("unused_top", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Code == DiagnosticCodes.UnusedParameter && w.Message.Contains("unused_param", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Code == DiagnosticCodes.UnusedFunction && w.Message.Contains("never_called", StringComparison.Ordinal));
    }

    [Fact]
    public void WarningAnalyzer_TreatsInterpolatedLiteralsAsVariableReads()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn exists(path)
                let ok = $(test $"-f {path}")
                return $ok == "1"
            end

            exists("README.md")
            """);

        var diagnostics = new DiagnosticBag();
        new WarningAnalyzer(diagnostics).Analyze(program);

        Assert.DoesNotContain(
            diagnostics.GetWarnings(),
            w => w.Code == DiagnosticCodes.UnusedParameter
                 && w.Message.Contains("path", StringComparison.Ordinal));
    }

    [Fact]
    public void WarningAnalyzer_TreatsCaptureCommandVariableExpansionsAsReads()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let projects = ["a:b"]
            for entry in $projects
                let known = $(printf '%s' "$entry" | cut -d: -f1)
                echo $known
            end
            """);

        var diagnostics = new DiagnosticBag();
        new WarningAnalyzer(diagnostics).Analyze(program);

        Assert.DoesNotContain(
            diagnostics.GetWarnings(),
            w => w.Code == DiagnosticCodes.UnusedVariable
                 && w.Message.Contains("entry", StringComparison.Ordinal));
    }

    [Fact]
    public void WarningAnalyzer_TreatsRawCommandVariableExpansionsAsReads()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let RED = "red"
            let NC = "none"
            echo -e "${RED}hello${NC}"
            """);

        var diagnostics = new DiagnosticBag();
        new WarningAnalyzer(diagnostics).Analyze(program);

        Assert.DoesNotContain(
            diagnostics.GetWarnings(),
            w => w.Code == DiagnosticCodes.UnusedVariable
                 && (w.Message.Contains("RED", StringComparison.Ordinal)
                     || w.Message.Contains("NC", StringComparison.Ordinal)));
    }

    [Fact]
    public void WarningAnalyzer_DoesNotTreatSwitchWithoutDefaultAsAlwaysTerminating()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn parse(value)
                switch $value
                    case "ok":
                        return 1
                end
                return 0
            end

            parse("ok")
            """);

        var diagnostics = new DiagnosticBag();
        new WarningAnalyzer(diagnostics).Analyze(program);

        Assert.DoesNotContain(
            diagnostics.GetWarnings(),
            w => w.Code == DiagnosticCodes.UnreachableStatement
                 && w.Line == 6);
    }
}
