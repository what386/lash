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
            let y = x + 1
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
            let x = 10 / z
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
    public void WarningAnalyzer_EmitsUnreachableShadowingAndWaitJobsWarnings()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let x = 1
            fn demo()
                let x = 2
                return x
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
}
