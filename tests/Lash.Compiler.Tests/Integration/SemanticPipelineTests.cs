using Lash.Compiler.Tests.TestSupport;
using Xunit;

namespace Lash.Compiler.Tests.Integration;

public class SemanticPipelineTests
{
    [Fact]
    public void SemanticPipeline_RejectsAddingNumberAndString()
    {
        var result = CompilerPipeline.Compile(
            """
            let x = 1
            let y = "hello"
            let z = x + y
            """);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.GetErrors(), e => e.Code == "E100" && e.Message.Contains("Cannot add number and string", StringComparison.Ordinal));
        Assert.Null(result.Bash);
    }

    [Fact]
    public void SemanticPipeline_RejectsAssigningToConst()
    {
        var result = CompilerPipeline.Compile(
            """
            const x = 1
            x = 2
            """);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.GetErrors(), e => e.Code == "E101" && e.Message.Contains("Cannot assign to const variable 'x'", StringComparison.Ordinal));
        Assert.Null(result.Bash);
    }

    [Fact]
    public void SemanticPipeline_RejectsLengthOperatorOnNonArray()
    {
        var result = CompilerPipeline.Compile(
            """
            let x = 42
            let y = #x
            """);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.GetErrors(), e => e.Code == "E100" && e.Message.Contains("Operator '#' expects an array", StringComparison.Ordinal));
        Assert.Null(result.Bash);
    }

    [Fact]
    public void SemanticPipeline_AllowsValidProgram()
    {
        var result = CompilerPipeline.Compile(
            """
            global let x = 1
            fn inc()
                global x = x + 1
            end
            inc()
            """);

        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.GetErrors()));
        Assert.NotNull(result.Bash);
    }

    [Fact]
    public void SemanticPipeline_RejectsUnknownEnumMember()
    {
        var result = CompilerPipeline.Compile(
            """
            enum AccountType
                Checking
            end

            let kind = AccountType::Savings
            """);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.GetErrors(), e => e.Code == "E102" && e.Message.Contains("Unknown enum member 'AccountType::Savings'", StringComparison.Ordinal));
        Assert.Null(result.Bash);
    }

    [Fact]
    public void SemanticPipeline_RejectsBreakOutsideLoop()
    {
        var result = CompilerPipeline.Compile(
            """
            break
            """);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.GetErrors(), e => e.Code == "E105" && e.Message.Contains("'break' can only be used inside a loop", StringComparison.Ordinal));
        Assert.Null(result.Bash);
    }

    [Fact]
    public void SemanticPipeline_RejectsContinueOutsideLoop()
    {
        var result = CompilerPipeline.Compile(
            """
            continue
            """);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.GetErrors(), e => e.Code == "E105" && e.Message.Contains("'continue' can only be used inside a loop", StringComparison.Ordinal));
        Assert.Null(result.Bash);
    }

    [Fact]
    public void SemanticPipeline_RejectsMixedArrayKeyKinds()
    {
        var result = CompilerPipeline.Compile(
            """
            let values = []
            values[0] = "a"
            values["name"] = "b"
            """);

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Contains(result.Diagnostics.GetErrors(), e => e.Code == "E100" && e.Message.Contains("Cannot mix numeric and string keys", StringComparison.Ordinal));
        Assert.Null(result.Bash);
    }
}
