using Lash.Compiler.Ast.Expressions;
using Lash.Compiler.Ast.Statements;
using Lash.Compiler.Diagnostics;
using Lash.Compiler.Tests.TestSupport;
using Xunit;

namespace Lash.Compiler.Tests.Frontend;

public class GrammarTests
{
    [Fact]
    public void ModuleLoader_ParsesCoreStatementsAndExpressions()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn greet(name, greeting = "Hello")
                return greeting + ", " + name
            end

            let items = ["a", "b"]
            const first = items[0]
            let count = 2 + 3

            switch first
                case "a":
                    echo ok
            end
            """);

        Assert.Contains(program.Statements, s => s is FunctionDeclaration { Name: "greet" });
        Assert.Contains(program.Statements, s => s is VariableDeclaration { Name: "items", Value: ArrayLiteral });
        Assert.Contains(program.Statements, s => s is VariableDeclaration { Name: "first", Kind: VariableDeclaration.VarKind.Const });
        Assert.Contains(program.Statements, s => s is VariableDeclaration { Name: "count", Value: BinaryExpression { Operator: "+" } });
        Assert.Contains(program.Statements, s => s is SwitchStatement { Cases.Count: 1 });
    }

    [Fact]
    public void ModuleLoader_ParsesGlobalVariableDeclarationAndAssignment()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            global let counter = 0
            fn bump()
                global counter = counter + 1
            end
            """);

        var globalDecl = Assert.IsType<VariableDeclaration>(program.Statements[0]);
        Assert.True(globalDecl.IsGlobal);

        var fn = Assert.IsType<FunctionDeclaration>(program.Statements[1]);
        var globalAssign = Assert.IsType<Assignment>(Assert.Single(fn.Body));
        Assert.True(globalAssign.IsGlobal);
    }

    [Fact]
    public void ModuleLoader_RewritesBareShellLinesToCommandStatements()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            echo "hello"
            cat "file.txt" | echo
            """);

        Assert.Equal(2, program.Statements.Count);
        Assert.All(program.Statements, s => Assert.IsType<CommandStatement>(s));
        Assert.Contains(program.Statements, s => s is CommandStatement { Script: "echo \"hello\"" });
        Assert.Contains(program.Statements, s => s is CommandStatement { Script: "cat \"file.txt\" | echo" });
    }

    [Fact]
    public void ModuleLoader_RewritesBareCommandContainingMultilineLiteral()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            echo [[line1
            line2]]
            """);

        var command = Assert.IsType<CommandStatement>(Assert.Single(program.Statements));
        Assert.Equal("echo $'line1\\nline2'", command.Script);
    }

    [Fact]
    public void ModuleLoader_RewritesSingleWordAndPathCommands()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            pwd
            ./scripts/build/build.sh
            /bin/echo hi
            """);

        Assert.Equal(3, program.Statements.Count);
        Assert.All(program.Statements, s => Assert.IsType<CommandStatement>(s));
        Assert.Contains(program.Statements, s => s is CommandStatement { Script: "pwd" });
        Assert.Contains(program.Statements, s => s is CommandStatement { Script: "./scripts/build/build.sh" });
        Assert.Contains(program.Statements, s => s is CommandStatement { Script: "/bin/echo hi" });
    }

    [Fact]
    public void ModuleLoader_ParsesPipeWithFunctionStageAsExpressionStatement()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let word = "Rob"
            let greeting = ""
            word | greet() | greeting
            """);

        var statement = Assert.IsType<ExpressionStatement>(program.Statements[2]);
        var outerPipe = Assert.IsType<PipeExpression>(statement.Expression);
        Assert.IsType<IdentifierExpression>(outerPipe.Right);
        var innerPipe = Assert.IsType<PipeExpression>(outerPipe.Left);
        Assert.IsType<FunctionCallExpression>(innerPipe.Right);
    }

    [Fact]
    public void ModuleLoader_ExpandsInlineCaseBodies()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            switch x
                case 1: echo "one"
            end
            """);

        var switchStatement = Assert.IsType<SwitchStatement>(Assert.Single(program.Statements));
        var clause = Assert.Single(switchStatement.Cases);
        Assert.Single(clause.Body);
        Assert.IsType<CommandStatement>(clause.Body[0]);
    }

    [Fact]
    public void ModuleLoader_ParsesLoopsAndIndexAssignment()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let items = ["a", "b", "c"]
            for i in 0 .. 2 step 1
                items[i] = "x"
            end
            while #items > 0
                break
            end
            until #items == 0
                continue
            end
            """);

        Assert.Contains(program.Statements, s => s is ForLoop { Body.Count: 1 });
        Assert.Contains(program.Statements, s => s is WhileLoop);
        Assert.Contains(program.Statements, s => s is UntilLoop);
    }

    [Fact]
    public void ModuleLoader_ParsesGlobForLoop()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            for file in ./*.txt
                echo $file
            end
            """);

        var loop = Assert.IsType<ForLoop>(Assert.Single(program.Statements));
        Assert.Equal("file", loop.Variable);
        Assert.Null(loop.Range);
        Assert.Equal("./*.txt", loop.GlobPattern);
        Assert.Null(loop.Step);
        Assert.Single(loop.Body);
    }

    [Fact]
    public void ModuleLoader_ParsesIfWithElifAndElseBlocks()
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

        var ifStatement = Assert.IsType<IfStatement>(program.Statements[1]);
        Assert.Single(ifStatement.ThenBlock);
        Assert.Single(ifStatement.ElifClauses);
        Assert.Single(ifStatement.ElifClauses[0].Body);
        Assert.Single(ifStatement.ElseBlock);
    }

    [Fact]
    public void ModuleLoader_ParsesInterpolatedAndRawStrings()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let name = "Rob"
            let greeting = $"Hi {name}"
            let raw = [[line1
            "line2"
            line3]]
            """);

        var interpolated = Assert.IsType<VariableDeclaration>(program.Statements[1]);
        Assert.True(Assert.IsType<LiteralExpression>(interpolated.Value).IsInterpolated);

        var raw = Assert.IsType<VariableDeclaration>(program.Statements[2]);
        Assert.True(Assert.IsType<LiteralExpression>(raw.Value).IsMultiline);
    }

    [Fact]
    public void ModuleLoader_ParsesEnumDeclarationAndEnumAccess()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            enum AccountType
                Checking
                Savings
            end

            let kind = AccountType::Checking
            """);

        var enumDeclaration = Assert.IsType<EnumDeclaration>(program.Statements[0]);
        Assert.Equal("AccountType", enumDeclaration.Name);
        Assert.Equal(new[] { "Checking", "Savings" }, enumDeclaration.Members);

        var variable = Assert.IsType<VariableDeclaration>(program.Statements[1]);
        var enumAccess = Assert.IsType<EnumAccessExpression>(variable.Value);
        Assert.Equal("AccountType", enumAccess.EnumName);
        Assert.Equal("Checking", enumAccess.MemberName);
    }

    [Fact]
    public void ModuleLoader_ParsesRedirectExpressions()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            write() >> "out.log"
            warn() 2>> "err.log"
            both() &>> "combined.log"
            warn() 2> "err-truncate.log"
            both() &> "combined-truncate.log"
            readwrite() <> "rw.log"
            feed() <<< "payload"
            feed() << [[line1
            line2]]
            dup() 3>&1
            closeout() 1>&-
            """);

        Assert.All(program.Statements, s =>
        {
            var expr = Assert.IsType<ExpressionStatement>(s).Expression;
            var redirect = Assert.IsType<RedirectExpression>(expr);
            Assert.IsType<FunctionCallExpression>(redirect.Left);
        });

        var first = (RedirectExpression)((ExpressionStatement)program.Statements[0]).Expression;
        var second = (RedirectExpression)((ExpressionStatement)program.Statements[1]).Expression;
        var third = (RedirectExpression)((ExpressionStatement)program.Statements[2]).Expression;
        var fourth = (RedirectExpression)((ExpressionStatement)program.Statements[3]).Expression;
        var fifth = (RedirectExpression)((ExpressionStatement)program.Statements[4]).Expression;
        var sixth = (RedirectExpression)((ExpressionStatement)program.Statements[5]).Expression;
        var seventh = (RedirectExpression)((ExpressionStatement)program.Statements[6]).Expression;
        var eighth = (RedirectExpression)((ExpressionStatement)program.Statements[7]).Expression;
        var ninth = (RedirectExpression)((ExpressionStatement)program.Statements[8]).Expression;
        var tenth = (RedirectExpression)((ExpressionStatement)program.Statements[9]).Expression;
        Assert.Equal(">>", first.Operator);
        Assert.Equal("2>>", second.Operator);
        Assert.Equal("&>>", third.Operator);
        Assert.Equal("2>", fourth.Operator);
        Assert.Equal("&>", fifth.Operator);
        Assert.Equal("<>", sixth.Operator);
        Assert.Equal("<<<", seventh.Operator);
        Assert.Equal("<<", eighth.Operator);
        Assert.Equal("3>&1", ninth.Operator);
        Assert.Equal("1>&-", tenth.Operator);
    }

    [Fact]
    public void ModuleLoader_ParsesStdoutAndStdinTruncatingRedirectionAsBinaryExpressions()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            write() > "out.log"
            feed() < "in.log"
            """);

        var first = Assert.IsType<BinaryExpression>(((ExpressionStatement)program.Statements[0]).Expression);
        var second = Assert.IsType<BinaryExpression>(((ExpressionStatement)program.Statements[1]).Expression);

        Assert.Equal(">", first.Operator);
        Assert.Equal("<", second.Operator);
        Assert.IsType<FunctionCallExpression>(first.Left);
        Assert.IsType<FunctionCallExpression>(second.Left);
    }

    [Fact]
    public void ModuleLoader_ParsesBreakAndContinueStatements()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            while true
                continue
                break
            end
            """);

        var loop = Assert.IsType<WhileLoop>(Assert.Single(program.Statements));
        Assert.IsType<ContinueStatement>(loop.Body[0]);
        Assert.IsType<BreakStatement>(loop.Body[1]);
    }

    [Fact]
    public void ModuleLoader_ParsesShiftShellCaptureShStatementAndAppendAssignment()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            fn demo()
                shift 2
                let size = $sh "du -sh ."
                sh $"echo {size}"
                let items = []
                items += ["x"]
            end
            """);

        var fn = Assert.IsType<FunctionDeclaration>(Assert.Single(program.Statements));
        Assert.IsType<ShiftStatement>(fn.Body[0]);

        var sizeDeclaration = Assert.IsType<VariableDeclaration>(fn.Body[1]);
        Assert.IsType<ShellCaptureExpression>(sizeDeclaration.Value);
        Assert.IsType<ShellStatement>(fn.Body[2]);

        var append = Assert.IsType<Assignment>(fn.Body[4]);
        Assert.Equal("+=", append.Operator);
    }

    [Fact]
    public void ModuleLoader_ParsesSubshellAndWaitStatements()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let pid = 0
            let status = 0
            subshell into pid
                echo "hi"
            end &
            wait pid into status
            wait jobs
            wait into status
            subshell into const const_pid
                echo "bye"
            end &
            wait into let local_status
            """);

        var subshell = Assert.IsType<SubshellStatement>(program.Statements[2]);
        Assert.Equal("pid", subshell.IntoVariable);
        Assert.Equal(IntoBindingMode.Auto, subshell.IntoMode);
        Assert.True(subshell.RunInBackground);
        Assert.Single(subshell.Body);

        var waitPid = Assert.IsType<WaitStatement>(program.Statements[3]);
        Assert.Equal(WaitTargetKind.Target, waitPid.TargetKind);
        Assert.Equal("status", waitPid.IntoVariable);
        Assert.Equal(IntoBindingMode.Auto, waitPid.IntoMode);
        var waitTarget = Assert.IsType<IdentifierExpression>(waitPid.Target);
        Assert.Equal("pid", waitTarget.Name);

        var waitJobs = Assert.IsType<WaitStatement>(program.Statements[4]);
        Assert.Equal(WaitTargetKind.Jobs, waitJobs.TargetKind);
        Assert.Null(waitJobs.Target);

        var waitAll = Assert.IsType<WaitStatement>(program.Statements[5]);
        Assert.Equal(WaitTargetKind.Default, waitAll.TargetKind);
        Assert.Equal("status", waitAll.IntoVariable);

        var constSubshell = Assert.IsType<SubshellStatement>(program.Statements[6]);
        Assert.Equal("const_pid", constSubshell.IntoVariable);
        Assert.Equal(IntoBindingMode.Const, constSubshell.IntoMode);

        var letWait = Assert.IsType<WaitStatement>(program.Statements[7]);
        Assert.Equal("local_status", letWait.IntoVariable);
        Assert.Equal(IntoBindingMode.Let, letWait.IntoMode);
    }

    [Fact]
    public void ModuleLoader_ParsesStringKeyedIndexAssignment()
    {
        var program = TestCompiler.ParseOrThrow(
            """
            let values = []
            values["name"] = "lash"
            """);

        var assignment = Assert.IsType<Assignment>(program.Statements[1]);
        var index = Assert.IsType<IndexAccessExpression>(assignment.Target);
        Assert.IsType<LiteralExpression>(index.Index);
    }

    [Fact]
    public void ModuleLoader_RejectsLegacyExternalCommandSubstitutionSyntax()
    {
        var result = TestCompiler.LoadProgram(
            """
            let size = $(du("-sh", "."))
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics.GetErrors(), d => d.Code == DiagnosticCodes.ParseSyntaxError);
    }
}
